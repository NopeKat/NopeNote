using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NopeNote.Core.Vault;
using NopeNote.Windows.Helpers;
using NopeNote.Windows.Services;
using System;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.System;

namespace NopeNote.Windows.Views;

/// <summary>Small always-on-top-optional window for jotting a note from anywhere.</summary>
public sealed partial class QuickCaptureWindow : Window
{
    private bool _saving;
    private bool _sized;
    private readonly AppServices _services;

    public QuickCaptureWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        Title = Strings.Get("QuickCaptureTitle");
        HeaderText.Text = Strings.Get("QuickCaptureTitle");
        CaptureTextBox.PlaceholderText = Strings.Get("QuickCapturePlaceholder");
        HintText.Text = Strings.Get("QuickCaptureHint");
        SaveButton.Content = Strings.Get("QuickCaptureSave");
    }

    public void FocusInput()
    {
        CaptureTextBox.Focus(FocusState.Programmatic);
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        BackdropHelper.Apply(this);

        if (!_sized)
        {
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.Resize(new SizeInt32((int)(460 * scale), (int)(340 * scale)));
            _sized = true;
        }

        FocusInput();
    }

    private void CaptureTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void SaveAccelerator_Invoked(
        KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = SaveAsync();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (_saving)
        {
            return;
        }

        string text = CaptureTextBox.Text.Trim();
        if (text.Length == 0)
        {
            Close();
            return;
        }

        _saving = true;
        SaveButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            VaultOpenResult open = await _services.Vault.GetVaultAsync();
            if (open.Store is null)
            {
                throw new InvalidOperationException(Strings.Get("QuickCaptureNoVault"));
            }

            await open.Store.CreateNoteAsync(string.Empty, text);
            _services.Vault.NotifyDataChanged();
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = Strings.Format("QuickCaptureFailed", exception.Message);
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = true;
        }
    }
}
