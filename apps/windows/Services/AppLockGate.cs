using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NopeNote.Windows.Helpers;
using System;
using System.Threading.Tasks;

namespace NopeNote.Windows.Services;

/// <summary>Prompts for the app-lock password before the shell becomes usable.</summary>
internal sealed class AppLockGate
{
    private readonly SettingsHelper _settings;

    internal AppLockGate(SettingsHelper settings)
    {
        _settings = settings;
    }

    /// <summary>Returns true when the app is unlocked (or has no lock configured).</summary>
    internal async Task<bool> UnlockAsync(Func<XamlRoot?> rootProvider)
    {
        if (!_settings.HasAppLock)
        {
            return true;
        }

        string? error = null;
        while (true)
        {
            XamlRoot? root = rootProvider();
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

            if (await DialogHost.ShowAsync(dialog) != ContentDialogResult.Primary)
            {
                return false;
            }

            if (PasswordHasher.Verify(input.Password, _settings.AppLockSalt, _settings.AppLockHash))
            {
                return true;
            }

            error = Strings.Get("AppLockWrongPassword");
            await Task.Delay(50);
        }
    }
}
