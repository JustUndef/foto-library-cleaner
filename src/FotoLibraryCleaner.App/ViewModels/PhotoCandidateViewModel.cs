using FotoLibraryCleaner.App.Infrastructure;
using FotoLibraryCleaner.Core.Models;

namespace FotoLibraryCleaner.App.ViewModels;

public sealed class PhotoCandidateViewModel : ObservableObject
{
    public PhotoCandidateViewModel(PhotoCandidate model)
    {
        Model = model;
    }

    public PhotoCandidate Model { get; }

    public string FileName => System.IO.Path.GetFileName(Model.Path);

    public string Path => Model.Path;

    public string Format => Model.Format;

    public string Resolution => $"{Model.Width} x {Model.Height}";

    public string FileSize => $"{Model.FileSizeBytes / 1024d / 1024d:F2} MB";

    public string TakenAt => Model.TakenAt?.ToString("dd.MM.yyyy HH:mm") ?? "Unknown";

    public string Hash => Model.Hash;

    public string SuggestedAction => Model.SuggestedAction.ToString();

    public string Reason => Model.Reason;

    public int QualityScore => Model.QualityScore;
}
