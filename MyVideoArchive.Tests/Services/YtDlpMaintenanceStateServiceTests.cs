namespace MyVideoArchive.Tests.Services;

public class YtDlpMaintenanceStateServiceTests
{
    [Fact]
    public void TryStart_WhenNotRunning_ReturnsTrueAndSetsState()
    {
        var service = new YtDlpMaintenanceStateService();

        bool started = service.TryStart(YtDlpMaintenanceOperation.Update);

        Assert.True(started);
        Assert.True(service.IsRunning);
        Assert.Equal(YtDlpMaintenanceOperation.Update, service.CurrentOperation);
    }

    [Fact]
    public void TryStart_WhenAlreadyRunning_ReturnsFalseAndKeepsOriginalOperation()
    {
        var service = new YtDlpMaintenanceStateService();
        service.TryStart(YtDlpMaintenanceOperation.Update);

        bool startedAgain = service.TryStart(YtDlpMaintenanceOperation.Rollback);

        Assert.False(startedAgain);
        Assert.Equal(YtDlpMaintenanceOperation.Update, service.CurrentOperation);
    }

    [Fact]
    public void EnsureStarted_WhenNotRunning_StartsIt()
    {
        var service = new YtDlpMaintenanceStateService();

        service.EnsureStarted(YtDlpMaintenanceOperation.Rollback);

        Assert.True(service.IsRunning);
        Assert.Equal(YtDlpMaintenanceOperation.Rollback, service.CurrentOperation);
    }

    [Fact]
    public void EnsureStarted_WhenAlreadyRunning_DoesNotChangeOperation()
    {
        var service = new YtDlpMaintenanceStateService();
        service.TryStart(YtDlpMaintenanceOperation.Update);

        service.EnsureStarted(YtDlpMaintenanceOperation.Rollback);

        Assert.Equal(YtDlpMaintenanceOperation.Update, service.CurrentOperation);
    }

    [Fact]
    public void Complete_ClearsRunningStateAndRecordsResult()
    {
        var service = new YtDlpMaintenanceStateService();
        service.TryStart(YtDlpMaintenanceOperation.Update);

        var result = new YtDlpMaintenanceResult(
            YtDlpMaintenanceOperation.Update, true, "Updated", "1.0", "2.0", DateTime.UtcNow);
        service.Complete(result);

        Assert.False(service.IsRunning);
        Assert.Null(service.CurrentOperation);
        Assert.Same(result, service.LastResult);
    }
}
