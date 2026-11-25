using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Models;
using NopeNote.Windows.Services;
using NopeNote.Windows.ViewModels;
using System;
using System.Threading.Tasks;

namespace NopeNote.Windows.Views;

public sealed partial class SearchPage : Page
{
    private static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(200);

    private bool _isLoading;
    private bool _isReady;
    private bool _loadFailed;
    private int _filterVersion;
    private int _loadedVersion = -1;
    private string? _appliedQuery;
    private readonly SolidColorBrush _highlightBrush = new(Colors.Gold) { Opacity = 0.35 };

    public SearchPage()
    {
        ViewModel = new NotesViewModel();
        InitializeComponent();
        _isReady = true;
        Loaded += SearchPage_Loaded;
    }

    public NotesViewModel ViewModel { get; }

    public void FocusSearch()
    {
        SearchInput.Focus(FocusState.Programmatic);
    }

    private async void SearchPage_Loaded(object sender, RoutedEventArgs e)
    {
        SearchInput.Focus(FocusState.Programmatic);
        await EnsureNotesLoadedAsync();
    }

    private async Task EnsureNotesLoadedAsync()
    {
        if (_loadFailed)
        {
            ApplyFilterIfChanged();
            return;
        }

        // 页面缓存复用同一个实例，仓库和版本都没变就不重过滤。
        if (ViewModel.IsLoaded && ReferenceEquals(ViewModel.StoreOrNull, VaultService.Current.Store))
        {
            if (_loadedVersion == VaultService.Current.DataVersion)
            {
                ApplyFilterIfChanged();
                return;
            }

            await ViewModel.LoadAsync();
            _loadedVersion = VaultService.Current.DataVersion;
            ApplyFilter();
            return;
        }

        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        UpdateViewState();

        try
        {
            VaultOpenResult opened = await VaultService.Current.GetVaultAsync();
            if (opened.Store is null)
            {
                ShowError(opened.Message);
                return;
            }

            ViewModel.Attach(opened.Store);
            await ViewModel.LoadAsync();
            _loadedVersion = VaultService.Current.DataVersion;
            ErrorInfoBar.IsOpen = false;
        }
        catch (Exception exception)
        {
            ShowError(string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message);
        }
        finally
        {
            _isLoading = false;
            ApplyFilter();
        }
    }

    private void ShowError(string detail)
    {
        _loadFailed = true;
        string hint = Strings.Get("NotesVaultOpenFailedHint");
        ErrorInfoBar.Message = string.IsNullOrWhiteSpace(detail)
            ? Strings.Get("SearchPageVaultError")
            : $"{detail} {hint}";
        ErrorInfoBar.IsOpen = true;
    }

    private void ApplyFilter()
    {
        // 先记录关键词，列表重建时模板里的高亮处理才能取到当前值。
        _appliedQuery = SearchInput.Text;
        ViewModel.ApplyFilter(SearchInput.Text);
        UpdateViewState();
    }

    private void ApplyFilterIfChanged()
    {
        if (string.Equals(SearchInput.Text, _appliedQuery, StringComparison.Ordinal))
        {
            return;
        }

        ApplyFilter();
    }

    private void UpdateViewState()
    {
        LoadingRing.IsActive = _isLoading;
        LoadingRing.Visibility = _isLoading ? Visibility.Visible : Visibility.Collapsed;

        if (_isLoading)
        {
            ResultsList.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        if (_loadFailed)
        {
            ResultsList.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        bool isEmpty = ViewModel.IsEmpty;
        ResultsList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

        if (isEmpty)
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(SearchInput.Text);
            EmptyText.Text = hasQuery
                ? Strings.Get("SearchPageNoResults")
                : Strings.Get("SearchPageEmptyHint");
        }
    }

    private void SearchInput_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_isReady && !_isLoading)
        {
            ScheduleApplyFilter();
        }
    }

    // 连续输入时只让最后一次生效，避免每个按键都重建结果列表。
    private async void ScheduleApplyFilter()
    {
        int version = ++_filterVersion;
        await Task.Delay(FilterDelay);

        if (version != _filterVersion || !_isReady || _isLoading)
        {
            return;
        }

        ApplyFilter();
    }

    private void SearchInput_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _filterVersion++;
        ApplyFilter();
    }

    private void SearchResultText_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not TextBlock textBlock)
        {
            return;
        }

        textBlock.TextHighlighters.Clear();
        if (args.NewValue is Note note)
        {
            // 等绑定把 Text 写进来之后再按真实文本算范围。
            textBlock.DispatcherQueue.TryEnqueue(() => ApplyHighlight(textBlock, note));
        }
    }

    private void ApplyHighlight(TextBlock textBlock, Note note)
    {
        textBlock.TextHighlighters.Clear();

        string query = _appliedQuery ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var highlighter = new TextHighlighter { Background = _highlightBrush };
        int index = 0;
        while (highlighter.Ranges.Count < 30)
        {
            int match = note.Text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                break;
            }

            highlighter.Ranges.Add(new TextRange(match, query.Length));
            index = match + query.Length;
        }

        if (highlighter.Ranges.Count > 0)
        {
            textBlock.TextHighlighters.Add(highlighter);
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _loadFailed = false;
        ErrorInfoBar.IsOpen = false;
        VaultService.Current.Reset();
        await EnsureNotesLoadedAsync();
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Note note)
        {
            Frame.Navigate(typeof(NotesPage), note.Id);
        }
    }
}
