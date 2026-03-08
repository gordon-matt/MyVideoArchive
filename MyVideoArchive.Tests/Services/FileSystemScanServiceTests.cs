using Microsoft.Extensions.DependencyInjection;

namespace MyVideoArchive.Tests.Services;

public class FileSystemScanServiceTests
{
    [Fact]
    public void Cancel_DelegatesToStateAndReturnsSuccess()
    {
        var state = new MyVideoArchive.Services.Content.FileSystemScanStateService();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var service = new FileSystemScanService(
            NullLogger<FileSystemScanService>.Instance,
            state,
            scopeFactory.Object);

        var result = service.Cancel();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GetStatus_DelegatesToState()
    {
        var state = new MyVideoArchive.Services.Content.FileSystemScanStateService();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var service = new FileSystemScanService(
            NullLogger<FileSystemScanService>.Instance,
            state,
            scopeFactory.Object);

        object status = service.GetStatus();

        Assert.NotNull(status);
    }

    [Fact]
    public async Task StartChannelScanAsync_WhenNotRunning_ReturnsStarted()
    {
        var state = new MyVideoArchive.Services.Content.FileSystemScanStateService();
        var scope = new Mock<IServiceScope>();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(s => s.CreateScope()).Returns(scope.Object);
        var service = new FileSystemScanService(
            NullLogger<FileSystemScanService>.Instance,
            state,
            scopeFactory.Object);

        var result = await service.StartChannelScanAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(ScanStartOutcome.Started, result.Value);
    }

    [Fact]
    public async Task StartChannelScanAsync_WhenAlreadyRunning_ReturnsAlreadyRunning()
    {
        var state = new MyVideoArchive.Services.Content.FileSystemScanStateService();
        state.TryStart(out _);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var service = new FileSystemScanService(
            NullLogger<FileSystemScanService>.Instance,
            state,
            scopeFactory.Object);

        var result = await service.StartChannelScanAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(ScanStartOutcome.AlreadyRunning, result.Value);
    }

    [Fact]
    public async Task StartScanAsync_WhenNotRunning_ReturnsStarted()
    {
        var state = new MyVideoArchive.Services.Content.FileSystemScanStateService();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var service = new FileSystemScanService(
            NullLogger<FileSystemScanService>.Instance,
            state,
            scopeFactory.Object);

        var result = await service.StartScanAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(ScanStartOutcome.Started, result.Value);
    }
}