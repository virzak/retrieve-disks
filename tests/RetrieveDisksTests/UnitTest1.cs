namespace RetrieveDisksTests;

public class DeviceInfoTests
{
    [Fact]
    public void GetDeviceInfos()
    {
        var list = DeviceInfo.GetDeviceInfos();
        Assert.True(list.Count > 0);
    }
}