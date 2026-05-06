using System.Collections.ObjectModel;
using System.IO;
using FotoLibraryCleaner.App.Infrastructure;
using FotoLibraryCleaner.App.Services;
using FotoLibraryCleaner.Core.Models;
using FotoLibraryCleaner.Core.Services;

namespace FotoLibraryCleaner.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IDuplicateScanService _scanService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly RelayCommand _browseSourceCommand;
    private readonly RelayCommand _browseDuplicatesCommand;
    private readonly RelayCommand _cancelScanCommand;
    private readonly RelayCommand _exportReviewPlanCommand;
    private readonly AsyncRelayCommand _applyReviewActionsCommand;
    private readonly AsyncRelayCommand _scanCommand;

    private CancellationTokenSource? _scanCancellationTokenSource;
    private string _sourceFolder = @"D:\Photos";
    private string _duplicatesFolder = @"D:\Photos\duplicates-review";
    private int _threshold = 5;
    private PhotoHashAlgorithm _hashAlgorithm = PhotoHashAlgorithm.PerceptualHash;
    private bool _useFastMode = true;
    private bool _dryRun = true;
    private bool _includeSubfolders = true;
    private bool _isBusy;
    private string _statusText = "Ready to scan";
    private string _scanPhase = "Idle";
    private int _filesDiscovered;
    private int _imagesAnalyzed;
    private int _imagesSkipped;
    private int _groupsFoundLive;
    private double _progressValue;
    private DuplicateGroupViewModel? _selectedGroup;

    public MainWindowViewModel(IDuplicateScanService scanService, IFolderPickerService folderPickerService)
    {
        _scanService = scanService;
        _folderPickerService = folderPickerService;

        Groups = new ObservableCollection<DuplicateGroupViewModel>();

        _browseSourceCommand = new RelayCommand(BrowseSourceFolder, () => !IsBusy);
        _browseDuplicatesCommand = new RelayCommand(BrowseDuplicatesFolder, () => !IsBusy);
        _cancelScanCommand = new RelayCommand(CancelScan, CanCancelScan);
        _exportReviewPlanCommand = new RelayCommand(ExportReviewPlan, CanExportReviewPlan);
        _applyReviewActionsCommand = new AsyncRelayCommand(ApplyReviewActionsAsync, CanApplyReviewActions);
        _scanCommand = new AsyncRelayCommand(StartScanAsync, CanStartScan);
    }

    public string Title => "Foto Library Cleaner";

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; }

    public IReadOnlyList<PhotoHashAlgorithm> HashAlgorithms { get; } =
    [
        PhotoHashAlgorithm.PerceptualHash,
        PhotoHashAlgorithm.DifferenceHash,
    ];

    public string SourceFolder
    {
        get => _sourceFolder;
        set
        {
            if (SetProperty(ref _sourceFolder, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public string DuplicatesFolder
    {
        get => _duplicatesFolder;
        set => SetProperty(ref _duplicatesFolder, value);
    }

    public int Threshold
    {
        get => _threshold;
        set => SetProperty(ref _threshold, Math.Clamp(value, 0, 20));
    }

    public PhotoHashAlgorithm HashAlgorithm
    {
        get => _hashAlgorithm;
        set => SetProperty(ref _hashAlgorithm, value);
    }

    public bool UseFastMode
    {
        get => _useFastMode;
        set => SetProperty(ref _useFastMode, value);
    }

    public bool DryRun
    {
        get => _dryRun;
        set => SetProperty(ref _dryRun, value);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(BusyLabel));
                NotifyCommandStateChanged();
            }
        }
    }

    public string BusyLabel => IsBusy ? "Scanning..." : "Idle";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ScanPhase
    {
        get => _scanPhase;
        private set => SetProperty(ref _scanPhase, value);
    }

    public int FilesDiscovered
    {
        get => _filesDiscovered;
        private set => SetProperty(ref _filesDiscovered, value);
    }

    public int ImagesAnalyzed
    {
        get => _imagesAnalyzed;
        private set => SetProperty(ref _imagesAnalyzed, value);
    }

    public int ImagesSkipped
    {
        get => _imagesSkipped;
        private set => SetProperty(ref _imagesSkipped, value);
    }

    public int GroupsFoundLive
    {
        get => _groupsFoundLive;
        private set => SetProperty(ref _groupsFoundLive, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public DuplicateGroupViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public int GroupCount => Groups.Count;

    public string EmptyStateText => "No scan results yet. Choose a folder and start the first analysis.";

    public RelayCommand BrowseSourceCommand => _browseSourceCommand;

    public RelayCommand BrowseDuplicatesCommand => _browseDuplicatesCommand;

    public AsyncRelayCommand ScanCommand => _scanCommand;

    public RelayCommand CancelScanCommand => _cancelScanCommand;

    public RelayCommand ExportReviewPlanCommand => _exportReviewPlanCommand;

    public AsyncRelayCommand ApplyReviewActionsCommand => _applyReviewActionsCommand;

    private bool CanStartScan() => !IsBusy && !string.IsNullOrWhiteSpace(SourceFolder);

    private bool CanCancelScan() => IsBusy && _scanCancellationTokenSource is not null;

    private bool CanExportReviewPlan() => !IsBusy && Groups.Count > 0 && !string.IsNullOrWhiteSpace(DuplicatesFolder);

    private bool CanApplyReviewActions() => !IsBusy && Groups.Count > 0 && !string.IsNullOrWhiteSpace(DuplicatesFolder);

    private void BrowseSourceFolder()
    {
        var selected = _folderPickerService.PickFolder(SourceFolder);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SourceFolder = selected;

            if (string.IsNullOrWhiteSpace(DuplicatesFolder))
            {
                DuplicatesFolder = System.IO.Path.Combine(selected, "duplicates-review");
            }
        }
    }

    private void BrowseDuplicatesFolder()
    {
        var selected = _folderPickerService.PickFolder(DuplicatesFolder);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            DuplicatesFolder = selected;
        }
    }

    private async Task StartScanAsync()
    {
        _scanCancellationTokenSource = new CancellationTokenSource();
        IsBusy = true;
        ScanPhase = "Starting";
        StatusText = "Analyzing images and preparing duplicate groups...";
        FilesDiscovered = 0;
        ImagesAnalyzed = 0;
        ImagesSkipped = 0;
        GroupsFoundLive = 0;
        ProgressValue = 0;

        try
        {
            var options = new ScanOptions(
                SourceFolder,
                DuplicatesFolder,
                Threshold,
                HashAlgorithm,
                UseFastMode,
                DryRun,
                IncludeSubfolders);

            var progress = new Progress<ScanProgress>(update =>
            {
                ScanPhase = update.Phase;
                FilesDiscovered = update.FilesDiscovered;
                ImagesAnalyzed = update.ImagesAnalyzed;
                ImagesSkipped = update.ImagesSkipped;
                GroupsFoundLive = update.GroupsFound;
                StatusText = update.Message;
                ProgressValue = update.Total <= 0
                    ? 0
                    : Math.Clamp((double)update.Current / update.Total * 100d, 0d, 100d);
            });

            var groups = await _scanService.ScanAsync(options, progress, _scanCancellationTokenSource.Token);

            Groups.Clear();
            foreach (var group in groups.Select(model => new DuplicateGroupViewModel(model)))
            {
                Groups.Add(group);
            }

            SelectedGroup = Groups.FirstOrDefault();
            RaisePropertyChanged(nameof(GroupCount));
            GroupsFoundLive = Groups.Count;
            ProgressValue = 100;
            StatusText = Groups.Count == 0
                ? $"Scan finished. No duplicate groups found in {SourceFolder}"
                : $"Loaded {Groups.Count} duplicate groups from {SourceFolder}";
        }
        catch (OperationCanceledException)
        {
            ScanPhase = "Canceled";
            StatusText = $"Scan canceled after analyzing {ImagesAnalyzed} images.";
        }
        catch (Exception ex)
        {
            Groups.Clear();
            SelectedGroup = null;
            RaisePropertyChanged(nameof(GroupCount));
            ScanPhase = "Failed";
            StatusText = ex.Message;
        }
        finally
        {
            _scanCancellationTokenSource?.Dispose();
            _scanCancellationTokenSource = null;

            if (ScanPhase != "Failed" && ScanPhase != "Canceled")
            {
                ScanPhase = "Idle";
            }

            IsBusy = false;
        }
    }

    private void CancelScan()
    {
        _scanCancellationTokenSource?.Cancel();
        StatusText = "Cancel requested. Finishing current work item...";
        NotifyCommandStateChanged();
    }

    private void ExportReviewPlan()
    {
        Directory.CreateDirectory(DuplicatesFolder);

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var outputPath = Path.Combine(DuplicatesFolder, $"review-plan-{timestamp}.csv");

        using var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8);
        writer.WriteLine("GroupId,Role,Action,Distance,Path,Reason");

        foreach (var group in Groups)
        {
            WriteReviewRow(writer, group.GroupId, "Primary", ReviewAction.Keep, 0, group.Primary.Path, group.Primary.Reason);

            foreach (var match in group.Matches)
            {
                WriteReviewRow(writer, group.GroupId, "Match", match.SelectedAction, match.Distance, match.Candidate.Path, match.Reason);
            }
        }

        StatusText = $"Review plan exported to {outputPath}";
    }

    private async Task ApplyReviewActionsAsync()
    {
        var actionRequests = Groups
            .SelectMany(group => group.Matches
                .Where(match => match.SelectedAction is ReviewAction.Move or ReviewAction.Delete)
                .Select(match => new PendingReviewAction(group, match)))
            .ToList();

        if (actionRequests.Count == 0)
        {
            StatusText = "No review actions to apply. Mark duplicates as Move or Delete first.";
            return;
        }

        if (DryRun)
        {
            var moveCount = actionRequests.Count(request => request.Match.SelectedAction == ReviewAction.Move);
            var deleteCount = actionRequests.Count(request => request.Match.SelectedAction == ReviewAction.Delete);
            var estimatedBytes = actionRequests.Sum(request => request.Match.Candidate.Model.FileSizeBytes);
            StatusText = $"Dry run: would move {moveCount}, delete {deleteCount}, and free about {estimatedBytes / 1024d / 1024d:F2} MB.";
            return;
        }

        try
        {
            IsBusy = true;
            ScanPhase = "Applying";
            StatusText = $"Applying {actionRequests.Count} reviewed actions...";

            var result = await Task.Run(() => ApplyReviewedFiles(actionRequests));

            foreach (var request in result.AppliedRequests)
            {
                request.Group.Matches.Remove(request.Match);
            }

            foreach (var emptyGroup in Groups.Where(group => group.Matches.Count == 0).ToList())
            {
                Groups.Remove(emptyGroup);
            }

            SelectedGroup = Groups.FirstOrDefault();
            RaisePropertyChanged(nameof(GroupCount));
            GroupsFoundLive = Groups.Count;
            StatusText = result.FailedCount == 0
                ? $"Applied review actions. Moved {result.MovedCount}, deleted {result.DeletedCount}."
                : $"Applied review actions with issues. Moved {result.MovedCount}, deleted {result.DeletedCount}, skipped {result.SkippedCount}, failed {result.FailedCount}.";
        }
        catch (Exception ex)
        {
            ScanPhase = "Failed";
            StatusText = $"Failed to apply review actions: {ex.Message}";
        }
        finally
        {
            if (ScanPhase != "Failed")
            {
                ScanPhase = "Idle";
            }

            IsBusy = false;
        }
    }

    private ReviewExecutionResult ApplyReviewedFiles(IReadOnlyList<PendingReviewAction> actionRequests)
    {
        Directory.CreateDirectory(DuplicatesFolder);

        var appliedRequests = new List<PendingReviewAction>();
        var movedCount = 0;
        var deletedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;

        foreach (var request in actionRequests)
        {
            var sourcePath = request.Match.Candidate.Path;

            try
            {
                if (!File.Exists(sourcePath))
                {
                    failedCount++;
                    continue;
                }

                if (request.Match.SelectedAction == ReviewAction.Move)
                {
                    var destinationPath = GetUniqueDestinationPath(sourcePath, DuplicatesFolder);
                    if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                    {
                        skippedCount++;
                        continue;
                    }

                    File.Move(sourcePath, destinationPath);
                    movedCount++;
                    appliedRequests.Add(request);
                }
                else if (request.Match.SelectedAction == ReviewAction.Delete)
                {
                    File.Delete(sourcePath);
                    deletedCount++;
                    appliedRequests.Add(request);
                }
            }
            catch
            {
                failedCount++;
            }
        }

        return new ReviewExecutionResult(appliedRequests, movedCount, deletedCount, skippedCount, failedCount);
    }

    private static string GetUniqueDestinationPath(string sourcePath, string duplicatesFolder)
    {
        var fileName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(duplicatesFolder, fileName);

        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var index = 1;

        do
        {
            destinationPath = Path.Combine(duplicatesFolder, $"{name}-{timestamp}-{index}{extension}");
            index++;
        }
        while (File.Exists(destinationPath));

        return destinationPath;
    }

    private static void WriteReviewRow(
        TextWriter writer,
        string groupId,
        string role,
        ReviewAction action,
        int distance,
        string path,
        string reason)
    {
        writer.WriteLine(string.Join(",",
            EscapeCsv(groupId),
            EscapeCsv(role),
            EscapeCsv(action.ToString()),
            EscapeCsv(distance.ToString()),
            EscapeCsv(path),
            EscapeCsv(reason)));
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"");
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value}\""
            : value;
    }

    private void NotifyCommandStateChanged()
    {
        _browseSourceCommand.NotifyCanExecuteChanged();
        _browseDuplicatesCommand.NotifyCanExecuteChanged();
        _cancelScanCommand.NotifyCanExecuteChanged();
        _exportReviewPlanCommand.NotifyCanExecuteChanged();
        _applyReviewActionsCommand.NotifyCanExecuteChanged();
        _scanCommand.NotifyCanExecuteChanged();
    }

    private sealed record PendingReviewAction(DuplicateGroupViewModel Group, PhotoCandidateMatchViewModel Match);

    private sealed record ReviewExecutionResult(
        IReadOnlyList<PendingReviewAction> AppliedRequests,
        int MovedCount,
        int DeletedCount,
        int SkippedCount,
        int FailedCount);
}
