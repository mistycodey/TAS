using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace TAS;

/// <summary>
/// Downloads the alert images the user points TAS at and keeps a copy in the data folder, so showing an alert never waits
/// on the network. Only http and https addresses are fetched.
/// </summary>
internal static class ImageCache
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private const int MaxSide = 2048;
    private const long MaxPixels = 40_000_000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, Task<string?>> InFlight = new();

    private static string Folder { get; } = Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "images");

    public static bool IsValidUrl(string? url, out Uri uri)
    {
        if (Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? parsed) && parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    public const int MaxImagesPerAlert = 20;

    /// <summary>
    /// Splits text with one address per line into a clean list: blank lines and repeats removed, at most
    /// <see cref="MaxImagesPerAlert"/> entries.
    /// </summary>
    public static List<string> ParseList(string? text) => (text ?? "")
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxImagesPerAlert)
        .ToList();

    /// <summary>
    /// Picks one image at random from the ones that are downloaded. Addresses that aren't downloaded yet start
    /// downloading in the background. Returns null if none can be shown.
    /// </summary>
    public static Image? TryLoadRandom(IEnumerable<string>? urls)
    {
        if (urls is null)
            return null;

        var ready = new List<string>();
        foreach (string url in urls)
        {
            if (IsCached(url))
                ready.Add(url);
            else if (IsValidUrl(url, out _))
                _ = EnsureAsync(url);
        }

        // Random order, so that an image that fails to load just falls through to another one.
        foreach (string url in ready.OrderBy(_ => Random.Shared.Next()))
        {
            Image? image = TryLoad(url);
            if (image is not null)
                return image;
        }

        return null;
    }

    public static bool IsCached(string? url) => IsValidUrl(url, out Uri uri) && File.Exists(PathFor(uri));

    /// <summary>
    /// Returns a copy of the cached image, or null if it isn't downloaded yet. A missing image starts downloading in the
    /// background so that it's ready for the next alert.
    /// </summary>
    public static Image? TryLoad(string? url)
    {
        if (!IsValidUrl(url, out Uri uri))
            return null;

        string path = PathFor(uri);
        if (!File.Exists(path))
        {
            _ = EnsureAsync(url!);
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using Image image = Image.FromStream(stream);
            // A copy, so the file isn't kept locked by the image.
            return new Bitmap(image);
        }
        catch (Exception ex)
        {
            Log.Write($"Could not read cached image for {uri}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Makes sure the image at <paramref name="url"/> is in the cache. Returns null on success, otherwise a short reason.
    /// </summary>
    public static Task<string?> EnsureAsync(string url, bool force = false)
    {
        if (!IsValidUrl(url, out Uri uri))
            return Task.FromResult<string?>("the address must start with http:// or https://");

        string path = PathFor(uri);
        if (!force && File.Exists(path))
            return Task.FromResult<string?>(null);

        return InFlight.GetOrAdd(uri.AbsoluteUri, _ => DownloadAsync(uri, path)).ContinueWith(task =>
        {
            InFlight.TryRemove(uri.AbsoluteUri, out _);
            return task.Result;
        });
    }

    private static async Task<string?> DownloadAsync(Uri uri, string path)
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return $"the server returned {(int)response.StatusCode}";
            if (response.Content.Headers.ContentLength > MaxBytes)
                return "the image is larger than 5 MB";

            await using Stream body = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes)
                    return "the image is larger than 5 MB";
            }

            byte[] png;
            try
            {
                png = ToPng(buffer.ToArray());
            }
            catch (Exception ex)
            {
                Log.Write($"Could not read {uri} as an image: {ex.Message}");
                return "TAS couldn't read that as an image";
            }

            Directory.CreateDirectory(Folder);
            await File.WriteAllBytesAsync(path, png);
            return null;
        }
        catch (Exception ex)
        {
            Log.Write($"Could not download image {uri}: {ex.Message}");
            return "TAS couldn't download it (check the address and your connection)";
        }
    }

    /// <summary>
    /// Decodes an image in whatever format the server sent (the content type and file extension are ignored) and
    /// re-encodes it as PNG, shrunk to at most <see cref="MaxSide"/> pixels since alerts only show it small.
    /// Animated images keep their first frame.
    /// </summary>
    private static byte[] ToPng(byte[] bytes)
    {
        using (var probe = SKCodec.Create(new MemoryStream(bytes)) ?? throw new InvalidDataException("unrecognised image format"))
        {
            if ((long)probe.Info.Width * probe.Info.Height > MaxPixels)
                throw new InvalidDataException("image has too many pixels");
        }

        using SKBitmap decoded = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("image could not be decoded");

        SKBitmap shown = decoded;
        SKBitmap? resized = null;
        int longest = Math.Max(decoded.Width, decoded.Height);
        if (longest > MaxSide)
        {
            double scale = (double)MaxSide / longest;
            var size = new SKImageInfo(Math.Max(1, (int)(decoded.Width * scale)), Math.Max(1, (int)(decoded.Height * scale)),
                decoded.ColorType, decoded.AlphaType);
            resized = decoded.Resize(size, SKFilterQuality.High);
            if (resized is not null)
                shown = resized;
        }

        try
        {
            using SKImage image = SKImage.FromBitmap(shown);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static string PathFor(Uri uri) =>
        Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..32] + ".png");
}
