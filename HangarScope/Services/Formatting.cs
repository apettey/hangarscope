using System.Globalization;

namespace HangarScope.Services;

public static class Formatting
{
    /// <summary>ISK abbreviation per design spec: 2.60 B / 812.0 M / 41.7 K; sub-10 values get 2 decimals.</summary>
    public static string Isk(double n)
    {
        if (n >= 1e9) return (n / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + " B";
        if (n >= 1e6) return (n / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M";
        if (n >= 1e3) return (n / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + " K";
        return n.ToString(n < 10 ? "0.00" : "0", CultureInfo.InvariantCulture);
    }

    public static string Signed(double n) => (n >= 0 ? "+" : "−") + Isk(Math.Abs(n));

    public static string Qty(long n) => n.ToString("N0", CultureInfo.GetCultureInfo("en-US"));

    public static string Ago(DateTimeOffset? t)
    {
        if (t is null) return "never";
        var d = DateTimeOffset.UtcNow - t.Value;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 48) return $"{(int)d.TotalHours} h ago";
        return $"{(int)d.TotalDays} d ago";
    }

    public static string In(DateTimeOffset? t)
    {
        if (t is null) return "—";
        var d = t.Value - DateTimeOffset.UtcNow;
        if (d.TotalSeconds <= 0) return "expired";
        if (d.TotalMinutes < 60) return $"in {(int)Math.Ceiling(d.TotalMinutes)} min";
        return $"in {(int)Math.Ceiling(d.TotalHours)} h";
    }
}
