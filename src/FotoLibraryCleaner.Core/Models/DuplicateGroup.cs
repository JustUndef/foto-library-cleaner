namespace FotoLibraryCleaner.Core.Models;

public sealed record DuplicateGroup(
    string GroupId,
    PhotoCandidate Primary,
    IReadOnlyList<PhotoCandidate> Matches,
    int MaxDistance,
    long EstimatedSavingsBytes);
