using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Models;
using NopeNote.Windows.ViewModels;
using System;
using System.Threading.Tasks;

namespace NopeNote.Windows.Views;

public sealed partial class NotesPage : Page
{
    private const double NarrowWidthThreshold = 900;
    private const double ListColumnWidth = 320;

    private bool _isSaving;
    private Note? _pendingSaveNote;

    public NotesPage()
    {
        ViewModel = new NotesViewModel();
        InitializeComponent();
        Loaded += NotesPage_Loaded;
        SizeChanged += NotesPage_SizeChanged;
        NoteSearchState.QueryChanged += OnSearchQueryChanged;
        Unloaded += NotesPage_Unloaded;
    }

    public NotesViewModel ViewModel { get; }

    public Note? SelectedNote { get; private set; }

    private void NotesPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLayout(e.NewSize.Width < NarrowWidthThreshold);
    }

    private void NotesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        NoteSearchState.QueryChanged -= OnSearchQueryChanged;
    }

    private async void OnSearchQueryChanged(object? sender, EventArgs e)
    {
        await EnsureNotesLoadedAsync();
        ApplySearch();
    }

    private async void NotesPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout(ActualWidth < NarrowWidthThreshold);
        await EnsureNotesLoadedAsync();
        ApplySearch();
    }

    private async Task EnsureNotesLoadedAsync()
    {
        if (ViewModel.IsLoaded)
        {
            return;
        }

        await ViewModel.LoadAsync();
    }

    private void ApplySearch()
    {
        ViewModel.ApplyFilter(NoteSearchState.Query);
        UpdateEmptyState();
    }

    private async void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        Note note = new Note();
        ViewModel.Add(note);
        await note.SaveAsync();
        ApplySearch();
        SelectNote(note);
        NoteEditor.Focus(FocusState.Programmatic);
    }

    private async void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNote is null)
        {
            return;
        }

        ContentDialog dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete note",
            Content = "This note will be permanently deleted. Continue?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || SelectedNote is null)
        {
            return;
        }

        Note noteToDelete = SelectedNote;
        await noteToDelete.DeleteAsync();

        if (ReferenceEquals(_pendingSaveNote, noteToDelete))
        {
            _pendingSaveNote = null;
        }

        ViewModel.Remove(noteToDelete);
        ApplySearch();
        ClearSelection();
    }

    private void NotesItemsView_ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is Note note)
        {
            SelectNote(note);
        }
    }

    private async void NoteEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        await SaveSelectedNoteAsync();
    }

    private async Task SaveSelectedNoteAsync()
    {
        Note? note = SelectedNote;
        if (note is null)
        {
            return;
        }

        if (_isSaving)
        {
            _pendingSaveNote = note;
            return;
        }

        _isSaving = true;
        SaveStatusTextBlock.Text = "Saving...";

        try
        {
            do
            {
                _pendingSaveNote = null;
                await note.SaveAsync();

                if (ReferenceEquals(SelectedNote, note))
                {
                    SaveStatusTextBlock.Text = "Saved";
                }

                note = _pendingSaveNote;
            }
            while (note is not null);
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void SelectNote(Note note)
    {
        SelectedNote = note;
        NoteEditor.IsEnabled = true;
        NoteDateTextBlock.Text = note.Date.ToString("g");
        DeleteNoteButton.IsEnabled = true;
        SaveStatusTextBlock.Text = string.Empty;
        Bindings.Update();
        ApplyLayout(ActualWidth < NarrowWidthThreshold);
    }

    private void ClearSelection()
    {
        SelectedNote = null;
        NoteEditor.IsEnabled = false;
        NoteEditor.Text = string.Empty;
        NoteDateTextBlock.Text = "Select a note";
        DeleteNoteButton.IsEnabled = false;
        SaveStatusTextBlock.Text = string.Empty;
        Bindings.Update();
        ApplyLayout(ActualWidth < NarrowWidthThreshold);
    }

    private void BackToListButton_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
    }

    private void UpdateEmptyState()
    {
        bool isSearching = !string.IsNullOrWhiteSpace(NoteSearchState.Query);
        EmptyStateTextBlock.Text = isSearching
            ? "No notes match your search. Clear the search box to see all notes."
            : "No notes yet. Select New note to create one.";
        EmptyStateTextBlock.Visibility = ViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        NotesItemsView.Visibility = ViewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyLayout(bool isNarrow)
    {
        ListColumn.Width = isNarrow
            ? new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star)
            : new Microsoft.UI.Xaml.GridLength(ListColumnWidth);
        DetailColumn.Width = isNarrow
            ? new Microsoft.UI.Xaml.GridLength(0)
            : new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star);

        bool hasSelection = SelectedNote is not null;
        NotesListPanel.Visibility = isNarrow && hasSelection ? Visibility.Collapsed : Visibility.Visible;
        NoteDetailPanel.Visibility = !isNarrow || hasSelection ? Visibility.Visible : Visibility.Collapsed;
        BackToListButton.Visibility = isNarrow && hasSelection ? Visibility.Visible : Visibility.Collapsed;
    }
}
