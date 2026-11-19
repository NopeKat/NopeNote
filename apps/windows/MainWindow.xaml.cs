using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Views;
using System;

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
        AppWindow.SetIcon("Assets/Tiles/NopeNoteIcon.ico");
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;
    }

    internal void InitializeNavigation()
    {
        ContentFrame.Navigate(typeof(NotesPage));
        NotesNavItem.IsSelected = true;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        WindowHelper.SetWindowMinSize(this, 640, 500);
        TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
    {
        TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);
    }

    private void OnPaneDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        AppTitleBar.IsPaneToggleButtonVisible = sender.PaneDisplayMode != NavigationViewPaneDisplayMode.Top;
    }

    private void OnNavigationViewControlLoaded(object sender, RoutedEventArgs e)
    {
        AppTitleBar.IsPaneToggleButtonVisible = RootNavigation.PaneDisplayMode != NavigationViewPaneDisplayMode.Top;
    }

    private void AppTitleBar_BackRequested(Microsoft.UI.Xaml.Controls.TitleBar sender, object args)
    {
        GoBack();
    }

    private void AppTitleBar_PaneToggleRequested(Microsoft.UI.Xaml.Controls.TitleBar sender, object args)
    {
        RootNavigation.IsPaneOpen = !RootNavigation.IsPaneOpen;
    }

    private void SearchBox_AcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
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
            CurrentPageTextBlock.Text = "Settings";
        }
        else
        {
            NotesNavItem.IsSelected = true;
            CurrentPageTextBlock.Text = "Notes";
        }

        _isNavigating = false;
    }

    private void GoBack()
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void GoBackInvokerButton_Click(object sender, RoutedEventArgs e)
    {
        GoBack();
    }

    private void CloseAppInvokerButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }

    private async void WaitForIdleInvokerButton_Click(object sender, RoutedEventArgs e)
    {
        IdleStateEnteredCheckBox.IsChecked = false;

        await global::Windows.System.Threading.ThreadPool.RunAsync(_ =>
        {
            ErrorReportingTextBox.Text = IdleSynchronizer.TryWait(out var log);
            LogReportingTextBox.Text = log;
        });

        IdleStateEnteredCheckBox.IsChecked = string.IsNullOrEmpty(ErrorReportingTextBox.Text);
    }
}
