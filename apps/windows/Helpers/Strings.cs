using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Globalization;

namespace NopeNote.Windows.Helpers;

internal static class Strings
{
    private static readonly ResourceLoader Loader = new();

    internal static string Get(string name)
    {
        return Loader.GetString(name);
    }

    internal static string Format(string name, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, Loader.GetString(name), arguments);
    }
}
