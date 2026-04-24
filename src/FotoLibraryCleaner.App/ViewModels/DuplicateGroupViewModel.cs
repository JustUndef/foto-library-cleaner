using System.Collections.ObjectModel;
using FotoLibraryCleaner.App.Infrastructure;
using FotoLibraryCleaner.Core.Models;

namespace FotoLibraryCleaner.App.ViewModels;

public sealed class DuplicateGroupViewModel : ObservableObject
{
    private PhotoCandidateViewModel? _selectedMatch;

    public DuplicateGroupViewModel(DuplicateGroup model)
    {
        Model = model;
        Primary = new PhotoCandidateViewModel(model.Primary);
        Matches = new ObservableCollection<PhotoCandidateViewModel>(model.Matches.Select(match => new PhotoCandidateViewModel(match)));
        _selectedMatch = Matches.FirstOrDefault();
    }

    public DuplicateGroup Model { get; }

    public string GroupId => Model.GroupId;

    public string Header => $"{Model.GroupId}  |  {Matches.Count + 1} files";

    public string Summary => $"Max distance {Model.MaxDistance}  |  Savings {Model.EstimatedSavingsBytes / 1024d / 1024d:F2} MB";

    public PhotoCandidateViewModel Primary { get; }

    public ObservableCollection<PhotoCandidateViewModel> Matches { get; }

    public PhotoCandidateViewModel? SelectedMatch
    {
        get => _selectedMatch;
        set
        {
            if (SetProperty(ref _selectedMatch, value))
            {
                RaisePropertyChanged(nameof(ComparisonTarget));
            }
        }
    }

    public PhotoCandidateViewModel? ComparisonTarget => SelectedMatch ?? Matches.FirstOrDefault();
}
