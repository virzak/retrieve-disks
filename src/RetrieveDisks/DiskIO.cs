// From https://stackoverflow.com/a/18183115/6461844

using System.Runtime.InteropServices;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ioctl;
using Windows.Win32.Storage.FileSystem;
using static Windows.Win32.PInvoke;

namespace RetrieveDisks;

public static class DiskIO
{
    static readonly int DevicePathOffset = (int)(nint)Marshal.OffsetOf<SP_DEVICE_INTERFACE_DETAIL_DATA_W>(nameof(SP_DEVICE_INTERFACE_DETAIL_DATA_W.DevicePath));


    // https://stackoverflow.com/questions/144176/fastest-way-to-convert-a-possibly-null-terminated-ascii-byte-to-a-string
    public static string UnsafeAsciiBytesToString(byte[] buffer, int offset)
    {
        int end = offset;
        while (end < buffer.Length && buffer[end] != 0)
        {
            end++;
        }
        unsafe
        {
            fixed (byte* pAscii = buffer)
            {
                return new string((sbyte*)pAscii, offset, end - offset);
            }
        }
    }


    public unsafe static void OpenDisks()
    {
        var diskClassDeviceInterfaceGuid = GUID_DEVINTERFACE_DISK;

        using var diskClassDevices = SetupDiGetClassDevs(
            diskClassDeviceInterfaceGuid,
            null,
            (HWND)IntPtr.Zero,
            DIGCF_PRESENT |
            DIGCF_DEVICEINTERFACE);
        var deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA()
        {
            cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DATA)
        };
        uint deviceIndex = 0;

        while (SetupDiEnumDeviceInterfaces(
            diskClassDevices,
            null,
            diskClassDeviceInterfaceGuid,
            deviceIndex,
            ref deviceInterfaceData))
        {

            ++deviceIndex;

            uint requiredSize;
            SetupDiGetDeviceInterfaceDetail(
                diskClassDevices,
                deviceInterfaceData,
                null,
                0,
                &requiredSize,
                null);

            if (WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER != (WIN32_ERROR)Marshal.GetLastWin32Error())
            {
                // Error
                return;
            }

            var buffer = new byte[requiredSize];
            fixed (byte* pBuffer = buffer)
            {
                var deviceInterfaceDetailData = (SP_DEVICE_INTERFACE_DETAIL_DATA_W*)pBuffer;
                deviceInterfaceDetailData->cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);

                if (!SetupDiGetDeviceInterfaceDetail(
                    diskClassDevices,
                    deviceInterfaceData,
                    deviceInterfaceDetailData,
                    requiredSize,
                    null,
                    null))
                {
                    // Error
                    return;
                }
            }

            var devicePath = System.Text.Encoding.Unicode.GetString(buffer[DevicePathOffset..]).TrimEnd((char)0);

            using var disk = CreateFile(
                devicePath,
                FILE_ACCESS_FLAGS.FILE_GENERIC_READ,
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                null,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
                null);

            if (disk.IsInvalid)
            {
                // Error
                return;
            }

            STORAGE_DEVICE_NUMBER diskNumber;
            DeviceIoControl(
                disk,
                IOCTL_STORAGE_GET_DEVICE_NUMBER,
                null,
                0,
                &diskNumber,
                (uint)sizeof(STORAGE_DEVICE_NUMBER),
                null,
                null);

            var diskname = "\\\\?\\PhysicalDrive" + diskNumber.DeviceNumber;

            // Get size https://stackoverflow.com/a/38855953/6461844
            var storage_read_capacity = new STORAGE_READ_CAPACITY();

            if (!DeviceIoControl(
                disk,
                IOCTL_STORAGE_READ_CAPACITY,
                null,
                0,
                &storage_read_capacity,
                4096,
                null,
                null))
            {
                // Error
                return;
            }

            // https://stackoverflow.com/a/48250301/6461844
            var spq = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = STORAGE_PROPERTY_ID.StorageDeviceProperty,
                QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery
            };


            WIN32_ERROR dwError = WIN32_ERROR.NO_ERROR;
            var size = (uint)sizeof(STORAGE_DEVICE_DESCRIPTOR) + 0x100;
            do
            {
                buffer = new byte[size];
                uint bytesReturned;

                fixed (void* pBuffer = buffer)
                {
                    if (!DeviceIoControl(
                        disk,
                        IOCTL_STORAGE_QUERY_PROPERTY,
                        &spq,
                        (uint)sizeof(STORAGE_PROPERTY_QUERY),
                        pBuffer,
                        size,
                        &bytesReturned,
                        null))
                    {
                        // Error
                        return;
                    }
                    var psdd = (STORAGE_DEVICE_DESCRIPTOR*)pBuffer;

                    if (psdd->Size > size)
                    {
                        size = psdd->Size;
                        dwError = WIN32_ERROR.ERROR_MORE_DATA;
                    }
                    else
                    {
                        var debug = System.Text.Encoding.ASCII.GetString(buffer);
                        var serial = UnsafeAsciiBytesToString(buffer, (int)psdd->SerialNumberOffset).TrimEnd();
                        var vendor = UnsafeAsciiBytesToString(buffer, (int)psdd->VendorIdOffset).TrimEnd();
                        var productId = UnsafeAsciiBytesToString(buffer, (int)psdd->ProductIdOffset).TrimEnd();
                        var productRevision = UnsafeAsciiBytesToString(buffer, (int)psdd->ProductRevisionOffset).TrimEnd();
                        dwError = WIN32_ERROR.NO_ERROR;
                    }
                }
            } while (dwError == WIN32_ERROR.ERROR_MORE_DATA);
        }
    }
}
