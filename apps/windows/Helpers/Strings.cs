using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Globalization;

namespace NopeNote.Windows.Helpers;

internal static class Strings
{
    private static ResourceManager? _manager;

    /// <summary>切换语言后调用：丢掉缓存的资源管理器，下一次取文串会按新的语言覆盖重新绑定。</summary>
    internal static void Reload() => _manager = null;

    private static ResourceManager Manager => _manager ??= new ResourceManager();

    internal static string Get(string name)
    {
        ResourceCandidate? candidate = Manager.MainResourceMap.TryGetValue("Resources/" + name);
        return candidate?.ValueAsString ?? name;
    }

    internal static string Format(string name, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(name), arguments);
    }
}
