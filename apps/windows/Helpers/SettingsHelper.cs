using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NopeNote.Windows.Helpers;

public sealed class SettingsHelper
{
    private readonly string _settingsPath;

    public ElementTheme SelectedAppTheme { get; set; } = ElementTheme.Default;

    public string VaultPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NopeNote",
        "Vault");

    public bool LegacyImportDone { get; set; }

    public string SelectedAppLanguage { get; set; } = string.Empty;

    public string AppLockSalt { get; set; } = string.Empty;

    public string AppLockHash { get; set; } = string.Empty;

    /// <summary>Stable 8-character lowercase id used in conflict copy names.</summary>
    public string DeviceId { get; private set; } = string.Empty;

    public bool HasAppLock => !string.IsNullOrWhiteSpace(AppLockSalt) &&
                              !string.IsNullOrWhiteSpace(AppLockHash);

    public SettingsHelper()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NopeNote");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
        Load();
        EnsureDeviceId();
    }

    public void Save()
    {
        SettingsDto dto = new()
        {
            SelectedAppTheme = SelectedAppTheme,
            VaultPath = VaultPath,
            LegacyImportDone = LegacyImportDone,
            SelectedAppLanguage = SelectedAppLanguage,
            AppLockSalt = AppLockSalt,
            AppLockHash = AppLockHash,
            DeviceId = DeviceId,
        };
        string json = JsonSerializer.Serialize(dto, SettingsJsonContext.Default.SettingsDto);
        File.WriteAllText(_settingsPath, json);
    }

    private void EnsureDeviceId()
    {
        if (IsValidDeviceId(DeviceId))
        {
            return;
        }

        DeviceId = GenerateDeviceId();
        try
        {
            Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Keep the generated id for this session when the file is locked.
        }
    }

    private static bool IsValidDeviceId(string? value)
    {
        if (value is null || value.Length != 8)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool digit = character >= '0' && character <= '9';
            bool lowerCase = character >= 'a' && character <= 'z';
            if (!digit && !lowerCase)
            {
                return false;
            }
        }

        return true;
    }

    private static string GenerateDeviceId()
    {
        const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        char[] buffer = new char[8];
        for (int index = 0; index < buffer.Length; index++)
        {
            buffer[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(buffer);
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
                SelectedAppLanguage = dto.SelectedAppLanguage ?? string.Empty;
                AppLockSalt = dto.AppLockSalt ?? string.Empty;
                AppLockHash = dto.AppLockHash ?? string.Empty;
                DeviceId = dto.DeviceId ?? string.Empty;
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

    public string? SelectedAppLanguage { get; set; }

    public string? AppLockSalt { get; set; }

    public string? AppLockHash { get; set; }

    public string? DeviceId { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsDto))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
