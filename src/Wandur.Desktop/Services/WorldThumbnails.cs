using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Services;

/// <summary>
/// One small bitmap per world for the saved worlds list, made once from the same artwork the directory
/// browser downloads and caches (<see cref="WorldCatalog.GetArtAsync"/>), so there is no second download
/// path and the list only ever holds thumbnails. Loads run one at a time: the list is not a browser.
/// </summary>
public sealed class WorldThumbnails : IDisposable
{
    /// <summary>The tile, in layout units: the 4 to 3 letterbox the site uses.</summary>
    public const int Width = 40;
    public const int Height = 30;
    // Two pixels per layout unit keeps the tile crisp on a high density display and is still tiny.
    private const int PixelWidth = Width * 2;
    private const int PixelHeight = Height * 2;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly bool _cacheBitmaps;
    private readonly WorldArtwork _kind;
    private readonly bool _cover;
    private readonly WorldCatalog _catalog;
    private readonly Dictionary<string, Task<Bitmap?>> _loads = new(StringComparer.Ordinal);
    /// <summary>How many pictures load at once. The catalog caps the downloads themselves at the same number.</summary>
    public const int Parallel = 4;
    private readonly SemaphoreSlim _slots = new(Parallel, Parallel);
    private readonly string? _size;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    /// <param name="kind">Which picture to use; <see cref="WorldArtwork.Preferred"/> follows the world's own preference.</param>
    /// <param name="size">The generated art size to ask the directory for, such as <see cref="WorldCatalog.RowSize"/>.</param>
    /// <param name="cover">Scale so the picture covers the pixel box (it will be cropped to fill a plate), rather than fit inside it.</param>
    public WorldThumbnails(WorldCatalog catalog, int pixelWidth = PixelWidth, int pixelHeight = PixelHeight, bool cacheBitmaps = true,
        WorldArtwork kind = WorldArtwork.Preferred, bool cover = false, string? size = null)
    {
        _catalog = catalog;
        _pixelWidth = pixelWidth; _pixelHeight = pixelHeight;
        _cacheBitmaps = cacheBitmaps; _kind = kind; _cover = cover; _size = size;
        // A refreshed catalog may carry artwork a world lacked before; only the misses are forgotten.
        _catalog.Changed += ForgetMisses;
    }

