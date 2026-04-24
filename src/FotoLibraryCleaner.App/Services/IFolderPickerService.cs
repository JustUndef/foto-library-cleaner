namespace FotoLibraryCleaner.App.Services;

public interface IFolderPickerService
{
    string? PickFolder(string? initialDirectory = null);
}
