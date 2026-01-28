using Avalonia.Controls;
using System.Collections.Specialized;
using SPRK.Avalonia.ViewModels;

namespace SPRK.Avalonia.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        // Auto-scroll console when messages are added
        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ConsoleMessages.CollectionChanged += OnConsoleMessagesChanged;
            }
        };
    }

    private void OnConsoleMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Scroll to bottom when new messages are added
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            ConsoleScroller.ScrollToEnd();
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ConsoleScroller.ScrollToHome();
        }
    }
}