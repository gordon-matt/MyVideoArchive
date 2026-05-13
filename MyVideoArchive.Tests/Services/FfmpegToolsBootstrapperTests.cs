namespace MyVideoArchive.Tests.Services;

public class FfmpegToolsBootstrapperTests
{
    [Fact]
    public async Task ConfigureXabeAsync_WithEmptyConfiguration_CompletesWithoutThrowing() => await FfmpegToolsBootstrapper.ConfigureXabeAsync(
        new ConfigurationBuilder().Build(),
        NullLogger.Instance,
        CancellationToken.None);
}