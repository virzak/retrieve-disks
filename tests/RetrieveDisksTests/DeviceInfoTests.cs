namespace RetrieveDisksTests;

public class DeviceInfoTests
{
    [Fact]
    public void GetDeviceInfos()
    {
        var list = DeviceInfo.GetDeviceInfos();
        Assert.True(list.Count > 0);
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData(@"\\?\PhysicalDrive0")]
    public void GetStreamLength(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var streamLength = stream.Length;
        Assert.True(streamLength > 0);
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData(@"\\?\PhysicalDrive0")]
    public async Task ReadBeginningAndEnd(string path)
    {
        var byteCount = 4096;
        var bytesBeginning = new byte[byteCount];
        var byteEnd = new byte[byteCount];

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await stream.ReadAsync(bytesBeginning.AsMemory());

        // Band aid solution to get length
        long streamLength;
        try
        {
            streamLength = stream.Length;
        }
        catch (IOException)
        {
            streamLength = DeviceInfo.GetDeviceInfo(path).DiskLength;
        }

        stream.Seek(streamLength - byteCount, SeekOrigin.Begin);
        await stream.ReadAsync(byteEnd.AsMemory());
    }
}