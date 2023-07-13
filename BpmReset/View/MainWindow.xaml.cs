using System.Windows.Controls;
using System.Windows.Input;
using BpmReset.Model;
using BpmReset.ViewModel;

namespace BpmReset.View;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    private void EventSetter_OnHandler(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem listViewItem) return;

        if (listViewItem.DataContext is not CalendarDate item) return;
        
        if (item.IsCrash)
        {
            e.Handled = true;
        }
    }
}
