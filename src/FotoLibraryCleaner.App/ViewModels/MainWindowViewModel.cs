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
    private readonly AsyncRelayCommand _scanCommand;

    private string _sourceFolder = @"D:\Photos";
    private string _duplicatesFolder = @"D:\Photos\duplicates-review";
    private int _threshold = 5;
    private bool _useFastMode = true;
    private bool _dryRun = true;
    private bool _includeSubfolders = true;
    private bool _isBusy;
    private string _statusText = "Ready to scan";
    private DuplicateGroupViewModel? _selectedGroup;

    public MainWindowViewModel(IDuplicateScanService scanService, IFolderPickerService folderPickerService)
    {
        _scanService = scanService;
        _folderPickerService = folderPickerService;

        Groups = new ObservableCollection<DuplicateGroupViewModel>();

        _browseSourceCommand = new RelayCommand(BrowseSourceFolder, () => !IsBusy);
        _browseDuplicatesCommand = new RelayCommand(BrowseDuplicatesFolder, () => !IsBusy);
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

    private bool CanStartScan() => !IsBusy && !string.IsNullOrWhiteSpace(SourceFolder);

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
        IsBusy = true;
        StatusText = "Analyzing images and preparing duplicate groups...";

        try
        {
            var options = new ScanOptions(
                SourceFolder,
                DuplicatesFolder,
                Threshold,
                UseFastMode,
                DryRun,
                IncludeSubfolders);

            var groups = await _scanService.ScanAsync(options);

            Groups.Clear();
            foreach (var group in groups.Select(model => new DuplicateGroupViewModel(model)))
            {
                Groups.Add(group);
            }

            SelectedGroup = Groups.FirstOrDefault();
            RaisePropertyChanged(nameof(GroupCount));
            StatusText = Groups.Count == 0
                ? $"Scan finished. No duplicate groups found in {SourceFolder}"
                : $"Loaded {Groups.Count} duplicate groups from {SourceFolder}";
        }
        catch (Exception ex)
        {
            Groups.Clear();
            SelectedGroup = null;
            RaisePropertyChanged(nameof(GroupCount));
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyCommandStateChanged()
    {
        _browseSourceCommand.NotifyCanExecuteChanged();
        _browseDuplicatesCommand.NotifyCanExecuteChanged();
        _scanCommand.NotifyCanExecuteChanged();
    }
}
