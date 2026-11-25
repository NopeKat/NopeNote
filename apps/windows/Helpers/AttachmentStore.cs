using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NopeNote.Windows.Helpers;

/// <summary>Copies dropped or pasted files into the vault attachment folder.</summary>
internal static class AttachmentStore
{
    private const string AttachmentFolder = ".vault/attachments";

    internal static string FolderPath(string vaultPath)
        => Path.Combine(vaultPath, ".vault", "attachments");

    internal static async Task<string> SaveFileAsync(string vaultPath, StorageFile file)
    {
        Directory.CreateDirectory(FolderPath(vaultPath));

        string extension = Path.GetExtension(file.Name);
        string name = CreateName(extension);
        string destination = Path.Combine(FolderPath(vaultPath), name);

        using Stream source = await file.OpenStreamForReadAsync();
        using FileStream target = File.Create(destination);
        await source.CopyToAsync(target);

        return $"{AttachmentFolder}/{name}";
    }

    internal static async Task<string?> SaveClipboardImageAsync(string vaultPath)
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

        Directory.CreateDirectory(FolderPath(vaultPath));
        string name = CreateName(".png");
        string destination = Path.Combine(FolderPath(vaultPath), name);

        output.Seek(0);
        using Stream source = output.AsStreamForRead();
        using FileStream target = File.Create(destination);
        await source.CopyToAsync(target);

        return $"{AttachmentFolder}/{name}";
    }

    private static string CreateName(string extension)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string suffix = Guid.NewGuid().ToString("N")[..6];
        string safeExtension = extension.Length == 0 ? string.Empty : extension.ToLowerInvariant();
        return $"{stamp}-{suffix}{safeExtension}";
    }
}
