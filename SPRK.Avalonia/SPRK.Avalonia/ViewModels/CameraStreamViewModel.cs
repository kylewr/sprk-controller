using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;

namespace SPRK.Avalonia.ViewModels;

public partial class CameraStreamViewModel : ViewModelBase
{
    [ObservableProperty]
    private Bitmap? _currentFrame;

    [ObservableProperty]
    private bool _isLoaded = false;
}