using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SPRK.Avalonia.ViewModels;

namespace SPRK.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Use tunneling (Preview) events to catch key presses before focused controls handle them
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        }

        private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.HandleKeyDown(e.Key);
            }
        }

        private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.HandleKeyUp(e.Key);
            }
        }
    }
}