using System.Windows;
using FotoLibraryCleaner.App.Services;
using FotoLibraryCleaner.App.ViewModels;
using FotoLibraryCleaner.Core.Services;

namespace FotoLibraryCleaner.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(
            new DuplicateScanService(),
            new FolderPickerService());
    }
}
