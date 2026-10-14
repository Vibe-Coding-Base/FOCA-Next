using Avalonia.Controls;
using Foca.App.ViewModels;

namespace Foca.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.Log.EntryAdded += (_, entry) =>
                {
                    if (vm.Log.AutoScroll)
                        LogGrid.ScrollIntoView(entry, null);
                };
        };
    }
}
