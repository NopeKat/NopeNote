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
        BackdropHelper.Apply(this);
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

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string query = args.QueryText ?? string.Empty;
        NoteSearchState.SetQuery(query);

        if (ContentFrame.CurrentSourcePageType != typeof(NotesPage))
        {
            ContentFrame.Navigate(typeof(NotesPage));
        }

        NotesNavItem.IsSelected = true;
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isNavigating)
        {
            return;
        }

        // 设置入口沿用 NavigationView 内置页脚项，选中状态由 IsSettingsSelected 表达
        Type? pageType = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : args.SelectedItem is NavigationViewItem item
                ? item.Tag?.ToString() switch
                {
                    "Trash" => typeof(TrashPage),
                    _ => typeof(NotesPage),
                }
                : null;

        if (pageType is null || ContentFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType);
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        _isNavigating = true;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            RootNavigation.SelectedItem = RootNavigation.SettingsItem;
            CurrentPageTextBlock.Text = "Settings";
        }
        else if (e.SourcePageType == typeof(TrashPage))
        {
            TrashNavItem.IsSelected = true;
            CurrentPageTextBlock.Text = "Trash";
            SearchBox.Text = NoteSearchState.Query;
        }
        else
        {
            NotesNavItem.IsSelected = true;
            CurrentPageTextBlock.Text = "Notes";
            SearchBox.Text = NoteSearchState.Query;
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
            ErrorReportingTextBox.Text = IdleSynchronizer.TryWait(out string log);
            LogReportingTextBox.Text = log;
        });

        IdleStateEnteredCheckBox.IsChecked = string.IsNullOrEmpty(ErrorReportingTextBox.Text);
    }
}
