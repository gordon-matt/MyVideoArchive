using MyVideoArchive.Models;

namespace MyVideoArchive.Tests;

public class FileSystemScanProgressTests
{
    [Fact]
    public void PercentComplete_SingleChannel_UsesInnerWorkFraction()
    {
        var p = new FileSystemScanProgress
        {
            TotalChannels = 1,
            ProcessedChannels = 0,
            CurrentChannelWorkProcessed = 50,
            CurrentChannelWorkTotal = 200
        };

        Assert.Equal(25, p.PercentComplete);
    }

    [Fact]
    public void PercentComplete_MultiChannel_BlendsCompletedAndCurrent()
    {
        var p = new FileSystemScanProgress
        {
            TotalChannels = 4,
            ProcessedChannels = 1,
            CurrentChannelWorkProcessed = 50,
            CurrentChannelWorkTotal = 100
        };

        Assert.Equal(38, p.PercentComplete);
    }

    [Fact]
    public void PercentComplete_ClampedTo100()
    {
        var p = new FileSystemScanProgress
        {
            TotalChannels = 1,
            ProcessedChannels = 1,
            CurrentChannelWorkProcessed = 99,
            CurrentChannelWorkTotal = 100
        };

        Assert.Equal(100, p.PercentComplete);
    }
}
