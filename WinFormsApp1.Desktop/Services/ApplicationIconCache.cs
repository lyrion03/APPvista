using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WinFormsApp1.Desktop.Services;

public sealed class ApplicationIconCache
{
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ApplicationIconCache(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(_cacheDirectory);
    }

    public string GetIconPath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return string.Empty;
        }

        var normalizedPath = Path.GetFullPath(executablePath);
        return _cache.GetOrAdd(normalizedPath, CreateIconCacheEntry) ?? string.Empty;
    }

    private string? CreateIconCacheEntry(string executablePath)
    {
        try
        {
            var cacheFilePath = Path.Combine(_cacheDirectory, ComputeCacheFileName(executablePath));
            if (File.Exists(cacheFilePath))
            {
                return cacheFilePath;
            }

            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            bitmap.Save(cacheFilePath, ImageFormat.Png);
            return cacheFilePath;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeCacheFileName(string executablePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(executablePath));
        return Convert.ToHexString(bytes) + ".png";
    }
}
