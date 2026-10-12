using Microsoft.Extensions.Configuration;

namespace MyVideoArchive.Tests.Services;

public class BetaFeatureServiceTests
{
    private static IConfiguration BuildConfiguration(string? value)
    {
        var settings = new Dictionary<string, string?>();
        if (value is not null)
        {
            settings[BetaFeatureService.ConfigurationKey] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("not-a-bool", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    public void IsEnabled_ReadsConfiguration_DefaultingToFalse(string? value, bool expected)
    {
        var service = new BetaFeatureService(BuildConfiguration(value));

        Assert.Equal(expected, service.IsEnabled);
    }

    [Theory]
    [InlineData("Odysee")]
    [InlineData("Rumble")]
    [InlineData("rumble")]
    public void IsPlatformAvailable_BetaPlatform_FollowsBetaSetting(string platform)
    {
        Assert.False(new BetaFeatureService(false).IsPlatformAvailable(platform));
        Assert.True(new BetaFeatureService(true).IsPlatformAvailable(platform));
    }

    [Theory]
    [InlineData("YouTube")]
    [InlineData("BitChute")]
    [InlineData("Custom")]
    public void IsPlatformAvailable_StablePlatform_AlwaysAvailable(string platform)
    {
        Assert.True(new BetaFeatureService(false).IsPlatformAvailable(platform));
        Assert.True(new BetaFeatureService(true).IsPlatformAvailable(platform));
    }
}
