using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Services;
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
    private bool _closeConfirmed;
    private bool _closeFlowRunning;
    private QuickCaptureWindow? _quickCaptureWindow;

    public MainWindow()
    {
        InitializeComponent();

        Title = "NopeNote";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/Tiles/NopeNoteIcon.ico");
        AppWindow.Closing += AppWindow_Closing;

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
        GlobalHotkey.RegisterQuickCapture(WindowHelper.GetWindowHandle(this), ShowQuickCapture);
    }

    internal async Task<bool> EnsureUnlockedAsync()
    {
        if (!SettingsHelper.Current.HasAppLock)
        {
            return true;
        }

        string? error = null;
        while (true)
        {
            XamlRoot? root = RootGrid.XamlRoot;
            if (root is null)
            {
                await Task.Delay(100);
                continue;
            }

            PasswordBox input = new() { PlaceholderText = Strings.Get("AppLockPasswordPlaceholder") };
            StackPanel panel = new() { Spacing = 8 };
            panel.Children.Add(input);
            if (error is not null)
            {
                panel.Children.Add(new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap });
            }

            ContentDialog dialog = new()
            {
                XamlRoot = root,
                Title = Strings.Get("AppLockTitle"),
                Content = panel,
                PrimaryButtonText = Strings.Get("AppLockUnlock"),
                CloseButtonText = Strings.Get("AppLockExit"),
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return false;
            }

            if (PasswordHasher.Verify(
                    input.Password,
                    SettingsHelper.Current.AppLockSalt,
                    SettingsHelper.Current.AppLockHash))
            {
                return true;
            }

            error = Strings.Get("AppLockWrongPassword");
            await Task.Delay(50);
        }
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

    private void ShowQuickCapture()
    {
        if (_quickCaptureWindow is null)
        {
            _quickCaptureWindow = new QuickCaptureWindow();
            WindowHelper.TrackWindow(_quickCaptureWindow);
            _quickCaptureWindow.Closed += (_, _) => _quickCaptureWindow = null;
        }

        _quickCaptureWindow.Activate();
        _quickCaptureWindow.FocusInput();
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
        // 走窗口关闭流程，确保未保存的内容和待提交的 git 变更都得到处理。
        Close();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed)
        {
            return;
        }

        if (_closeFlowRunning)
        {
            args.Cancel = true;
            return;
        }

        bool hasWork = VaultService.Current.HasPendingCommits ||
            (ContentFrame.Content is NotesPage notes && notes.HasUnsavedWork());

        if (!hasWork)
        {
            return;
        }

        args.Cancel = true;
        _closeFlowRunning = true;

        bool canClose = false;
        try
        {
            bool saved = true;
            if (ContentFrame.Content is NotesPage page)
            {
                saved = await page.FlushBeforeCloseAsync();
            }

            bool committed = saved &&
                await VaultService.Current.FlushCommitsAsync(TimeSpan.FromSeconds(10));

            canClose = saved && committed
                ? true
                : await ConfirmCloseWithoutSavingAsync();
        }
        finally
        {
            _closeFlowRunning = false;
        }

        if (canClose)
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private async Task<bool> ConfirmCloseWithoutSavingAsync()
    {
        ContentDialog dialog = new()
        {
            XamlRoot = ContentFrame.XamlRoot,
            Title = Strings.Get("CloseUnsavedTitle"),
            Content = Strings.Get("CloseUnsavedBody"),
            PrimaryButtonText = Strings.Get("CloseUnsavedPrimary"),
            CloseButtonText = Strings.Get("CloseUnsavedCancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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
