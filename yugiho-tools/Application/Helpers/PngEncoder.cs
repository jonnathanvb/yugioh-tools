using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Codificador PNG sobre <see cref="Bitmap"/> do GDI+. Substitui o
/// <c>BmpEncoder</c> antigo: gera arquivos ~5-10× menores (compressão
/// deflate), suporta canal alpha de forma universal (sem precisar de
/// <c>mix-blend-mode</c> no CSS pra esconder fundo preto) e é entendido
/// por qualquer ferramenta de imagem.
///
/// Entrada esperada: buffers BGR (24bpp) ou BGRA (32bpp) <strong>top-down</strong>,
/// ou seja, primeira linha = topo da imagem. <c>Format32bppArgb</c> e
/// <c>Format24bppRgb</c> do GDI+ armazenam pixels em memória little-endian
/// como (B, G, R, A) e (B, G, R) respectivamente — então o memcpy é direto.
///
/// O projeto já é Windows-only (<c>net10.0-windows10.0.19041.0</c>),
/// portanto a dependência de <c>System.Drawing.Common</c> não restringe
/// nada que já não estivesse restrito.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PngEncoder
{
    public static string ToDataUrl24(ReadOnlySpan<byte> bgrTopDown, int width, int height)
        => "data:image/png;base64," + Convert.ToBase64String(RawBytes24(bgrTopDown, width, height));

    public static string ToDataUrl32(ReadOnlySpan<byte> bgraTopDown, int width, int height)
        => "data:image/png;base64," + Convert.ToBase64String(RawBytes32(bgraTopDown, width, height));

    /// <summary>BGR top-down (3 bytes/pixel) → PNG sem alpha.</summary>
    public static byte[] RawBytes24(ReadOnlySpan<byte> bgrTopDown, int width, int height)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            CopyTopDownToBitmap(bgrTopDown, data, bytesPerPixel: 3);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return EncodePng(bmp);
    }

    /// <summary>BGRA top-down (4 bytes/pixel) → PNG com alpha.</summary>
    public static byte[] RawBytes32(ReadOnlySpan<byte> bgraTopDown, int width, int height)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            CopyTopDownToBitmap(bgraTopDown, data, bytesPerPixel: 4);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return EncodePng(bmp);
    }

    /// <summary>Copia linha-a-linha respeitando o <c>Stride</c> que o GDI+
    /// alinha a 4 bytes — sem isso, larguras não-múltiplas de 4 ficam com
    /// linhas deslocadas e a imagem sai "escorregando".</summary>
    private static void CopyTopDownToBitmap(ReadOnlySpan<byte> source, BitmapData target, int bytesPerPixel)
    {
        int width   = target.Width;
        int height  = target.Height;
        int rowLen  = width * bytesPerPixel;
        int stride  = target.Stride;
        nint scan0  = target.Scan0;

        // Buffer temporário só se stride != rowLen (precisa zerar padding).
        // Quando coincidem (32bpp em qualquer largura, 24bpp em largura
        // múltipla de 4), copia direto sem alocar.
        if (stride == rowLen)
        {
            Marshal.Copy(source.ToArray(), 0, scan0, rowLen * height);
            return;
        }

        var rowBuf = new byte[rowLen];
        for (int y = 0; y < height; y++)
        {
            source.Slice(y * rowLen, rowLen).CopyTo(rowBuf);
            Marshal.Copy(rowBuf, 0, scan0 + y * stride, rowLen);
        }
    }

    private static byte[] EncodePng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
