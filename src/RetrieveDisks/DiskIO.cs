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

            var devicePath = System.Text.Encoding.Unicode.GetString(buffer[DevicePathOffset..]);

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
        }
    }
}
