using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NopeNote.Windows.Helpers;

public sealed class SettingsHelper
{
    private static readonly SettingsHelper CurrentInstance = new();
    private readonly string _settingsPath;

    public static SettingsHelper Current => CurrentInstance;

    public ElementTheme SelectedAppTheme { get; set; } = ElementTheme.Default;

    public string VaultPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NopeNote",
        "Vault");

    public bool LegacyImportDone { get; set; }

    private SettingsHelper()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NopeNote");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
        Load();
    }

    public void Save()
    {
        SettingsDto dto = new()
        {
            SelectedAppTheme = SelectedAppTheme,
            VaultPath = VaultPath,
            LegacyImportDone = LegacyImportDone,
        };
        string json = JsonSerializer.Serialize(dto, SettingsJsonContext.Default.SettingsDto);
        File.WriteAllText(_settingsPath, json);
    }

    private void Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        try
        {
            SettingsDto? dto = JsonSerializer.Deserialize(
                File.ReadAllText(_settingsPath),
                SettingsJsonContext.Default.SettingsDto);

            if (dto is not null)
            {
                SelectedAppTheme = dto.SelectedAppTheme;
                LegacyImportDone = dto.LegacyImportDone;
                if (!string.IsNullOrWhiteSpace(dto.VaultPath))
                {
                    VaultPath = dto.VaultPath;
                }
            }
        }
        catch (JsonException)
        {
            SelectedAppTheme = ElementTheme.Default;
        }
    }
}

internal sealed class SettingsDto
{
    public ElementTheme SelectedAppTheme { get; set; }

    public string? VaultPath { get; set; }

    public bool LegacyImportDone { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsDto))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
