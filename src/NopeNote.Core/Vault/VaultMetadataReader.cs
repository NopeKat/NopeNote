using System.Globalization;
using System.Text.Json;

namespace NopeNote.Core.Vault;

public static class VaultMetadataReader
{
    public static VaultMetadata Read(string rootPath)
    {
        string path = Path.Combine(rootPath, ".nopenote");
        if (!File.Exists(path))
        {
            throw new VaultFormatException("missing .nopenote marker.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new VaultFormatException(".nopenote must be a JSON object.");
            }

            string schema = root.TryGetProperty("schema", out JsonElement schemaElement)
                ? schemaElement.GetString() ?? string.Empty
                : string.Empty;

            int formatVersion = root.TryGetProperty("formatVersion", out JsonElement versionElement) &&
                                versionElement.TryGetInt32(out int parsedVersion)
                ? parsedVersion
                : 0;

            Guid repoId = Guid.Empty;
            string repoIdText = string.Empty;
            if (root.TryGetProperty("repoId", out JsonElement repoElement) &&
                repoElement.ValueKind == JsonValueKind.String)
            {
                repoIdText = repoElement.GetString() ?? string.Empty;
                Guid.TryParse(repoIdText, out repoId);
            }

            DateTimeOffset createdAt = DateTimeOffset.MinValue;
            if (root.TryGetProperty("createdAt", out JsonElement createdElement) &&
                createdElement.ValueKind == JsonValueKind.String)
            {
                DateTimeOffset.TryParse(
                    createdElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out createdAt);
            }

            bool encrypted = root.TryGetProperty("encryption", out JsonElement encryptionElement) &&
                             encryptionElement.ValueKind != JsonValueKind.Null;

            return new VaultMetadata(schema, formatVersion, repoId, createdAt, encrypted)
            {
                RepoIdText = repoIdText,
            };
        }
        catch (JsonException exception)
        {
            throw new VaultFormatException(".nopenote is not valid JSON.", exception);
        }
    }
}
