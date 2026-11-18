using Microsoft.UI.Xaml;

namespace NopeNote.Windows;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Title = "NopeNote";
    }
}
