using System.Text.Json;

namespace NopeNote.Core.Vault;

/// <summary>
/// Repository-level settings kept in `.settings/settings.json`. Only small,
/// non-secret values live here; app preferences stay outside the vault.
/// </summary>
public sealed record VaultSettings(bool GitEnabled)
{
    private const string RelativePath = ".settings/settings.json";

    public static VaultSettings Default { get; } = new(GitEnabled: true);

    public static VaultSettings Read(string rootPath)
    {
        string path = VaultLayout.ToSystemPath(rootPath, RelativePath);
        if (!File.Exists(path))
        {
            return Default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Default;
            }

            bool gitEnabled = document.RootElement.TryGetProperty("git.enabled", out JsonElement element) &&
                              element.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? element.GetBoolean()
                : true;

            return new VaultSettings(gitEnabled);
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    public static void Write(string rootPath, VaultSettings settings)
    {
        string path = VaultLayout.ToSystemPath(rootPath, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string json = JsonSerializer.Serialize(
            new Dictionary<string, object> { ["git.enabled"] = settings.GitEnabled },
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(
            path,
            json.Replace("\r\n", "\n") + "\n",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
