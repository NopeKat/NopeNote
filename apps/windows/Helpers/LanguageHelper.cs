using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;
using System;

namespace NopeNote.Windows.Helpers;

public static class LanguageHelper
{
    public const string FollowSystem = "";
    public const string EnglishUnitedStates = "en-US";
    public const string SimplifiedChinese = "zh-Hans";

    public static string SelectedLanguage(SettingsHelper settings) => settings.SelectedAppLanguage;

    /// <summary>把设置里的语言同步到运行时覆盖值，启动时和切换语言时都调用。</summary>
    public static void Apply(SettingsHelper settings)
    {
        string language = settings.SelectedAppLanguage;
        if (language is EnglishUnitedStates or SimplifiedChinese)
        {
            ApplicationLanguages.PrimaryLanguageOverride = language;
            return;
        }

        if (IsPackaged())
        {
            // 打包应用的语言覆盖会跨会话保存，用系统 API 清空才能回到系统语言
            global::Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
        }

        // 非打包应用里把覆盖值清空会让原生层直接崩，所以改成显式设成系统语言列表的首项。
        string system = string.Empty;
        foreach (string tag in ApplicationLanguages.Languages)
        {
            if (tag.Length > 0)
            {
                system = tag;
                break;
            }
        }

        if (system.Length > 0)
        {
            ApplicationLanguages.PrimaryLanguageOverride = system;
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
}
