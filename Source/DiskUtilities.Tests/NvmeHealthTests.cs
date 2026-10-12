using DiskHealth.Services;

namespace DiskUtilities.Tests;

public class NvmeHealthTests
{
    [Fact]
    public void ParseSmartLog_ReadsCoreFields()
    {
        var log = new byte[512];
        log[0] = 0x01; // critical warning bit
        // Temperature Kelvin 310 → 37 °C
        log[1] = 310 & 0xFF;
        log[2] = (310 >> 8) & 0xFF;
        log[3] = 95; // available spare
        log[4] = 10; // threshold
        log[5] = 12; // percentage used

        var health = NvmeHealthReader.ParseSmartLog(log);
        Assert.True(health.HasCriticalWarning);
        Assert.Equal(37, health.TemperatureCelsius);
        Assert.Equal(95, health.AvailableSparePercent);
        Assert.Equal(10, health.AvailableSpareThreshold);
        Assert.Equal(12, health.PercentageUsed);
    }

    [Fact]
    public void IsLikelyNvme_DetectsPnpAndModel()
    {
        Assert.True(NvmeHealthReader.IsLikelyNvme("SCSI", @"PCI\VEN_144D&DEV_A808\NVME&DISK", "Samsung SSD 980"));
        Assert.False(NvmeHealthReader.IsLikelyNvme("IDE", @"IDE\DISKST1000", "ST1000DM"));
    }
}
