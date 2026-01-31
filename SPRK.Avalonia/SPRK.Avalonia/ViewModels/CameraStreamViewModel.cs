using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace SPRK.Avalonia.ViewModels;

public partial class CameraStreamViewModel : ViewModelBase
{
    [ObservableProperty]
    private Bitmap? _currentFrame;

    [ObservableProperty]
    private bool _isLoaded = false;

    public string StatusText => IsLoaded ? "Connected" : "Connecting...";

    public string FrameInfo => CurrentFrame is not null 
        ? $"{CurrentFrame.PixelSize.Width} × {CurrentFrame.PixelSize.Height}" 
        : "";

    partial void OnIsLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnCurrentFrameChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(FrameInfo));
    }
}

public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Colors.LimeGreen : Colors.Orange;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}