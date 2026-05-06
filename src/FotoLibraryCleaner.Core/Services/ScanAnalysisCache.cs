using System.Security.Cryptography;
using System.Text;
using FotoLibraryCleaner.Core.Models;

namespace FotoLibraryCleaner.Core.Services;

public static class ScanAnalysisCache
{
    public static string GetCachePath(ScanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GetCachePath(options.SourceFolder, options.IncludeSubfolders, options.HashAlgorithm);
    }

    public static string GetCachePath(string sourceFolder, bool includeSubfolders, PhotoHashAlgorithm hashAlgorithm)
    {
        var key = $"{sourceFolder}|{includeSubfolders}|{hashAlgorithm}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FotoLibraryCleaner",
            "scan-cache",
            $"analysis-{hash}.json");
    }

    public static bool HasCache(ScanOptions options)
    {
        return File.Exists(GetCachePath(options));
    }

    public static bool DeleteCache(ScanOptions options)
    {
        var cachePath = GetCachePath(options);
        if (!File.Exists(cachePath))
        {
            return false;
        }

        File.Delete(cachePath);
        return true;
    }
}
