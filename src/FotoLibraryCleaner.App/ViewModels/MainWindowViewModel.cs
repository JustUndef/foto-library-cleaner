using System.Collections.ObjectModel;
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
    private readonly AsyncRelayCommand _scanCommand;

    private CancellationTokenSource? _scanCancellationTokenSource;
    private string _sourceFolder = @"D:\Photos";
    private string _duplicatesFolder = @"D:\Photos\duplicates-review";
    private int _threshold = 5;
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
        _scanCommand = new AsyncRelayCommand(StartScanAsync, CanStartScan);
    }

    public string Title => "Foto Library Cleaner";

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; }

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

    private bool CanStartScan() => !IsBusy && !string.IsNullOrWhiteSpace(SourceFolder);

    private bool CanCancelScan() => IsBusy && _scanCancellationTokenSource is not null;

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

    private void NotifyCommandStateChanged()
    {
        _browseSourceCommand.NotifyCanExecuteChanged();
        _browseDuplicatesCommand.NotifyCanExecuteChanged();
        _cancelScanCommand.NotifyCanExecuteChanged();
        _scanCommand.NotifyCanExecuteChanged();
    }
}
