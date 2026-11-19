using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Views;
using System.IO;

namespace NopeNote.Windows;

public sealed partial class MainWindow : Window
{
    private bool _isNavigating;

    public MainWindow()
    {
        InitializeComponent();

        Title = "NopeNote";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(System.AppContext.BaseDirectory, "Assets", "nopenote.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 500;
        }

        ThemeHelper.Initialize(this);
        RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;
        ApplyCaptionButtonColors();

        ContentFrame.Navigate(typeof(NotesPage));
        NotesNavItem.IsSelected = true;
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyCaptionButtonColors();
    }

    private void ApplyCaptionButtonColors()
    {
        TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);
    }

    private void AppTitleBar_BackRequested(Microsoft.UI.Xaml.Controls.TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void AppTitleBar_PaneToggleRequested(Microsoft.UI.Xaml.Controls.TitleBar sender, object args)
    {
        RootNavigation.IsPaneOpen = !RootNavigation.IsPaneOpen;
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isNavigating || args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var pageType = item.Tag?.ToString() switch
        {
            "Settings" => typeof(SettingsPage),
            _ => typeof(NotesPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        _isNavigating = true;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            SettingsNavItem.IsSelected = true;
        }
        else
        {
            NotesNavItem.IsSelected = true;
        }

        AppTitleBar.IsBackButtonVisible = ContentFrame.CanGoBack;
        _isNavigating = false;
    }
}
