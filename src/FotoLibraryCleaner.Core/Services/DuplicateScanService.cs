using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FotoLibraryCleaner.Core.Models;

namespace FotoLibraryCleaner.Core.Services;

public sealed class DuplicateScanService : IDuplicateScanService
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".bmp",
    ];

    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<DuplicateGroup>> ScanAsync(ScanOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.SourceFolder))
        {
            throw new InvalidOperationException("Please choose a source folder before starting the scan.");
        }

        if (!Directory.Exists(options.SourceFolder))
        {
            throw new DirectoryNotFoundException($"Source folder not found: {options.SourceFolder}");
        }

        return await Task.Run(() => ScanCore(options, null, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.SourceFolder))
        {
            throw new InvalidOperationException("Please choose a source folder before starting the scan.");
        }

        if (!Directory.Exists(options.SourceFolder))
        {
            throw new DirectoryNotFoundException($"Source folder not found: {options.SourceFolder}");
        }

        return await Task.Run(() => ScanCore(options, progress, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<DuplicateGroup> ScanCore(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var paths = CollectImagePaths(options, cancellationToken);
        progress?.Report(new ScanProgress(
            "Discovering",
            paths.Count,
            0,
            0,
            0,
            paths.Count,
            paths.Count,
            $"Discovered {paths.Count} supported image files."));

        var analyzedImages = AnalyzeImages(paths, options, progress, cancellationToken);

        var groups = new List<DuplicateGroup>();
        var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactGroupIndex = 1;
        var similarGroupIndex = 1;

        foreach (var md5Group in analyzedImages
                     .GroupBy(image => image.Md5, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var groupImages = md5Group
                .OrderByDescending(image => image.PixelCount)
                .ThenByDescending(image => image.FileSizeBytes)
                .ToList();

            foreach (var image in groupImages)
            {
                processedPaths.Add(image.Path);
            }

            groups.Add(BuildDuplicateGroup(groupImages, $"EX-{exactGroupIndex:0000}", isExactMatch: true));
            exactGroupIndex++;
            progress?.Report(new ScanProgress(
                "Grouping",
                paths.Count,
                analyzedImages.Count,
                paths.Count - analyzedImages.Count,
                groups.Count,
                groups.Count,
                analyzedImages.Count,
                $"Built {groups.Count} duplicate groups so far."));
        }

        var remaining = analyzedImages
            .Where(image => !processedPaths.Contains(image.Path))
            .ToList();

        var candidateBuckets = options.UseFastMode
            ? remaining.GroupBy(image => image.PerceptualHash >> 48).Select(group => group.ToList()).ToList()
            : [remaining];

        foreach (var bucket in candidateBuckets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var i = 0; i < bucket.Count; i++)
            {
                var current = bucket[i];
                if (processedPaths.Contains(current.Path))
                {
                    continue;
                }

                var groupImages = new List<AnalyzedImage> { current };

                for (var j = i + 1; j < bucket.Count; j++)
                {
                    var candidate = bucket[j];
                    if (processedPaths.Contains(candidate.Path))
                    {
                        continue;
                    }

                    var distance = HammingDistance(current.PerceptualHash, candidate.PerceptualHash);
                    if (distance <= options.Threshold)
                    {
                        groupImages.Add(candidate);
                    }
                }

                if (groupImages.Count <= 1)
                {
                    continue;
                }

                foreach (var image in groupImages)
                {
                    processedPaths.Add(image.Path);
                }

                groups.Add(BuildDuplicateGroup(groupImages, $"PH-{similarGroupIndex:0000}", isExactMatch: false));
                similarGroupIndex++;
                progress?.Report(new ScanProgress(
                    "Grouping",
                    paths.Count,
                    analyzedImages.Count,
                    paths.Count - analyzedImages.Count,
                    groups.Count,
                    groups.Count,
                    analyzedImages.Count,
                    $"Built {groups.Count} duplicate groups so far."));
            }
        }

        progress?.Report(new ScanProgress(
            "Completed",
            paths.Count,
            analyzedImages.Count,
            paths.Count - analyzedImages.Count,
            groups.Count,
            paths.Count,
            paths.Count,
            groups.Count == 0
                ? "Scan completed without duplicates."
                : $"Scan completed with {groups.Count} duplicate groups."));

        return groups
            .OrderByDescending(group => group.EstimatedSavingsBytes)
            .ThenBy(group => group.GroupId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> CollectImagePaths(ScanOptions options, CancellationToken cancellationToken)
    {
        var searchOption = options.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var imagePaths = Directory.EnumerateFiles(options.SourceFolder, "*.*", searchOption)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();
        return imagePaths;
    }

    private static List<AnalyzedImage> AnalyzeImages(
        IReadOnlyList<string> paths,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<AnalyzedImage>();
        var cachePath = GetAnalysisCachePath(options);
        var cache = LoadAnalysisCache(cachePath, options);
        var cacheGate = new object();
        var remainingPaths = new List<string>();
        var cachedCount = 0;
        var analyzedCount = 0;
        var skippedCount = 0;

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(path);
            if (TryGetCachedImage(cache, fileInfo, out var cachedImage))
            {
                results.Add(cachedImage);
                cachedCount++;
                analyzedCount++;
            }
            else
            {
                remainingPaths.Add(path);
            }
        }

        if (cachedCount > 0)
        {
            progress?.Report(new ScanProgress(
                "Analyzing",
                paths.Count,
                analyzedCount,
                0,
                0,
                analyzedCount,
                paths.Count,
                $"Reused {cachedCount} cached image analyses. {remainingPaths.Count} images left to analyze."));
        }

        try
        {
            Parallel.ForEach(
                remainingPaths,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                },
                path =>
                {
                    if (TryAnalyzeImage(path, options.HashAlgorithm, out var image))
                    {
                        results.Add(image);
                        lock (cacheGate)
                        {
                            cache.Items[image.Path] = ToCacheItem(image);
                        }

                        var current = Interlocked.Increment(ref analyzedCount);
                        progress?.Report(new ScanProgress(
                            "Analyzing",
                            paths.Count,
                            current,
                            skippedCount,
                            0,
                            current,
                            paths.Count,
                            $"Analyzed {current} of {paths.Count} images."));
                    }
                    else
                    {
                        var skipped = Interlocked.Increment(ref skippedCount);
                        progress?.Report(new ScanProgress(
                            "Analyzing",
                            paths.Count,
                            analyzedCount,
                            skipped,
                            0,
                            analyzedCount + skipped,
                            paths.Count,
                            $"Analyzed {analyzedCount} images, skipped {skipped}."));
                    }
                });
        }
        catch (OperationCanceledException)
        {
            lock (cacheGate)
            {
                SaveAnalysisCache(cachePath, cache);
            }

            throw;
        }

        lock (cacheGate)
        {
            SaveAnalysisCache(cachePath, cache);
        }

        return results
            .OrderBy(image => image.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AnalysisCache LoadAnalysisCache(string cachePath, ScanOptions options)
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                return CreateAnalysisCache(options);
            }

            var cache = JsonSerializer.Deserialize<AnalysisCache>(File.ReadAllText(cachePath), CacheJsonOptions);
            if (cache is null
                || cache.SchemaVersion != 1
                || !string.Equals(cache.SourceFolder, options.SourceFolder, StringComparison.OrdinalIgnoreCase)
                || cache.IncludeSubfolders != options.IncludeSubfolders
                || cache.HashAlgorithm != options.HashAlgorithm)
            {
                return CreateAnalysisCache(options);
            }

            return cache;
        }
        catch
        {
            return CreateAnalysisCache(options);
        }
    }

    private static AnalysisCache CreateAnalysisCache(ScanOptions options)
    {
        return new AnalysisCache(
            1,
            options.SourceFolder,
            options.IncludeSubfolders,
            options.HashAlgorithm,
            new Dictionary<string, CachedAnalyzedImage>(StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryGetCachedImage(AnalysisCache cache, FileInfo fileInfo, out AnalyzedImage image)
    {
        image = default!;

        if (!cache.Items.TryGetValue(fileInfo.FullName, out var cached)
            || cached.FileSizeBytes != fileInfo.Length
            || cached.LastWriteTimeUtcTicks != fileInfo.LastWriteTimeUtc.Ticks)
        {
            return false;
        }

        image = new AnalyzedImage(
            cached.Path,
            cached.Format,
            cached.FileSizeBytes,
            cached.LastWriteTimeUtcTicks,
            cached.Width,
            cached.Height,
            cached.PixelCount,
            cached.Md5,
            cached.PerceptualHash,
            cached.TakenAt);

        return true;
    }

    private static CachedAnalyzedImage ToCacheItem(AnalyzedImage image)
    {
        return new CachedAnalyzedImage(
            image.Path,
            image.Format,
            image.FileSizeBytes,
            image.LastWriteTimeUtcTicks,
            image.Width,
            image.Height,
            image.PixelCount,
            image.Md5,
            image.PerceptualHash,
            image.TakenAt);
    }

    private static void SaveAnalysisCache(string cachePath, AnalysisCache cache)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.WriteAllText(cachePath, JsonSerializer.Serialize(cache, CacheJsonOptions));
    }

    private static string GetAnalysisCachePath(ScanOptions options) => ScanAnalysisCache.GetCachePath(options);

    private static bool TryAnalyzeImage(string path, PhotoHashAlgorithm hashAlgorithm, out AnalyzedImage image)
    {
        image = default!;

        try
        {
            var fileInfo = new FileInfo(path);
            var bytes = File.ReadAllBytes(path);
            using var memoryStream = new MemoryStream(bytes, writable: false);
            using var bitmap = new Bitmap(memoryStream);

            var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
            var perceptualHash = ComputeHash(bitmap, hashAlgorithm);
            var takenAt = TryReadTakenAt(bitmap, fileInfo);

            image = new AnalyzedImage(
                path,
                GetFormat(path),
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks,
                bitmap.Width,
                bitmap.Height,
                bitmap.Width * bitmap.Height,
                md5,
                perceptualHash,
                takenAt);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DuplicateGroup BuildDuplicateGroup(IReadOnlyList<AnalyzedImage> images, string groupId, bool isExactMatch)
    {
        var ordered = images
            .OrderByDescending(image => image.PixelCount)
            .ThenByDescending(image => image.FileSizeBytes)
            .ThenBy(image => image.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var best = ordered[0];
        var maxPixelCount = ordered.Max(image => image.PixelCount);
        var maxFileSize = ordered.Max(image => image.FileSizeBytes);

        var primary = ToCandidate(
            best,
            best,
            distance: 0,
            qualityScore: 100,
            suggestedAction: ReviewAction.Keep,
            reason: isExactMatch ? "Exact duplicate set. Keeping the highest-quality file." : "Best quality in duplicate group.");

        var matches = ordered
            .Skip(1)
            .Select(image =>
            {
                var distance = isExactMatch ? 0 : HammingDistance(best.PerceptualHash, image.PerceptualHash);
                var reason = BuildReason(best, image, distance, isExactMatch);
                var qualityScore = ComputeQualityScore(image, maxPixelCount, maxFileSize);
                var action = distance == 0 || image.PixelCount < best.PixelCount || image.FileSizeBytes < best.FileSizeBytes
                    ? ReviewAction.Move
                    : ReviewAction.Skip;

                return new PhotoDuplicateMatch(
                    ToCandidate(image, best, distance, qualityScore, action, reason),
                    distance);
            })
            .ToList();

        return new DuplicateGroup(
            groupId,
            primary,
            matches,
            matches.Count == 0 ? 0 : matches.Max(match => match.Distance),
            matches.Sum(match => match.Candidate.FileSizeBytes),
            isExactMatch);
    }

    private static PhotoCandidate ToCandidate(
        AnalyzedImage image,
        AnalyzedImage best,
        int distance,
        int qualityScore,
        ReviewAction suggestedAction,
        string reason)
    {
        return new PhotoCandidate(
            image.Path,
            image.Format,
            image.FileSizeBytes,
            image.Width,
            image.Height,
            image.PixelCount,
            image.Md5,
            image.PerceptualHash.ToString("x16"),
            qualityScore,
            image.TakenAt,
            suggestedAction,
            reason);
    }

    private static string BuildReason(AnalyzedImage best, AnalyzedImage candidate, int distance, bool isExactMatch)
    {
        if (isExactMatch)
        {
            return "Same binary content as the selected keep file.";
        }

        if (candidate.PixelCount < best.PixelCount)
        {
            return $"Lower resolution copy (distance: {distance}).";
        }

        if (candidate.FileSizeBytes < best.FileSizeBytes)
        {
            return $"Smaller file size than the selected keep file (distance: {distance}).";
        }

        return $"Visually similar candidate that should be reviewed (distance: {distance}).";
    }

    private static int ComputeQualityScore(AnalyzedImage image, int maxPixelCount, long maxFileSize)
    {
        if (maxPixelCount <= 0 || maxFileSize <= 0)
        {
            return 0;
        }

        var pixelRatio = (double)image.PixelCount / maxPixelCount;
        var fileSizeRatio = (double)image.FileSizeBytes / maxFileSize;
        var score = (pixelRatio * 0.75d) + (fileSizeRatio * 0.25d);

        return Math.Clamp((int)Math.Round(score * 100d, MidpointRounding.AwayFromZero), 1, 100);
    }

    private static DateTimeOffset? TryReadTakenAt(Image image, FileInfo fileInfo)
    {
        var exifDate = TryReadExifDate(image, 0x9003)
            ?? TryReadExifDate(image, 0x0132);

        if (exifDate is not null)
        {
            return exifDate;
        }

        return fileInfo.LastWriteTimeUtc == DateTime.MinValue
            ? null
            : new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime();
    }

    private static DateTimeOffset? TryReadExifDate(Image image, int propertyId)
    {
        try
        {
            if (!image.PropertyIdList.Contains(propertyId))
            {
                return null;
            }

            var property = image.GetPropertyItem(propertyId)!;
            if (property.Value is not { Length: > 0 } value)
            {
                return null;
            }

            var raw = System.Text.Encoding.ASCII.GetString(value).Trim('\0', ' ');
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTime.TryParseExact(
                    raw,
                    "yyyy:MM:dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal,
                    out var parsed))
            {
                return new DateTimeOffset(parsed);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static ulong ComputeHash(Bitmap source, PhotoHashAlgorithm hashAlgorithm)
    {
        return hashAlgorithm switch
        {
            PhotoHashAlgorithm.PerceptualHash => ComputePerceptualHash(source),
            _ => ComputeDifferenceHash(source),
        };
    }

    private static ulong ComputeDifferenceHash(Bitmap source)
    {
        using var resized = new Bitmap(9, 8, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(source, 0, 0, 9, 8);
        }

        Span<byte> values = stackalloc byte[72];

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                var pixel = resized.GetPixel(x, y);
                var grayscale = (byte)Math.Clamp((pixel.R + pixel.G + pixel.B) / 3, 0, 255);
                values[(y * 9) + x] = grayscale;
            }
        }

        ulong hash = 0;
        var index = 0;

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var left = values[(y * 9) + x];
                var right = values[(y * 9) + x + 1];
                if (left > right)
                {
                    hash |= 1UL << index;
                }

                index++;
            }
        }

        return hash;
    }

    private static ulong ComputePerceptualHash(Bitmap source)
    {
        const int sampleSize = 32;
        const int hashSize = 8;

        using var resized = new Bitmap(sampleSize, sampleSize, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(source, 0, 0, sampleSize, sampleSize);
        }

        var pixels = new double[sampleSize, sampleSize];
        for (var y = 0; y < sampleSize; y++)
        {
            for (var x = 0; x < sampleSize; x++)
            {
                var pixel = resized.GetPixel(x, y);
                pixels[x, y] = (pixel.R * 0.299d) + (pixel.G * 0.587d) + (pixel.B * 0.114d);
            }
        }

        var coefficients = new double[hashSize * hashSize];
        var index = 0;
        for (var v = 0; v < hashSize; v++)
        {
            for (var u = 0; u < hashSize; u++)
            {
                coefficients[index++] = ComputeDctCoefficient(pixels, u, v, sampleSize);
            }
        }

        var median = coefficients
            .Skip(1)
            .Order()
            .ElementAt((coefficients.Length - 1) / 2);

        ulong hash = 0;
        for (var bit = 0; bit < coefficients.Length; bit++)
        {
            if (coefficients[bit] > median)
            {
                hash |= 1UL << bit;
            }
        }

        return hash;
    }

    private static double ComputeDctCoefficient(double[,] pixels, int u, int v, int sampleSize)
    {
        var sum = 0d;

        for (var y = 0; y < sampleSize; y++)
        {
            for (var x = 0; x < sampleSize; x++)
            {
                sum += pixels[x, y]
                    * Math.Cos(((2 * x) + 1) * u * Math.PI / (2 * sampleSize))
                    * Math.Cos(((2 * y) + 1) * v * Math.PI / (2 * sampleSize));
            }
        }

        var scaleU = u == 0 ? 1d / Math.Sqrt(2d) : 1d;
        var scaleV = v == 0 ? 1d / Math.Sqrt(2d) : 1d;

        return 0.25d * scaleU * scaleV * sum;
    }

    private static int HammingDistance(ulong left, ulong right)
    {
        var value = left ^ right;
        var count = 0;

        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    private static string GetFormat(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "JPEG",
            ".png" => "PNG",
            ".gif" => "GIF",
            ".bmp" => "BMP",
            _ => "Unknown",
        };
    }

    private sealed record AnalyzedImage(
        string Path,
        string Format,
        long FileSizeBytes,
        long LastWriteTimeUtcTicks,
        int Width,
        int Height,
        int PixelCount,
        string Md5,
        ulong PerceptualHash,
        DateTimeOffset? TakenAt);

    private sealed record AnalysisCache(
        int SchemaVersion,
        string SourceFolder,
        bool IncludeSubfolders,
        PhotoHashAlgorithm HashAlgorithm,
        Dictionary<string, CachedAnalyzedImage> Items);

    private sealed record CachedAnalyzedImage(
        string Path,
        string Format,
        long FileSizeBytes,
        long LastWriteTimeUtcTicks,
        int Width,
        int Height,
        int PixelCount,
        string Md5,
        ulong PerceptualHash,
        DateTimeOffset? TakenAt);
}
