using FotoLibraryCleaner.App.Infrastructure;
using FotoLibraryCleaner.Core.Models;

namespace FotoLibraryCleaner.App.ViewModels;

public sealed class PhotoCandidateMatchViewModel : ObservableObject
{
    private ReviewAction _selectedAction;

    public PhotoCandidateMatchViewModel(PhotoDuplicateMatch model)
    {
        Model = model;
        Candidate = new PhotoCandidateViewModel(model.Candidate);
        _selectedAction = model.Candidate.SuggestedAction;
    }

    public PhotoDuplicateMatch Model { get; }

    public PhotoCandidateViewModel Candidate { get; }

    public string FileName => Candidate.FileName;

    public Array AvailableActions => Enum.GetValues(typeof(ReviewAction));

    public ReviewAction SelectedAction
    {
        get => _selectedAction;
        set => SetProperty(ref _selectedAction, value);
    }

    public string Resolution => Candidate.Resolution;

    public string Reason => Candidate.Reason;

    public int Distance => Model.Distance;
}
