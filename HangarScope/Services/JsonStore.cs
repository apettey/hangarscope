using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HangarScope.Services;

/// <summary>JSON file persistence under %APPDATA%\HangarScope, with DPAPI encryption for secrets.</summary>
public sealed class JsonStore
{
    public string Root { get; }
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public JsonStore()
    {
        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HangarScope");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "cache"));
    }

    public string PathFor(string name) => Path.Combine(Root, name);

    public T Load<T>(string name) where T : new()
    {
        var p = PathFor(name);
        if (!File.Exists(p)) return new T();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(p), Opts) ?? new T(); }
        catch { return new T(); }
    }

    public void Save<T>(string name, T value)
    {
        var p = PathFor(name);
        File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(value, Opts));
        File.Move(p + ".tmp", p, overwrite: true);
    }

    public void Delete(string name)
    {
        var p = PathFor(name);
        if (File.Exists(p)) File.Delete(p);
    }

    public long CacheSizeBytes()
    {
        try
        {
            return new DirectoryInfo(Path.Combine(Root, "cache"))
                .EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch { return 0; }
    }

    public void PurgeCache()
    {
        var dir = Path.Combine(Root, "cache");
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir)) File.Delete(f);
        }
        catch { /* best effort */ }
    }

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        if (!OperatingSystem.IsWindows()) return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    public static string Unprotect(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return "";
        try
        {
            var bytes = Convert.FromBase64String(cipher);
            if (!OperatingSystem.IsWindows()) return Encoding.UTF8.GetString(bytes);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch { return ""; }
    }
}