    /// <summary>Up to two letters for the tile of a world without artwork: the first letter of its first two words, as the site does.</summary>
    public static string Initials(string name)
    {
        var letters = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => char.IsLetterOrDigit(word[0]))
            .Select(word => char.ToUpperInvariant(word[0]))
            .Take(2).ToArray();
        return letters.Length > 0 ? new string(letters) : "?";
    }

    /// <summary>The listing a saved world came from, when the directory knows its address.</summary>
    public WorldListing? Find(ConnectionProfile profile) => _catalog.FindEndpoint(profile.Host, profile.Port, profile.UseTls);

    /// <summary>The thumbnail for a saved world, or null when the directory has no artwork for it (or none it can fetch).</summary>
    public Task<Bitmap?> GetAsync(ConnectionProfile profile)
    {
        if (_disposed) return Task.FromResult<Bitmap?>(null);
        var listing = Find(profile);
        return listing is null ? Task.FromResult<Bitmap?>(null) : GetAsync(listing);
    }

    /// <summary>Browser rows use uncached bitmaps they own and dispose; the catalog still caches image bytes on disk.</summary>
    public Task<Bitmap?> GetAsync(WorldListing listing, CancellationToken cancellationToken = default)
    {
        if (_disposed || (!listing.HasSuppliedArtwork && !listing.HasGeneratedArtwork)) return Task.FromResult<Bitmap?>(null);
        if (!_cacheBitmaps) return LoadAsync(listing, cancellationToken);
        // The kind and size are part of the key: the same world's banner and illustration are different pictures.
        var key = $"{_kind}:{_size}:{listing.ArtKey}:{listing.SuppliedArtKey}";
        lock (_loads)
        {
            if (!_loads.TryGetValue(key, out var load)) _loads[key] = load = LoadAsync(listing);
            return load;
        }
    }

    private async Task<Bitmap?> LoadAsync(WorldListing listing, CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var token = lifetime.Token;
        try
        {
            // A row recycled while it waits for a slot never starts a download; one recycled mid-download stops
            // waiting, but the catalog finishes and caches the picture for when the row comes back.
            await _slots.WaitAsync(token);
            try
            {
                var kind = _kind == WorldArtwork.Preferred ? listing.PreferredArtwork : _kind;
                var bytes = await _catalog.GetArtAsync(listing, kind, kind == WorldArtwork.Generated ? _size : null, token);
                // A world whose preferred picture is missing still shows the other one before falling back to initials.
                if (bytes is null && _kind == WorldArtwork.Preferred && listing.SecondaryArtwork is { } other)
                    bytes = await _catalog.GetArtAsync(listing, other, other == WorldArtwork.Generated ? _size : null, token);
                if (bytes is null) return null;
                var bitmap = await Task.Run(() => Shrink(bytes), token);
                if (_disposed) { bitmap?.Dispose(); return null; }
                return bitmap;
            }
            finally { _slots.Release(); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>The whole picture inside the tile's pixel box (or covering it, for a plate that crops), never upscaled
    /// and never stretched out of shape. The codec reads the size from the header and decodes straight to the nearest
    /// size it supports at or above the target (JPEG and WebP scale while decoding), so a 4000 by 3000 JPEG never becomes
    /// a 48 MB bitmap on its way to an 800 pixel plate. PNG cannot: Skia's PNG codec neither scales nor reads row by
    /// row here, so a PNG is still decoded whole, as before. Every intermediate is disposed here.</summary>
    internal Bitmap? Shrink(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) return null;
        var size = codec.Info.Size;
        if (size.Width <= 0 || size.Height <= 0) return null;
        var (across, down) = ((double)_pixelWidth / size.Width, (double)_pixelHeight / size.Height);
        var scale = Math.Min(1.0, _cover ? Math.Max(across, down) : Math.Min(across, down));
        var target = new SKSizeI(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
        // Never below the target in either direction: the codec rounds its scale up, and the resize below finishes the job.
        var sampled = scale >= 1 ? size : codec.GetScaledDimensions((float)Math.Max((double)target.Width / size.Width, (double)target.Height / size.Height));
        if (sampled.Width < target.Width || sampled.Height < target.Height) sampled = size;
        var info = new SKImageInfo(sampled.Width, sampled.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return null;
        if (sampled == target) return Wrap(decoded);
        using var resized = decoded.Resize(new SKImageInfo(target.Width, target.Height, info.ColorType, info.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return resized is null ? null : Wrap(resized);
    }

    /// <summary>Copies the pixels into an Avalonia bitmap, which owns its own memory; the Skia bitmap stays the caller's to dispose.</summary>
    private static Bitmap Wrap(SKBitmap bitmap) => new(
        bitmap.ColorType == SKColorType.Rgba8888 ? Avalonia.Platform.PixelFormat.Rgba8888 : Avalonia.Platform.PixelFormat.Bgra8888,
        Avalonia.Platform.AlphaFormat.Premul, bitmap.GetPixels(), new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), bitmap.RowBytes);

    private void ForgetMisses()
    {
        lock (_loads)
            foreach (var key in _loads.Where(pair => pair.Value.IsCompleted && pair.Value.Result is null).Select(pair => pair.Key).ToArray())
                _loads.Remove(key);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _catalog.Changed -= ForgetMisses;
        _lifetime.Cancel();
        List<Task<Bitmap?>> loads;
        lock (_loads) { loads = [.. _loads.Values]; _loads.Clear(); }
        foreach (var load in loads) if (load.IsCompletedSuccessfully) load.Result?.Dispose();
        _lifetime.Dispose();
    }
}
