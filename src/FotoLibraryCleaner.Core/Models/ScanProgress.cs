namespace FotoLibraryCleaner.Core.Models;

public sealed record ScanProgress(
    string Phase,
    int FilesDiscovered,
    int ImagesAnalyzed,
    int ImagesSkipped,
    int GroupsFound,
    int Current,
    int Total,
    string Message);
