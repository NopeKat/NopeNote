using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Windows.Models;
using NopeNote.Windows.ViewModels;

namespace NopeNote.Windows.Views;

public sealed partial class NotesPage : Page
{
    private Note? _selectedNote;
    private bool _isNarrow;

    public NotesPage()
    {
        ViewModel = new NotesViewModel();
        InitializeComponent();
        Loaded += NotesPage_Loaded;
        SizeChanged += NotesPage_SizeChanged;
    }

    public NotesViewModel ViewModel { get; }

    private void NotesPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isNarrow = e.NewSize.Width < 900;
        if (isNarrow != _isNarrow || e.PreviousSize.Width == 0)
        {
            OnLayoutStateChanged(isNarrow);
        }
    }

    private async void NotesPage_Loaded(object sender, RoutedEventArgs e)
    {
        OnLayoutStateChanged(ActualWidth < 900);
        await ViewModel.LoadAsync();
        UpdateEmptyState();
    }

    private void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        Note note = new Note();
        ViewModel.Add(note);
        SelectNote(note);
        NoteEditor.Focus(FocusState.Programmatic);
        UpdateEmptyState();
    }

    private async void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNote is null)
        {
            return;
        }

        await _selectedNote.DeleteAsync();
        ViewModel.Remove(_selectedNote);
        ClearSelection();
        UpdateEmptyState();
    }

    private void NotesItemsView_ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is Note note)
        {
            SelectNote(note);
        }
    }

    private void NoteEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_selectedNote is not null)
        {
            _selectedNote.Text = NoteEditor.Text;
            _selectedNote.Date = System.DateTime.Now;
        }
    }

    private async void NoteEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_selectedNote is not null)
        {
            await _selectedNote.SaveAsync();
        }
    }

    private void SelectNote(Note note)
    {
        _selectedNote = note;
        NoteEditor.IsEnabled = true;
        NoteEditor.Text = note.Text;
        NoteDateTextBlock.Text = note.Date.ToString("g");
        DeleteNoteButton.IsEnabled = true;
        ShowDetail();
    }

    private void ClearSelection()
    {
        _selectedNote = null;
        NoteEditor.IsEnabled = false;
        NoteEditor.Text = string.Empty;
        NoteDateTextBlock.Text = "Select a note";
        DeleteNoteButton.IsEnabled = false;
    }

    private void ShowDetail()
    {
        if (_isNarrow)
        {
            NotesListPanel.Visibility = Visibility.Collapsed;
            NoteDetailPanel.Visibility = Visibility.Visible;
            BackToListButton.Visibility = Visibility.Visible;
        }
    }

    private void BackToListButton_Click(object sender, RoutedEventArgs e)
    {
        NotesListPanel.Visibility = Visibility.Visible;
        NoteDetailPanel.Visibility = Visibility.Collapsed;
        BackToListButton.Visibility = Visibility.Collapsed;
    }

    private void UpdateEmptyState()
    {
        EmptyStateTextBlock.Visibility = ViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        NotesItemsView.Visibility = ViewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLayoutStateChanged(bool isNarrow)
    {
        _isNarrow = isNarrow;
        if (isNarrow)
        {
            if (_selectedNote is null)
            {
                NoteDetailPanel.Visibility = Visibility.Collapsed;
                NotesListPanel.Visibility = Visibility.Visible;
                BackToListButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                NotesListPanel.Visibility = Visibility.Collapsed;
                NoteDetailPanel.Visibility = Visibility.Visible;
                BackToListButton.Visibility = Visibility.Visible;
            }
        }
        else
        {
            NotesListPanel.Visibility = Visibility.Visible;
            NoteDetailPanel.Visibility = Visibility.Visible;
            BackToListButton.Visibility = Visibility.Collapsed;
        }
    }
}
