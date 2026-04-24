using FotoLibraryCleaner.App.Infrastructure;
using FotoLibraryCleaner.Core.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FotoLibraryCleaner.App.ViewModels;

public sealed class PhotoCandidateViewModel : ObservableObject
{
    public PhotoCandidateViewModel(PhotoCandidate model)
    {
        Model = model;
        PreviewImage = TryLoadPreviewImage(model.Path);
    }

    public PhotoCandidate Model { get; }

    public ImageSource? PreviewImage { get; }

    public bool HasPreview => PreviewImage is not null;

    public string FileName => System.IO.Path.GetFileName(Model.Path);

    public string Path => Model.Path;

    public string Format => Model.Format;

    public string Resolution => $"{Model.Width} x {Model.Height}";

    public string PixelCount => $"{Model.PixelCount:N0} px";

    public string FileSize => $"{Model.FileSizeBytes / 1024d / 1024d:F2} MB";

    public string TakenAt => Model.TakenAt?.ToString("dd.MM.yyyy HH:mm") ?? "Unknown";

    public string Hash => Model.Hash;

    public string Md5 => Model.Md5;

    public string SuggestedAction => Model.SuggestedAction.ToString();

    public string Reason => Model.Reason;

    public int QualityScore => Model.QualityScore;

    private static ImageSource? TryLoadPreviewImage(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.DelayCreation;
            image.DecodePixelWidth = 720;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
