using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;
using System;
using System.Diagnostics;

namespace NopeNote.Windows.Helpers;

public static class LanguageHelper
{
    public const string FollowSystem = "";
    public const string EnglishUnitedStates = "en-US";
    public const string SimplifiedChinese = "zh-Hans";

    public static string SelectedLanguage(SettingsHelper settings) => settings.SelectedAppLanguage;

    public static void ApplyStartupOverride(SettingsHelper settings)
    {
        string language = settings.SelectedAppLanguage;
        switch (language)
        {
            case EnglishUnitedStates:
            case SimplifiedChinese:
                ApplicationLanguages.PrimaryLanguageOverride = language;
                break;
            case FollowSystem when IsPackaged():
                // 打包应用的语言覆盖会跨会话保存，用系统 API 清空才能回到系统语言
                global::Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
                break;
        }
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = global::Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Select(SettingsHelper settings, string language)
    {
        if (language is not (FollowSystem or EnglishUnitedStates or SimplifiedChinese))
        {
            throw new ArgumentOutOfRangeException(nameof(language));
        }

        settings.SelectedAppLanguage = language;
        settings.Save();
    }

    public static void RestartApplication()
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            return;
        }

        Application.Current.Exit();
    }
}
