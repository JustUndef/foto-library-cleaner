namespace FotoLibraryCleaner.Core.Models;

public sealed record ScanOptions(
    string SourceFolder,
    string DuplicatesFolder,
    int Threshold,
    bool UseFastMode,
    bool DryRun,
    bool IncludeSubfolders);
