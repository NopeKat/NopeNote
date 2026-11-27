using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Windows.Helpers;
using System;
using System.Threading.Tasks;

namespace NopeNote.Windows.Services;

internal enum WindowCloseOutcome
{
    /// <summary>The window may close as-is.</summary>
    Allow,

    /// <summary>The user backed out; the window stays open.</summary>
    Canceled,

    /// <summary>The close was confirmed after saving or discarding, so closing must
    /// be restarted now that the guard is armed.</summary>
    CloseNow,
}

/// <summary>Runs the "save or discard pending work" flow when the main window closes.</summary>
internal sealed class WindowCloseCoordinator
{
    private readonly Func<XamlRoot?> _rootProvider;
    private readonly Func<bool> _hasUnsavedWork;
    private readonly Func<Task<bool>> _saveWorkAsync;
    private bool _confirmed;
    private bool _running;

    internal WindowCloseCoordinator(
        Func<XamlRoot?> rootProvider,
        Func<bool> hasUnsavedWork,
        Func<Task<bool>> saveWorkAsync)
    {
        _rootProvider = rootProvider;
        _hasUnsavedWork = hasUnsavedWork;
        _saveWorkAsync = saveWorkAsync;
    }

    internal async Task<WindowCloseOutcome> HandleAsync(AppWindowClosingEventArgs args)
    {
        if (_confirmed)
        {
            return WindowCloseOutcome.Allow;
        }

        if (_running)
        {
            args.Cancel = true;
            return WindowCloseOutcome.Canceled;
        }

        if (!_hasUnsavedWork())
        {
            return WindowCloseOutcome.Allow;
        }

        args.Cancel = true;
        _running = true;

        bool canClose;
        try
        {
            bool saved = await _saveWorkAsync();
            canClose = saved || await ConfirmDiscardAsync();
        }
        finally
        {
            _running = false;
        }

        if (!canClose)
        {
            return WindowCloseOutcome.Canceled;
        }

        _confirmed = true;
        return WindowCloseOutcome.CloseNow;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        ContentDialog dialog = new()
        {
            XamlRoot = _rootProvider(),
            Title = Strings.Get("CloseUnsavedTitle"),
            Content = Strings.Get("CloseUnsavedBody"),
            PrimaryButtonText = Strings.Get("CloseUnsavedPrimary"),
            CloseButtonText = Strings.Get("CloseUnsavedCancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await DialogHost.ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
