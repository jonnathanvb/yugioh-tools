using System.Buffers.Binary;

namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Codificador BMP minimalista (24bpp não-comprimido). Usado pra serializar
/// os thumbnails 40×32 extraídos do ROM em uma string <c>data:image/bmp</c>
/// que pode ser jogada direto num <c>&lt;img&gt;</c>.
///
/// Por que não PNG? PNG exigiria SkiaSharp/ImageSharp. BMP 24bpp é três linhas
/// de cabeçalho, cabe inteiro nesse arquivo, e o navegador renderiza sem
/// problema. O custo é o tamanho — pra 40×32 é trivial (~3.9 KB cru).
/// </summary>
public static class BmpEncoder
{
    /// <summary>Atalho: cria a data URL chamando <see cref="RawBytes"/>
    /// e codificando em base64.</summary>
    public static string ToDataUrl(ReadOnlySpan<byte> bgrTopDown, int width, int height)
        => "data:image/bmp;base64," + Convert.ToBase64String(RawBytes(bgrTopDown, width, height));

    /// <summary>
    /// Devolve os bytes brutos de um arquivo BMP 24bpp (pra escrever em
    /// disco). Mesma lógica de <see cref="ToDataUrl"/>, sem o encoding
    /// final em base64.
    /// </summary>
    public static byte[] RawBytes(ReadOnlySpan<byte> bgrTopDown, int width, int height)
    {
        // BMP exige cada linha alinhada a 4 bytes.
        int rowStride = (width * 3 + 3) & ~3;
        int pixelDataSize = rowStride * height;
        const int fileHeaderSize = 14;
        const int dibHeaderSize  = 40;
        int pixelOffset = fileHeaderSize + dibHeaderSize;
        int fileSize    = pixelOffset + pixelDataSize;

        var buf = new byte[fileSize];

        // ── BITMAPFILEHEADER ────────────────────────────────────────────
        buf[0] = (byte)'B';
        buf[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(2),  fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(6),  0);            // reservado
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(10), pixelOffset);

        // ── BITMAPINFOHEADER ────────────────────────────────────────────
        // Layout (offsets a partir do início do arquivo, header file = 14):
        //  14 size(4) · 18 width(4) · 22 height(4) · 26 planes(2) · 28 bpp(2)
        //  30 compression(4) · 34 sizeImage(4) · 38 xPpm(4) · 42 yPpm(4)
        //  46 clrUsed(4) · 50 clrImportant(4)
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(14), dibHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(22), height);       // positiva = bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(26), 1);            // planes
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(28), 24);           // bpp
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(30), 0);            // BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(34), pixelDataSize);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(38), 2835);         // ~72 dpi
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(42), 2835);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(46), 0);            // colors used
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(50), 0);            // important

        // ── Pixel data: BMP é bottom-up, então invertemos as linhas ─────
        for (int y = 0; y < height; y++)
        {
            int srcRow = (height - 1 - y) * width * 3;
            int dstRow = pixelOffset + y * rowStride;
            bgrTopDown.Slice(srcRow, width * 3).CopyTo(buf.AsSpan(dstRow));
            // Padding já é zero (default do array).
        }

        return buf;
    }

    /// <summary>
    /// Versão 32bpp com canal ALPHA (BGRA top-down). Pra sprites
    /// extraídos do ROM onde o índice 0 da CLUT representa pixel
    /// transparente (alpha bit do RGB555 = 0). Browsers respeitam o
    /// alpha do BMP 32bpp, então o fundo preto do sprite simplesmente
    /// some sobre o frame.
    /// </summary>
    public static byte[] RawBytes32(ReadOnlySpan<byte> bgraTopDown, int width, int height)
    {
        // 32bpp já é alinhado a 4 bytes; sem padding por linha.
        int rowStride     = width * 4;
        int pixelDataSize = rowStride * height;
        const int fileHeaderSize = 14;
        // Usamos BITMAPV4HEADER (108 bytes) pra declarar masks RGBA
        // explícitos — sem isso, alguns visualizadores ignoram o alpha.
        const int dibHeaderSize  = 108;
        int pixelOffset = fileHeaderSize + dibHeaderSize;
        int fileSize    = pixelOffset + pixelDataSize;

        var buf = new byte[fileSize];

        buf[0] = (byte)'B';
        buf[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(2),  fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(10), pixelOffset);

        // BITMAPV4HEADER
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(14), dibHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(26), 1);            // planes
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(28), 32);           // bpp
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(30), 3);            // BI_BITFIELDS
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(34), pixelDataSize);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(38), 2835);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(42), 2835);
        // Color masks (RGBA): obrigatório com BI_BITFIELDS
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(54), 0x00FF0000);  // R
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(58), 0x0000FF00);  // G
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(62), 0x000000FF);  // B
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(66), 0xFF000000);  // A
        // ColorSpace = LCS_sRGB
        buf[70] = (byte)' '; buf[71] = (byte)'s'; buf[72] = (byte)'R'; buf[73] = (byte)'G';
        buf[74] = (byte)'B'; buf[75] = 0;         buf[76] = 0;         buf[77] = 0;

        // Pixel data: bottom-up. BMP 32bpp lê BGRA (B, G, R, A) por pixel.
        for (int y = 0; y < height; y++)
        {
            int srcRow = (height - 1 - y) * rowStride;
            int dstRow = pixelOffset + y * rowStride;
            bgraTopDown.Slice(srcRow, rowStride).CopyTo(buf.AsSpan(dstRow));
        }

        return buf;
    }
}
