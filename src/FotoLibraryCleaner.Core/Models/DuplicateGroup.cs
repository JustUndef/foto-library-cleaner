namespace FotoLibraryCleaner.Core.Models;

public sealed record DuplicateGroup(
    string GroupId,
    PhotoCandidate Primary,
    IReadOnlyList<PhotoDuplicateMatch> Matches,
    int MaxDistance,
    long EstimatedSavingsBytes,
    bool IsExactMatch);
