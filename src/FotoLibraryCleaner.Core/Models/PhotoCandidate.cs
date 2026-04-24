namespace FotoLibraryCleaner.Core.Models;

public sealed record PhotoCandidate(
    string Path,
    string Format,
    long FileSizeBytes,
    int Width,
    int Height,
    string Hash,
    int QualityScore,
    DateTimeOffset? TakenAt,
    ReviewAction SuggestedAction,
    string Reason);
