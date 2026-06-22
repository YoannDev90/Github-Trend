using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;
using SkiaSharp;
using Svg.Skia;

namespace Github_Trend.Controls;

public class SvgImage : Image
{
    private static readonly ConcurrentDictionary<string, WeakReference<Bitmap>> SvgCache = new();

    public static readonly StyledProperty<string?> SourceUriProperty = AvaloniaProperty.Register<
        SvgImage,
        string?
    >(nameof(SourceUri));

    static SvgImage()
    {
        SourceUriProperty.Changed.AddClassHandler<SvgImage>((s, e) => s.OnSourceChanged(e));
    }

    public string? SourceUri
    {
        get => GetValue(SourceUriProperty);
        set => SetValue(SourceUriProperty, value);
    }

    private async void OnSourceChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string uri || string.IsNullOrWhiteSpace(uri))
            return;

        try
        {
            if (SvgCache.TryGetValue(uri, out var weakRef) && weakRef.TryGetTarget(out var cached))
            {
                Source = cached;
                return;
            }

            var bitmap = await RenderSvgToBitmapAsync(
                uri,
                (int)(Width > 0 ? Width : 18),
                (int)(Height > 0 ? Height : 18)
            );
            if (bitmap != null)
            {
                Source = bitmap;
                SvgCache[uri] = new WeakReference<Bitmap>(bitmap);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SVG source change failed: {Uri}", uri);
        }
    }

    private static async Task<Bitmap?> RenderSvgToBitmapAsync(
        string uri,
        int targetWidth,
        int targetHeight
    )
    {
        return await Task.Run(() =>
        {
            try
            {
                using var stream = OpenSvgStream(uri);
                if (stream == null)
                    return null;

                var svg = new SKSvg();
                var picture = svg.Load(stream);
                if (picture == null)
                    return null;

                var bounds = picture.CullRect;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    return null;

                var scale = Math.Min(targetWidth / bounds.Width, targetHeight / bounds.Height);
                if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0)
                    scale = 1f;

                var bmpWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
                var bmpHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));

                using var bitmap = new SKBitmap(
                    bmpWidth,
                    bmpHeight,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul
                );
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(scale);

                using var paint = new SKPaint
                {
                    ColorFilter = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.SrcIn),
                    IsAntialias = true,
                };
                canvas.DrawPicture(picture, paint);
                canvas.Flush();

                using var img = SKImage.FromBitmap(bitmap);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                using var ms = data.AsStream();
                ms.Position = 0;
                return new Bitmap(ms);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SVG render failed: {Uri}", uri);
                return null;
            }
        });
    }

    private static Stream? OpenSvgStream(string uri)
    {
        try
        {
            if (uri.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                var assetUri = new Uri(uri);
                if (AssetLoader.Exists(assetUri))
                    return AssetLoader.Open(assetUri);

                var fileName = Path.GetFileName(assetUri.AbsolutePath);
                var localPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", fileName);
                if (File.Exists(localPath))
                    return File.OpenRead(localPath);

                return null;
            }

            if (File.Exists(uri))
                return File.OpenRead(uri);

            return null;
        }
        catch
        {
            return null;
        }
    }
}
