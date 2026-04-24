using FotoLibraryCleaner.App.Infrastructure;
using FotoLibraryCleaner.Core.Models;

namespace FotoLibraryCleaner.App.ViewModels;

public sealed class PhotoCandidateMatchViewModel : ObservableObject
{
    public PhotoCandidateMatchViewModel(PhotoDuplicateMatch model)
    {
        Model = model;
        Candidate = new PhotoCandidateViewModel(model.Candidate);
    }

    public PhotoDuplicateMatch Model { get; }

    public PhotoCandidateViewModel Candidate { get; }

    public string FileName => Candidate.FileName;

    public string SuggestedAction => Candidate.SuggestedAction;

    public string Resolution => Candidate.Resolution;

    public int Distance => Model.Distance;
}
