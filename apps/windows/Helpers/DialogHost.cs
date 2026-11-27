using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NopeNote.Windows.Helpers;

/// <summary>Serializes ContentDialog calls across the app. WinUI throws when a
/// second dialog is shown while one is already open; waiting here turns that
/// into a short queue instead of an unhandled async-void failure.</summary>
internal static class DialogHost
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await Gate.WaitAsync().ConfigureAwait(true);
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static async Task RunGuardedAsync(Func<Task> action, Action<string> showError)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            showError(string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message);
        }
    }
}
