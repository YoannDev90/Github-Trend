using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Serilog;

namespace Github_Trend.Controls;

public class SvgImage : Image
{
    public static readonly StyledProperty<string?> SourceUriProperty =
        AvaloniaProperty.Register<SvgImage, string?>(nameof(SourceUri));

    private static readonly ConcurrentDictionary<string, byte> MissingLogged = new();
    private static readonly ConcurrentDictionary<string, byte> RenderErrorLogged = new();

    public string? SourceUri
    {
        get => GetValue(SourceUriProperty);
        set => SetValue(SourceUriProperty, value);
    }

    static SvgImage()
    {
        SourceUriProperty.Changed.AddClassHandler<SvgImage>((s, e) => s.OnSourceChanged(e));
    }

    private async void OnSourceChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string uri || string.IsNullOrWhiteSpace(uri))
            return;

        try
        {
            var bitmap = await RenderSvgToBitmapAsync(uri, (int)(Width > 0 ? Width : 18), (int)(Height > 0 ? Height : 18));
            if (bitmap != null)
            {
                Source = bitmap;
                return;
            }

            // Log une seule fois par URI pour éviter le spam massif dans la liste.
            if (MissingLogged.TryAdd(uri, 0))
                Log.Warning("SvgImage: rendu null pour {Uri}", uri);
        }
        catch (Exception ex)
        {
            if (RenderErrorLogged.TryAdd(uri, 0))
                Log.Error(ex, "SvgImage: exception OnSourceChanged pour {Uri}", uri);
        }
    }

    private static async Task<Bitmap?> RenderSvgToBitmapAsync(string uri, int targetWidth, int targetHeight)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var stream = OpenSvgStream(uri);
                if (stream == null)
                {
                    if (MissingLogged.TryAdd(uri, 0))
                        Log.Warning("SvgImage: introuvable {Uri}", uri);
                    return null;
                }

                var svg = new Svg.Skia.SKSvg();
                var picture = svg.Load(stream);
                if (picture == null)
                {
                    if (RenderErrorLogged.TryAdd(uri, 0))
                        Log.Warning("SvgImage: échec de parsing SVG {Uri}", uri);
                    return null;
                }

                var bounds = picture.CullRect;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    if (RenderErrorLogged.TryAdd(uri + "#bounds", 0))
                        Log.Warning("SvgImage: dimensions invalides pour {Uri}", uri);
                    return null;
                }

                var scale = Math.Min(targetWidth / bounds.Width, targetHeight / bounds.Height);
                if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0)
                    scale = 1f;

                var bmpWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
                var bmpHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));

                using var bitmap = new SKBitmap(bmpWidth, bmpHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(scale);
                canvas.DrawPicture(picture);
                canvas.Flush();

                using var img = SKImage.FromBitmap(bitmap);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                using var ms = data.AsStream();
                ms.Position = 0;
                return new Bitmap(ms);
            }
            catch (Exception ex)
            {
                if (RenderErrorLogged.TryAdd(uri, 0))
                    Log.Error(ex, "SvgImage: erreur de rendu SVG {Uri}", uri);
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

                // fallback: si jamais l'asset n'est pas packé, tentative fichier direct
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
