using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        if (ViewModel.IsLoaded || _loadFailed)
        {
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
        ViewModel.ApplyFilter(SearchInput.Text);
        UpdateViewState();
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
