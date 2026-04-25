using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace APPvista.Desktop.Services;

public sealed class ApplicationIconCache
{
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _pending = new(StringComparer.OrdinalIgnoreCase);

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
        if (_cache.TryGetValue(normalizedPath, out var cachedPath))
        {
            return cachedPath ?? string.Empty;
        }

        var cacheFilePath = Path.Combine(_cacheDirectory, ComputeCacheFileName(normalizedPath));
        if (File.Exists(cacheFilePath))
        {
            _cache[normalizedPath] = cacheFilePath;
            return cacheFilePath;
        }

        _pending.GetOrAdd(normalizedPath, path => Task.Run(() =>
        {
            try
            {
                _cache[path] = CreateIconCacheEntry(path);
            }
            finally
            {
                _pending.TryRemove(path, out _);
            }
        }));

        return string.Empty;
    }

    public string GetIconPathImmediate(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return string.Empty;
        }

        var normalizedPath = Path.GetFullPath(executablePath);
        if (_cache.TryGetValue(normalizedPath, out var cachedPath))
        {
            return cachedPath ?? string.Empty;
        }

        var cacheFilePath = Path.Combine(_cacheDirectory, ComputeCacheFileName(normalizedPath));
        if (File.Exists(cacheFilePath))
        {
            _cache[normalizedPath] = cacheFilePath;
            return cacheFilePath;
        }

        var createdPath = CreateIconCacheEntry(normalizedPath);
        _cache[normalizedPath] = createdPath;
        return createdPath ?? string.Empty;
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
