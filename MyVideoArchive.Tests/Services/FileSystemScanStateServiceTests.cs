using MyVideoArchive.Models;

namespace MyVideoArchive.Tests.Services;

public class FileSystemScanStateServiceTests
{
    [Fact]
    public void TryStart_WhenNotRunning_ReturnsTrueAndSetsIsRunning()
    {
        var service = new FileSystemScanStateService();
        bool started = service.TryStart(out var ct);
        Assert.True(started);
        Assert.True(service.IsRunning);
        Assert.True(ct.CanBeCanceled);
    }

    [Fact]
    public void TryStart_WhenAlreadyRunning_ReturnsFalse()
    {
        var service = new FileSystemScanStateService();
        service.TryStart(out _);
        bool startedAgain = service.TryStart(out var ct);
        Assert.False(startedAgain);
        Assert.False(ct.CanBeCanceled);
    }

    [Fact]
    public void Complete_SetsIsRunningFalseAndLastResult()
    {
        var service = new FileSystemScanStateService();
        service.TryStart(out _);
        var result = new FileSystemScanResult
        {
            NewVideos = 1
        };
        service.Complete(result);
        Assert.False(service.IsRunning);
        Assert.NotNull(service.LastResult);
        Assert.Equal(1, service.LastResult.NewVideos);
    }

    [Fact]
    public void Fail_SetsIsRunningFalseAndErrorMessage()
    {
        var service = new FileSystemScanStateService();
        service.TryStart(out _);
        service.Fail("Something went wrong");
        Assert.False(service.IsRunning);
        Assert.Equal("Something went wrong", service.ErrorMessage);
    }

    [Fact]
    public void UpdateProgress_SetsProgress()
    {
        var service = new FileSystemScanStateService();
        var progress = new FileSystemScanProgress { TotalChannels = 5, ProcessedChannels = 2 };
        service.UpdateProgress(progress);
        Assert.NotNull(service.Progress);
        Assert.Equal(5, service.Progress.TotalChannels);
        Assert.Equal(2, service.Progress.ProcessedChannels);
    }

    [Fact]
    public void Cancel_DoesNotThrowWhenNotStarted()
    {
        var service = new FileSystemScanStateService();
        service.Cancel();
    }

    [Fact]
    public void GetStatus_ReturnsAnonymousObjectWithExpectedShape()
    {
        var service = new FileSystemScanStateService();
        object status = service.GetStatus();
        Assert.NotNull(status);
        var dict = status.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(status));
        Assert.True(dict.ContainsKey("isRunning"));
        Assert.True(dict.ContainsKey("progress"));
        Assert.True(dict.ContainsKey("lastResult"));
        Assert.True(dict.ContainsKey("errorMessage"));
    }
}