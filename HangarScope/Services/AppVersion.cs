using System.Reflection;

namespace HangarScope.Services;

/// <summary>
/// The running app's version for display. Releases are built with <c>-p:Version=X.Y.Z</c> by CI; local builds keep the
/// csproj default <c>0.0.0-dev</c> and show as a dev build. Pure formatting so it is covered by HangarScope.Tests.
/// </summary>
public static class AppVersion
{
    public const string DevLabel = "DEV BUILD";

    /// <summary>Display text for the running app, e.g. "V1.3.0" or "DEV BUILD".</summary>
    public static string Display { get; } = Format(Informational);

    /// <summary>HangarScope's own informational version (the app assembly, not whichever host loaded it).</summary>
    public static string? Informational =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>
    /// "1.3.0+abc123" → "V1.3.0"; "0.0.1-ci.42" → "V0.0.1-CI.42"; "0.0.0-dev", empty or missing → "DEV BUILD".
    /// The "+commit" suffix the SDK appends is dropped.
    /// </summary>
    public static string Format(string? informationalVersion)
    {
        var v = informationalVersion?.Trim() ?? "";
        var plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        if (v.Length == 0 || v.EndsWith("-dev", StringComparison.OrdinalIgnoreCase)) return DevLabel;
        return "V" + v.ToUpperInvariant();
    }
}
