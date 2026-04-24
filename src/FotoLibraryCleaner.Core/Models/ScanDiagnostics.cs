namespace FotoLibraryCleaner.Core.Models;

public sealed record ScanDiagnostics(
    int FilesDiscovered,
    int ImagesAnalyzed,
    int ImagesSkipped,
    int ExactDuplicateGroups,
    int SimilarDuplicateGroups);
