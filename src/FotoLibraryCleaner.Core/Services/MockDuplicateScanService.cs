using FotoLibraryCleaner.Core.Models;

namespace FotoLibraryCleaner.Core.Services;

public sealed class MockDuplicateScanService : IDuplicateScanService
{
    public async Task<IReadOnlyList<DuplicateGroup>> ScanAsync(ScanOptions options, CancellationToken cancellationToken = default)
    {
        await Task.Delay(900, cancellationToken);

        var root = string.IsNullOrWhiteSpace(options.SourceFolder)
            ? @"C:\Photos"
            : options.SourceFolder.TrimEnd('\\');

        var firstGroup = new DuplicateGroup(
            GroupId: "G-001",
            Primary: new PhotoCandidate(
                Path: Path.Combine(root, "2024", "Trip", "IMG_1201.JPG"),
                Format: "JPEG",
                FileSizeBytes: 4_281_512,
                Width: 4032,
                Height: 3024,
                Hash: "f1ab-0021-cc90",
                QualityScore: 98,
                TakenAt: new DateTimeOffset(2024, 6, 15, 10, 22, 0, TimeSpan.FromHours(2)),
                SuggestedAction: ReviewAction.Keep,
                Reason: "Highest resolution and largest file size in group."),
            Matches:
            [
                new PhotoCandidate(
                    Path: Path.Combine(root, "WhatsApp", "IMG-20240615-WA0007.jpg"),
                    Format: "JPEG",
                    FileSizeBytes: 612_440,
                    Width: 1600,
                    Height: 1200,
                    Hash: "f1ab-0024-cc80",
                    QualityScore: 54,
                    TakenAt: new DateTimeOffset(2024, 6, 15, 10, 22, 0, TimeSpan.FromHours(2)),
                    SuggestedAction: ReviewAction.Move,
                    Reason: "Compressed share copy."),
                new PhotoCandidate(
                    Path: Path.Combine(root, "Exports", "Trip", "IMG_1201-edit.jpg"),
                    Format: "JPEG",
                    FileSizeBytes: 2_018_932,
                    Width: 3024,
                    Height: 2268,
                    Hash: "f1ab-0027-cc82",
                    QualityScore: 73,
                    TakenAt: new DateTimeOffset(2024, 6, 16, 8, 10, 0, TimeSpan.FromHours(2)),
                    SuggestedAction: ReviewAction.Skip,
                    Reason: "Likely manual edit. Review before moving."),
            ],
            MaxDistance: 4,
            EstimatedSavingsBytes: 2_631_372);

        var secondGroup = new DuplicateGroup(
            GroupId: "G-002",
            Primary: new PhotoCandidate(
                Path: Path.Combine(root, "Family", "2022", "DSC04519.JPG"),
                Format: "JPEG",
                FileSizeBytes: 6_902_116,
                Width: 6000,
                Height: 4000,
                Hash: "00af-8110-98cc",
                QualityScore: 100,
                TakenAt: new DateTimeOffset(2022, 12, 24, 19, 3, 0, TimeSpan.FromHours(1)),
                SuggestedAction: ReviewAction.Keep,
                Reason: "Original camera file."),
            Matches:
            [
                new PhotoCandidate(
                    Path: Path.Combine(root, "Backups", "OldLaptop", "DSC04519.JPG"),
                    Format: "JPEG",
                    FileSizeBytes: 6_901_887,
                    Width: 6000,
                    Height: 4000,
                    Hash: "00af-8110-98cc",
                    QualityScore: 99,
                    TakenAt: new DateTimeOffset(2022, 12, 24, 19, 3, 0, TimeSpan.FromHours(1)),
                    SuggestedAction: ReviewAction.Move,
                    Reason: "Exact duplicate in backup folder."),
            ],
            MaxDistance: 0,
            EstimatedSavingsBytes: 6_901_887);

        var thirdGroup = new DuplicateGroup(
            GroupId: "G-003",
            Primary: new PhotoCandidate(
                Path: Path.Combine(root, "Scans", "album-page-14.png"),
                Format: "PNG",
                FileSizeBytes: 8_412_501,
                Width: 4961,
                Height: 3508,
                Hash: "9912-ac45-0ff2",
                QualityScore: 91,
                TakenAt: null,
                SuggestedAction: ReviewAction.Keep,
                Reason: "Lossless scan."),
            Matches:
            [
                new PhotoCandidate(
                    Path: Path.Combine(root, "Scans", "album-page-14-copy.png"),
                    Format: "PNG",
                    FileSizeBytes: 8_412_501,
                    Width: 4961,
                    Height: 3508,
                    Hash: "9912-ac45-0ff2",
                    QualityScore: 91,
                    TakenAt: null,
                    SuggestedAction: ReviewAction.Move,
                    Reason: "Exact duplicate with alternate filename."),
                new PhotoCandidate(
                    Path: Path.Combine(root, "Scans", "album-page-14-whatsapp.jpg"),
                    Format: "JPEG",
                    FileSizeBytes: 422_204,
                    Width: 1600,
                    Height: 1131,
                    Hash: "9912-ac48-0ef0",
                    QualityScore: 42,
                    TakenAt: null,
                    SuggestedAction: ReviewAction.Move,
                    Reason: "Shared low-quality conversion."),
            ],
            MaxDistance: 3,
            EstimatedSavingsBytes: 8_834_705);

        return [firstGroup, secondGroup, thirdGroup];
    }
}
