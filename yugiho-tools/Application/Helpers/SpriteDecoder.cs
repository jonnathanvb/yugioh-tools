using System.Buffers.Binary;

namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Decoder de sprites 4bpp paletted do FM. Os ícones de tipo, guardião,
/// atributo etc. são armazenados num "sprite sheet" no MRG, em 4-bit-per-pixel
/// (cada byte contém 2 pixels), com paleta CLUT de 16 cores (RGB555 little-endian).
///
/// Os sprites de tipo (24) + guardião (13) compartilham um sheet em
/// <c>0xB50000</c> (32 KB), com CLUTs em <c>0xB60200 + 32 * spriteIndex</c>.
/// Atributos (9) ficam em <c>0xF02800</c>, com CLUTs em <c>0xF08600 + 32*i</c>.
/// </summary>
public static class SpriteDecoder
{
    /// <summary>
    /// Extrai um sprite de tamanho <paramref name="spriteSize"/>×<paramref name="spriteSize"/>
    /// do sheet 4bpp em <paramref name="bodyOffset"/>, posição
    /// (<paramref name="x"/>, <paramref name="y"/>), usando a CLUT em
    /// <paramref name="clutOffset"/>. Retorna BMP em data URL pronto pra
    /// gravar em disco ou usar em &lt;img src=…&gt;.
    /// </summary>
    /// <param name="mrg">Conteúdo bruto do WA_MRG.MRG.</param>
    /// <param name="bodyOffset">Endereço do início do sprite sheet.</param>
    /// <param name="sheetWidth">Largura do sheet em pixels (geralmente 128).</param>
    /// <param name="x">X do sprite no sheet (em pixels).</param>
    /// <param name="y">Y do sprite no sheet (em pixels).</param>
    /// <param name="spriteSize">Lado do sprite em pixels (16 pra ícones).</param>
    /// <param name="clutOffset">Endereço da CLUT (16 cores × 2 bytes = 32 bytes).</param>
    public static byte[]? ExtractSprite(
        ReadOnlySpan<byte> mrg,
        int bodyOffset, int sheetWidth,
        int x, int y, int spriteSize,
        int clutOffset)
    {
        if (clutOffset < 0 || clutOffset + 32 > mrg.Length) return null;

        // RGB555 → BGRA. Pixel com word=0 (CLUT slot zerado) = transparente.
        Span<byte> palR = stackalloc byte[16];
        Span<byte> palG = stackalloc byte[16];
        Span<byte> palB = stackalloc byte[16];
        Span<byte> palA = stackalloc byte[16];
        for (int i = 0; i < 16; i++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(mrg.Slice(clutOffset + i * 2, 2));
            palR[i] = (byte)((word & 31) * 8);
            palG[i] = (byte)((word >> 5  & 31) * 8);
            palB[i] = (byte)((word >> 10 & 31) * 8);
            // Convenção PSX TIM (mesmo critério do lab): pixel cuja
            // RGB decodificada é (0,0,0) é TRANSPARENTE — ignora o bit
            // STP/alpha do RGB555. Mods que setam bit alpha mas mantêm
            // RGB zerado (ex: word 0x8000) também caem aqui, evitando
            // o fundo preto opaco que aparecia em alguns ROMs.
            palA[i] = (palR[i] == 0 && palG[i] == 0 && palB[i] == 0)
                      ? (byte)0 : (byte)255;
        }

        int bytesPerRow = sheetWidth / 2;
        var bgra = new byte[spriteSize * spriteSize * 4];

        for (int dy = 0; dy < spriteSize; dy++)
        {
            int rowStart = bodyOffset + (y + dy) * bytesPerRow;
            for (int dx = 0; dx < spriteSize; dx++)
            {
                int srcX = x + dx;
                int byteIdx = rowStart + (srcX >> 1);
                if (byteIdx < 0 || byteIdx >= mrg.Length) continue;
                byte b = mrg[byteIdx];
                int idx = (srcX & 1) == 0 ? (b & 0x0F) : (b >> 4);

                int o = (dy * spriteSize + dx) * 4;
                bgra[o + 0] = palB[idx];
                bgra[o + 1] = palG[idx];
                bgra[o + 2] = palR[idx];
                bgra[o + 3] = palA[idx];
            }
        }

        return PngEncoder.RawBytes32(bgra, spriteSize, spriteSize);
    }

