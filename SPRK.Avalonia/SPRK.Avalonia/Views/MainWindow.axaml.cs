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

                // Prevent Enter from activating focused buttons (matches WIN_MAIN behavior)
                if (e.Key == Key.Enter && vm.IsConnected)
                {
                    e.Handled = true;
                }
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