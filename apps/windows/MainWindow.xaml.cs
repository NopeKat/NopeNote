using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Views;
using System;
using Windows.UI.ViewManagement;

namespace NopeNote.Windows;

public sealed partial class MainWindow : Window
{
    private UISettings? _uiSettings;
    private bool _isNavigating;

    public MainWindow()
    {
        InitializeComponent();

        Title = "NopeNote";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/Tiles/NopeNoteIcon.ico");

        RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;

        try
        {
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += UISettings_ColorValuesChanged;
        }
        catch (Exception)
        {
            _uiSettings = null;
        }
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

    private void UISettings_ColorValuesChanged(UISettings sender, object args)
    {
        TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);
    }

    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ContentFrame.CurrentSourcePageType == typeof(SearchPage))
        {
            ((SearchPage)ContentFrame.Content).FocusSearch();
        }
        else
        {
            ContentFrame.Navigate(typeof(SearchPage));
        }

        SearchNavItem.IsSelected = true;
        args.Handled = true;
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
                    "Search" => typeof(SearchPage),
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
        }
        else if (e.SourcePageType == typeof(SearchPage))
        {
            SearchNavItem.IsSelected = true;
            CurrentPageTextBlock.Text = "Search";
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
            ErrorReportingTextBox.Text = IdleSynchronizer.TryWait(out string log);
            LogReportingTextBox.Text = log;
        });

        IdleStateEnteredCheckBox.IsChecked = string.IsNullOrEmpty(ErrorReportingTextBox.Text);
    }
}
