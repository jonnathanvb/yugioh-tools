using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using IsPngEncoder = SixLabors.ImageSharp.Formats.Png.PngEncoder;

namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Codificador PNG cross-platform (Windows + macOS via MacCatalyst) usando
/// SixLabors.ImageSharp. Substitui o impl anterior em System.Drawing/GDI+
/// que dependia de libgdiplus em plataformas não-Windows.
///
/// Entrada esperada: buffers BGR (24bpp) ou BGRA (32bpp) <strong>top-down</strong>,
/// ou seja, primeira linha = topo da imagem. ImageSharp usa pixel format
/// canônico RGBA (não BGRA) — convertemos durante o load row-by-row.
/// </summary>
public static class PngEncoder
{
    public static string ToDataUrl24(ReadOnlySpan<byte> bgrTopDown, int width, int height)
        => "data:image/png;base64," + Convert.ToBase64String(RawBytes24(bgrTopDown, width, height));

    public static string ToDataUrl32(ReadOnlySpan<byte> bgraTopDown, int width, int height)
        => "data:image/png;base64," + Convert.ToBase64String(RawBytes32(bgraTopDown, width, height));

    /// <summary>BGR top-down (3 bytes/pixel) → PNG sem alpha.</summary>
    public static byte[] RawBytes24(ReadOnlySpan<byte> bgrTopDown, int width, int height)
    {
        // Span não pode ser capturado em lambda — copia para array antes.
        var src = bgrTopDown.ToArray();
        using var image = new Image<Rgb24>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            int rowLen = width * 3;
            for (int y = 0; y < accessor.Height; y++)
            {
                var dstRow = accessor.GetRowSpan(y);
                int srcOffset = y * rowLen;
                for (int x = 0; x < width; x++)
                {
                    int o = srcOffset + x * 3;
                    // ImageSharp Rgb24 = (R, G, B); fonte é BGR.
                    dstRow[x] = new Rgb24(src[o + 2], src[o + 1], src[o]);
                }
            }
        });
        return EncodePng(image);
    }

    /// <summary>BGRA top-down (4 bytes/pixel) → PNG com alpha.</summary>
    public static byte[] RawBytes32(ReadOnlySpan<byte> bgraTopDown, int width, int height)
    {
        var src = bgraTopDown.ToArray();
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            int rowLen = width * 4;
            for (int y = 0; y < accessor.Height; y++)
            {
                var dstRow = accessor.GetRowSpan(y);
                int srcOffset = y * rowLen;
                for (int x = 0; x < width; x++)
                {
                    int o = srcOffset + x * 4;
                    // ImageSharp Rgba32 = (R, G, B, A); fonte é BGRA.
                    dstRow[x] = new Rgba32(src[o + 2], src[o + 1], src[o], src[o + 3]);
                }
            }
        });
        return EncodePng(image);
    }

    private static byte[] EncodePng<TPixel>(Image<TPixel> image) where TPixel : unmanaged, IPixel<TPixel>
    {
        using var ms = new MemoryStream();
        image.Save(ms, new IsPngEncoder());
        return ms.ToArray();
    }
}
