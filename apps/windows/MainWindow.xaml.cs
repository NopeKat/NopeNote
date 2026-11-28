using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Navigation;
using NopeNote.Windows.Services;
using NopeNote.Windows.Views;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace NopeNote.Windows;

public sealed partial class MainWindow : Window
{
    private UISettings? _uiSettings;
    private bool _isNavigating;
    private object? _navItemBeforePress;
    private bool _paneOpenBeforePress;
    private QuickCaptureWindow? _quickCaptureWindow;
    private readonly AppServices _services;
    private readonly AppLockGate _appLock;
    private readonly WindowCloseCoordinator _closeFlow;
    private readonly IReadOnlyDictionary<string, NavigationViewItem> _navItems;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        _navItems = new Dictionary<string, NavigationViewItem>
        {
            [ShellRoutes.Notes.Tag] = NotesNavItem,
            [ShellRoutes.Search.Tag] = SearchNavItem,
            [ShellRoutes.Trash.Tag] = TrashNavItem,
            [ShellRoutes.Settings.Tag] = SettingsNavItem,
        };

        Title = "NopeNote";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/Tiles/NopeNoteIcon.ico");
        AppWindow.Closing += AppWindow_Closing;

        RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;

        _appLock = new AppLockGate(_services.Settings);
        _closeFlow = new WindowCloseCoordinator(
            () => ContentFrame.XamlRoot,
            HasPendingWork,
            SavePendingWorkAsync);

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
        NavigateTo(ShellRoutes.Notes);
        GlobalHotkey.RegisterQuickCapture(WindowHelper.GetWindowHandle(this), ShowQuickCapture);
    }

    /// <summary>切语言后重建界面：导航项文案和页面里的 x:Uid 文案都只在 XAML
    /// 加载时解析，所以重设导航项、清掉页面缓存让当前页按新语言重新加载。</summary>
    internal void ReloadLanguageUi()
    {
        ApplyNavText(NotesNavItem, "NavNotes");
        ApplyNavText(SearchNavItem, "NavSearch");
        ApplyNavText(TrashNavItem, "NavTrash");
        ApplyNavText(SettingsNavItem, "NavSettings");

        ShellRoute route = ContentFrame.CurrentSourcePageType is { } pageType
            ? ShellRoutes.ForPage(pageType)
            : ShellRoutes.Notes;

        // 页面缓存会让旧语言的页面实例被复用。先把容量压到 0 清掉缓存、恢复容量，
        // 再导航当前页，这样页面按新语言重建，后续导航也照常缓存。
        int cacheSize = ContentFrame.CacheSize;
        ContentFrame.CacheSize = 0;
        ContentFrame.CacheSize = cacheSize;
        NavigateTo(route);
    }
    private static void ApplyNavText(NavigationViewItem item, string key)
    {
        // 资源键里的点会被 PRI 当作路径分隔，所以取名字要写成多段路径。
        item.Content = Strings.Get(key + "/Content");
        AutomationProperties.SetName(item, Strings.Get(key + "/AutomationProperties/Name"));
    }

    private void NavigateTo(ShellRoute route)
    {
        object parameter = route.Tag == ShellRoutes.Notes.Tag && _services.Shell.LastNoteId is { } noteId
            ? new NotesNavigation(noteId, _services)
            : route.ParameterFactory(_services);

        ContentFrame.Navigate(route.PageType, parameter);
    }

    internal Task<bool> EnsureUnlockedAsync() => _appLock.UnlockAsync(() => RootGrid.XamlRoot);

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
            _quickCaptureWindow = new QuickCaptureWindow(_services);
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
        if (ContentFrame.CurrentSourcePageType == ShellRoutes.Search.PageType)
        {
            ((SearchPage)ContentFrame.Content).FocusSearch();
        }
        else
        {
            NavigateTo(ShellRoutes.Search);
        }

        _navItems[ShellRoutes.Search.Tag].IsSelected = true;
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
        ShellRoute? route = args.SelectedItem is NavigationViewItem item
            ? ShellRoutes.ForTag(item.Tag?.ToString())
            : null;

        if (route is null || ContentFrame.CurrentSourcePageType == route.PageType)
        {
            return;
        }

        NavigateTo(route);
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        _isNavigating = true;

        ShellRoute route = ShellRoutes.ForPage(e.SourcePageType);
        _navItems[route.Tag].IsSelected = true;

        // 自动化测试按这个英文标记判断当前页，显示文案由各页自己的资源键负责。
        CurrentPageTextBlock.Text = route.Tag;

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

    private bool HasPendingWork()
        => _services.Vault.HasPendingCommits ||
            (ContentFrame.Content is NotesPage notes && notes.HasUnsavedWork());

    private async Task<bool> SavePendingWorkAsync()
    {
        bool saved = ContentFrame.Content is not NotesPage page || await page.FlushBeforeCloseAsync();
        return saved && await _services.Vault.FlushCommitsAsync(TimeSpan.FromSeconds(10));
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // 主题这类高频改动是延迟写盘的，关窗前先把它们落盘。
        _services.Settings.FlushDeferred();

        if (await _closeFlow.HandleAsync(args) == WindowCloseOutcome.CloseNow)
        {
            Close();
        }
    }

    private async void WaitForIdleInvokerButton_Click(object sender, RoutedEventArgs e)
    {
        IdleStateEnteredCheckBox.IsChecked = false;

        (string error, string log) = await AutomationIdleProbe.RunAsync();

        ErrorReportingTextBox.Text = error;
        LogReportingTextBox.Text = log;
        IdleStateEnteredCheckBox.IsChecked = string.IsNullOrEmpty(error);
    }
}
