using HangarScope.Services;

namespace HangarScope.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.3.0", "V1.3.0")]
    [InlineData("1.3.0+02f88ad1c0ffee", "V1.3.0")]
    [InlineData("0.0.1-ci.42+abc", "V0.0.1-CI.42")]
    [InlineData("0.0.0-dev", "DEV BUILD")]
    [InlineData("0.0.0-dev+abc", "DEV BUILD")]
    [InlineData("", "DEV BUILD")]
    [InlineData(null, "DEV BUILD")]
    public void Formats_version_for_display(string? informational, string expected) =>
        Assert.Equal(expected, AppVersion.Format(informational));

    [Fact]
    public void Display_reads_the_app_assemblys_version()
    {
        // the SDK always stamps an informational version (csproj default 0.0.0-dev, or CI's -p:Version)
        var informational = AppVersion.Informational;
        Assert.False(string.IsNullOrWhiteSpace(informational));
        Assert.Equal(AppVersion.Format(informational), AppVersion.Display);
        var version = typeof(AppVersion).Assembly.GetName().Version!;
        Assert.StartsWith($"{version.Major}.{version.Minor}.{version.Build}", informational);
    }
}
