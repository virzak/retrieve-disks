using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;
using System.Text;
using static Windows.Win32.PInvoke;

namespace RetrieveDisks;

public record DeviceInfo(string DevicePath, uint DeviceNumber, long DiskLength, uint SectorSize, string? Vendor, string? ProductId, string? ProductRevision, string? SerialNumber)
{
    /// <summary>
    /// Gets the description.
    /// </summary>
    public string? Description => $"{Vendor ?? string.Empty} {ProductId ?? string.Empty}".Trim();

    /// <summary>
    /// Gets the access path.
    /// </summary>
    public string Path => $@"\\?\PhysicalDrive{DeviceNumber}";

    private static string AsciiBytesToString(Span<byte> buffer, int offset)
    {
        int end = offset;
        while (end < buffer.Length && buffer[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(buffer[offset..end]);
    }

    private static string UnicodeBytesToString(Span<byte> buffer, int offset)
    {
        int end = offset;
        while (end < buffer.Length - 1 && (buffer[end] != 0 || buffer[end + 1] != 0))
        {
            end += 2;
        }

        return Encoding.Unicode.GetString(buffer[offset..end]);
    }

    /// <summary>
    /// Gets a list of pysical devices connected to the computer.
    /// </summary>
    /// <returns>List of devices.</returns>
    public static IList<DeviceInfo> GetDeviceInfos()
    {
        var devicePaths = DevicePaths();
        var list = new List<DeviceInfo>();
        foreach (var devicePath in devicePaths)
        {
            list.Add(GetDeviceInfo(devicePath));
        }

        return list;
    }

    // From https://stackoverflow.com/a/18183115/6461844
    private static unsafe IList<string> DevicePaths()
    {
        var list = new List<string>();
        var diskClassDeviceInterfaceGuid = GUID_DEVINTERFACE_DISK;

        using var diskClassDevices = SetupDiGetClassDevs(
            diskClassDeviceInterfaceGuid,
            null,
            (HWND)IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        var deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA()
        {
            cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DATA),
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

            if ((WIN32_ERROR)Marshal.GetLastWin32Error() != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
            {
                throw new Win32Exception();
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
                    throw new Win32Exception();
                }

                var offset = (byte*)&deviceInterfaceDetailData->DevicePath - pBuffer;
                list.Add(UnicodeBytesToString(buffer, (int)offset));
            }
        }

        return list;
    }

    /// <summary>
    /// Gets all relevant device information.
    /// </summary>
    /// <param name="devicePath">Device path (\\?\PhysicalDrive0).</param>
    /// <returns>DeviceInfo structure containing all relevant information.</returns>
    /// <exception cref="Win32Exception">When API call fails.</exception>
    public static unsafe DeviceInfo GetDeviceInfo(string devicePath)
    {
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
            throw new Win32Exception();
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

        // Get size https://stackoverflow.com/a/38855953/6461844
        var diskGeometeryEx = default(DISK_GEOMETRY_EX);

        if (!DeviceIoControl(
            disk,
            IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
            null,
            0,
            &diskGeometeryEx,
            (uint)sizeof(DISK_GEOMETRY_EX),
            null,
            null))
        {
            throw new Win32Exception();
        }

        // https://stackoverflow.com/a/48250301/6461844
        var spq = new STORAGE_PROPERTY_QUERY
        {
            PropertyId = STORAGE_PROPERTY_ID.StorageDeviceProperty,
            QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery,
        };

        string? productRevision = null;
        string? productId = null;
        string? vendor = null;
        string? serialNumber = null;

        WIN32_ERROR dwError = WIN32_ERROR.NO_ERROR;
        var size = (uint)sizeof(STORAGE_DEVICE_DESCRIPTOR) + 0x100;
        do
        {
            var buffer = new byte[size];
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
                    throw new Win32Exception();
                }

                var psdd = (STORAGE_DEVICE_DESCRIPTOR*)pBuffer;

                if (psdd->Size > size)
                {
                    size = psdd->Size;
                    dwError = WIN32_ERROR.ERROR_MORE_DATA;
                }
                else
                {
                    var debug = Encoding.ASCII.GetString(buffer);
                    serialNumber = psdd->SerialNumberOffset == 0 ? null : AsciiBytesToString(buffer, (int)psdd->SerialNumberOffset).TrimEnd();
                    vendor = psdd->VendorIdOffset == 0 ? null : AsciiBytesToString(buffer, (int)psdd->VendorIdOffset).TrimEnd();
                    productId = AsciiBytesToString(buffer, (int)psdd->ProductIdOffset).TrimEnd();
                    productRevision = AsciiBytesToString(buffer, (int)psdd->ProductRevisionOffset).TrimEnd();
                    dwError = WIN32_ERROR.NO_ERROR;
                }
            }
        }
        while (dwError == WIN32_ERROR.ERROR_MORE_DATA);

        return new(
            devicePath,
            diskNumber.DeviceNumber,
            diskGeometeryEx.DiskSize,
            diskGeometeryEx.Geometry.BytesPerSector,
            vendor,
            productId,
            productRevision,
            serialNumber);
    }
}
