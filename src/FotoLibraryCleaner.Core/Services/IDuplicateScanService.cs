using FotoLibraryCleaner.Core.Models;

namespace FotoLibraryCleaner.Core.Services;

public interface IDuplicateScanService
{
    Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
