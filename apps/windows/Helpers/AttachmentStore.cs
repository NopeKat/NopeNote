using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NopeNote.Core.Vault;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NopeNote.Windows.Helpers;

/// <summary>Stores pasted or dropped files under the vault's content-addressed folder.</summary>
internal static class AttachmentStore
{
    private const string AttachmentFolder = VaultLayout.AttachmentsDirectory;
    private const string ReferencePrefix = VaultLayout.AttachmentReferencePrefix;

    internal static string FolderPath(string vaultPath)
        => Path.Combine(vaultPath, VaultLayout.AttachmentsDirectory);

    internal static async Task<string> SaveFileAsync(IVaultStore store, StorageFile file)
    {
        using Stream source = await file.OpenStreamForReadAsync();
        return await SaveAsync(store, source, Path.GetExtension(file.Name)).ConfigureAwait(false);
    }

    internal static async Task<string?> SaveClipboardImageAsync(IVaultStore store)
    {
        DataPackageView content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap))
        {
            return null;
        }

        RandomAccessStreamReference reference = await content.GetBitmapAsync();
        using IRandomAccessStream input = await reference.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input);

        using InMemoryRandomAccessStream output = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        var pixels = await decoder.GetPixelDataAsync();
        encoder.SetPixelData(
            decoder.BitmapPixelFormat,
            decoder.BitmapAlphaMode,
            decoder.PixelWidth,
            decoder.PixelHeight,
            decoder.DpiX,
            decoder.DpiY,
            pixels.DetachPixelData());
        await encoder.FlushAsync();

        output.Seek(0);
        using Stream source = output.AsStreamForRead();
        return await SaveAsync(store, source, ".png").ConfigureAwait(false);
    }

    /// <summary>
    /// Hashes the bytes into a temporary file, then moves them to
    /// <c>.vault/atts/&lt;sha256&gt;.&lt;ext&gt;</summary> and records the write.
    /// </summary>
    private static async Task<string> SaveAsync(IVaultStore store, Stream source, string extension)
    {
        string root = store.RootPath;
        string tempDirectory = Path.Combine(root, ".tmp");
        Directory.CreateDirectory(tempDirectory);
        string tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tmp");

        string hash = await WriteTempAsync(source, tempPath).ConfigureAwait(false);
        string name = ComposeName(hash, extension);
        string destination = Path.Combine(FolderPath(root), name);
        Directory.CreateDirectory(FolderPath(root));

        if (File.Exists(destination))
        {
            File.Delete(tempPath);
        }
        else
        {
            File.Move(tempPath, destination);
        }

        await store.RegisterAttachmentAsync(hash).ConfigureAwait(false);
        return $"{ReferencePrefix}/{name}";
    }

    private static async Task<string> WriteTempAsync(Stream source, string tempPath)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using FileStream target = new(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        byte[] buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ComposeName(string hash, string extension)
    {
        string source = extension.TrimStart('.').ToLowerInvariant();
        var builder = new StringBuilder(source.Length);
        foreach (char character in source)
        {
            bool digit = character >= '0' && character <= '9';
            bool lowerCase = character >= 'a' && character <= 'z';
            if (digit || lowerCase)
            {
                builder.Append(character);
            }

            if (builder.Length == 12)
            {
                break;
            }
        }

        string suffix = builder.Length == 0 ? "bin" : builder.ToString();
        return $"{hash}.{suffix}";
    }
}
