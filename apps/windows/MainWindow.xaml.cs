using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Views;
using System;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace NopeNote.Windows;

public sealed partial class MainWindow : Window
{
    private UISettings? _uiSettings;
    private bool _isNavigating;
    private object? _navItemBeforePress;
    private bool _paneOpenBeforePress;

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

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    internal void InitializeNavigation()
    {
        ContentFrame.Navigate(typeof(NotesPage));
        NotesNavItem.IsSelected = true;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        // Remove the classic window menu on title-bar right click and the
        // shell tooltips on the three caption buttons. The title bar is
        // registered above, so this subclass sees the non-client messages
        // before the title bar's own handler.
        CaptionChromeHelper.SuppressCaptionChrome(WindowHelper.GetWindowHandle(this));

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

    private void NavigationItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 点击处理会先改变选中项，所以在按下时把点击前的选中项和侧栏状态记下来。
        _navItemBeforePress = RootNavigation.SelectedItem;
        _paneOpenBeforePress = RootNavigation.IsPaneOpen;
    }

    private void NavigationItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // 只有再次点击当前选中项才切换侧栏；点别的项只切页面，不动侧栏。
        if (!ReferenceEquals(_navItemBeforePress, sender))
        {
            return;
        }

        if (_paneOpenBeforePress)
        {
            RootNavigation.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
            RootNavigation.IsPaneOpen = false;
        }
        else
        {
            RootNavigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
            RootNavigation.IsPaneOpen = true;
        }
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isNavigating)
        {
            return;
        }

        // 设置入口现在是普通页脚导航项，和其他导航项共用同一套选中逻辑
        Type? pageType = args.SelectedItem is NavigationViewItem item
            ? item.Tag?.ToString() switch
            {
                "Search" => typeof(SearchPage),
                "Trash" => typeof(TrashPage),
                "Settings" => typeof(SettingsPage),
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
            SettingsNavItem.IsSelected = true;
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

        // 探测放到后台，结果回到 UI 线程再写入辅助控件。
        string error = string.Empty;
        string log = string.Empty;
        await Task.Run(() =>
        {
            error = IdleSynchronizer.TryWait(out log);
        });

        ErrorReportingTextBox.Text = error;
        LogReportingTextBox.Text = log;
        IdleStateEnteredCheckBox.IsChecked = string.IsNullOrEmpty(error);
    }
}