    /// <summary>
    /// Sobrecarga retangular: extrai sprite W×H de um sheet 4bpp na
    /// posição (<paramref name="x"/>, <paramref name="y"/>). Útil pros
    /// nomes de carta (96×14).
    /// </summary>
    public static byte[]? ExtractRect4bpp(
        ReadOnlySpan<byte> mrg,
        int bodyOffset, int sheetWidth,
        int x, int y, int spriteWidth, int spriteHeight,
        int clutOffset, int clutColors = 16)
    {
        if (clutOffset < 0 || clutOffset + clutColors * 2 > mrg.Length) return null;

        Span<byte> palR = stackalloc byte[256];
        Span<byte> palG = stackalloc byte[256];
        Span<byte> palB = stackalloc byte[256];
        Span<byte> palA = stackalloc byte[256];
        for (int i = 0; i < clutColors; i++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(mrg.Slice(clutOffset + i * 2, 2));
            palR[i] = (byte)((word & 31) * 8);
            palG[i] = (byte)((word >> 5  & 31) * 8);
            palB[i] = (byte)((word >> 10 & 31) * 8);
            // Convenção PSX TIM (mesmo critério do lab): pixel cuja
            // RGB decodificada é (0,0,0) é TRANSPARENTE — ignora o bit
            // STP/alpha do RGB555. Mods que setam bit alpha mas mantêm
            // RGB zerado (ex: word 0x8000) também caem aqui, evitando
            // o fundo preto opaco que aparecia em alguns ROMs.
            palA[i] = (palR[i] == 0 && palG[i] == 0 && palB[i] == 0)
                      ? (byte)0 : (byte)255;
        }

        int bytesPerRow = sheetWidth / 2;
        var bgra = new byte[spriteWidth * spriteHeight * 4];

        for (int dy = 0; dy < spriteHeight; dy++)
        {
            int rowStart = bodyOffset + (y + dy) * bytesPerRow;
            for (int dx = 0; dx < spriteWidth; dx++)
            {
                int srcX = x + dx;
                int byteIdx = rowStart + (srcX >> 1);
                if (byteIdx < 0 || byteIdx >= mrg.Length) continue;
                byte b = mrg[byteIdx];
                int idx = (srcX & 1) == 0 ? (b & 0x0F) : (b >> 4);
                if (idx >= clutColors) idx = 0;

                int o = (dy * spriteWidth + dx) * 4;
                bgra[o + 0] = palB[idx];
                bgra[o + 1] = palG[idx];
                bgra[o + 2] = palR[idx];
                bgra[o + 3] = palA[idx];
            }
        }

        return PngEncoder.RawBytes32(bgra, spriteWidth, spriteHeight);
    }

    /// <summary>
    /// Decoder 8bpp paletted de um sprite "auto-contido" (cada sprite tem
    /// seu próprio body + CLUT). Usado pros portraits dos duelistas
    /// (48×48 com CLUT de 64 cores).
    /// </summary>
    public static byte[]? Extract8bppPaletted(
        ReadOnlySpan<byte> mrg,
        int bodyOffset, int width, int height,
        int clutOffset, int clutColors)
    {
        int pixelCount = width * height;
        if (bodyOffset < 0 || bodyOffset + pixelCount > mrg.Length) return null;
        if (clutOffset < 0 || clutOffset + clutColors * 2 > mrg.Length) return null;

        Span<byte> palR = stackalloc byte[256];
        Span<byte> palG = stackalloc byte[256];
        Span<byte> palB = stackalloc byte[256];
        Span<byte> palA = stackalloc byte[256];
        for (int i = 0; i < clutColors; i++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(mrg.Slice(clutOffset + i * 2, 2));
            palR[i] = (byte)((word & 31) * 8);
            palG[i] = (byte)((word >> 5  & 31) * 8);
            palB[i] = (byte)((word >> 10 & 31) * 8);
            // Convenção PSX TIM (mesmo critério do lab): pixel cuja
            // RGB decodificada é (0,0,0) é TRANSPARENTE — ignora o bit
            // STP/alpha do RGB555. Mods que setam bit alpha mas mantêm
            // RGB zerado (ex: word 0x8000) também caem aqui, evitando
            // o fundo preto opaco que aparecia em alguns ROMs.
            palA[i] = (palR[i] == 0 && palG[i] == 0 && palB[i] == 0)
                      ? (byte)0 : (byte)255;
        }

        var bgra = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            byte idx = mrg[bodyOffset + i];
            if (idx >= clutColors) idx = 0;
            int o = i * 4;
            bgra[o + 0] = palB[idx];
            bgra[o + 1] = palG[idx];
            bgra[o + 2] = palR[idx];
            bgra[o + 3] = palA[idx];
        }

        return PngEncoder.RawBytes32(bgra, width, height);
    }
}
