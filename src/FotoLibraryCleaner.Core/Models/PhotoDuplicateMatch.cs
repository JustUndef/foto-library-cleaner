namespace FotoLibraryCleaner.Core.Models;

public sealed record PhotoDuplicateMatch(
    PhotoCandidate Candidate,
    int Distance);
