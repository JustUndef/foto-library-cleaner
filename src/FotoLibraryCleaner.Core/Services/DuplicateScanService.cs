using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
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

        return await Task.Run(() => ScanCore(options, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<DuplicateGroup> ScanCore(ScanOptions options, CancellationToken cancellationToken)
    {
        var paths = CollectImagePaths(options, cancellationToken);
        var analyzedImages = AnalyzeImages(paths, cancellationToken);

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
            }
        }

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

    private static List<AnalyzedImage> AnalyzeImages(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<AnalyzedImage>();

        Parallel.ForEach(
            paths,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount,
            },
            path =>
            {
                if (TryAnalyzeImage(path, out var image))
                {
                    results.Add(image);
                }
            });

        return results
            .OrderBy(image => image.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryAnalyzeImage(string path, out AnalyzedImage image)
    {
        image = default!;

        try
        {
            var fileInfo = new FileInfo(path);
            var bytes = File.ReadAllBytes(path);
            using var memoryStream = new MemoryStream(bytes, writable: false);
            using var bitmap = new Bitmap(memoryStream);

            var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
            var perceptualHash = ComputeAverageHash(bitmap);
            var takenAt = TryReadTakenAt(bitmap, fileInfo);

            image = new AnalyzedImage(
                path,
                GetFormat(path),
                fileInfo.Length,
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

    private static ulong ComputeAverageHash(Bitmap source)
    {
        using var resized = new Bitmap(8, 8, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(source, 0, 0, 8, 8);
        }

        Span<byte> values = stackalloc byte[64];
        var index = 0;
        var total = 0;

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var pixel = resized.GetPixel(x, y);
                var grayscale = (byte)Math.Clamp((pixel.R + pixel.G + pixel.B) / 3, 0, 255);
                values[index++] = grayscale;
                total += grayscale;
            }
        }

        var average = total / 64d;
        ulong hash = 0;

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] >= average)
            {
                hash |= 1UL << i;
            }
        }

        return hash;
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
        int Width,
        int Height,
        int PixelCount,
        string Md5,
        ulong PerceptualHash,
        DateTimeOffset? TakenAt);
}
