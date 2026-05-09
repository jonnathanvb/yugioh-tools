using System.Buffers.Binary;

namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Decoder do "espelho" da carta extraído do <c>WA_MRG.MRG</c>. O frame
/// 144×264 não está armazenado linearmente: o jogo guarda um body 128×928
/// em 8 bpp paletted (offset 0xB63000), e uma paleta de 256 cores RGB555
/// em 0xB84000 (com variantes por <see cref="ColorIndex"/>). A imagem
/// visível é montada copiando 6 retângulos do body para o canvas conforme
/// o array <see cref="CardTransform1"/> — engenharia reversa do tool
/// <c>cardsleeves</c> do basededatostea (módulo 5457).
/// </summary>
public static class CardFrameDecoder
{
    /// <summary>Dimensões do canvas: usamos 144×200 (frente sem área de
    /// descrição) em vez de 144×264. A área de descrição (linhas 200..264)
    /// no jogo é onde fica o texto da carta — sem texto, ela parece um
    /// "segundo sprite colado", ruim visualmente. ATK/DEF/atributo/level
    /// estão todos no top 200, então não perdemos nada relevante.</summary>
    public const int CanvasWidth     = 144;
    public const int CanvasHeight    = 200;
    public const int BytesWidth      = 128;
    public const int BytesHeight     = 928;
    public const int BodyBaseOffset  = 0xB63000;
    public const int PaletteOffset   = 0xB84000;
    public const int PaletteStride   = 512;   // 256 cores × 2 bytes
    public const int NumColors       = 256;

    /// <summary>
    /// Cycles definidos pelo jogo. Cada cycle é uma variante completa de
    /// frame (body + paletas) — 10 ao todo. Provavelmente mapeiam pra
    /// categorias de carta (monstro normal, spell, trap, ritual, …).
    /// </summary>
    public static readonly int[] Cycles =
    {
        0, 481280, 962560, 1443840, 1925120,
        2406400, 2887680, 3684352, 4409344, 5658624
    };

    /// <summary>
    /// Definição dos 6 retângulos que compõem a imagem 144×264 a partir
    /// do body 128×928 (cardTransform1 — id "d" no módulo JS 5457).
    /// </summary>
    /// <summary>
    /// Mesmos retângulos do <c>cardBg1</c> do editor cardsleeves, que cobrem
    /// EXATAMENTE 144×200 — frente da carta sem descrição. Vem do módulo JS
    /// 5457 (export "a"). Cortamos o chunk extra de descrição (200..264).
    /// </summary>
    public static readonly Chunk[] CardTransform1 =
    {
        // Painel principal (corpo da carta, 128×192)
        new(BodyX: 0,  BodyY: 0,   CanvasX: 0,   CanvasY: 0,   Width: 128, Height: 192),
        // Faixa lateral direita superior (16×128)
        new(BodyX: 0,  BodyY: 256, CanvasX: 128, CanvasY: 0,   Width: 16,  Height: 128),
        // Faixa lateral direita inferior (16×72)
        new(BodyX: 32, BodyY: 256, CanvasX: 128, CanvasY: 128, Width: 16,  Height: 72),
        // Borda inferior esquerda (64×8)
        new(BodyX: 64, BodyY: 272, CanvasX: 0,   CanvasY: 192, Width: 64,  Height: 8),
        // Borda inferior direita (64×8)
        new(BodyX: 64, BodyY: 280, CanvasX: 64,  CanvasY: 192, Width: 64,  Height: 8),
    };

    public readonly record struct Chunk(
        int BodyX, int BodyY, int CanvasX, int CanvasY, int Width, int Height);

    /// <summary>
    /// Decodifica UM frame e devolve sua data URL (BMP base64).
    /// </summary>
    /// <param name="mrg">Conteúdo bruto de WA_MRG.MRG.</param>
    /// <param name="cycle">Índice em <see cref="Cycles"/> (0..9).</param>
    /// <param name="colorIndex">Variante de cor (0..6).</param>
    /// <returns>Data URL ou null se offsets estourarem.</returns>
    public static string? Decode(ReadOnlySpan<byte> mrg, int cycle, int colorIndex)
    {
        if (cycle < 0 || cycle >= Cycles.Length) return null;
        if (colorIndex < 0 || colorIndex > 6)    return null;

        int cycleOff = Cycles[cycle];
        // Hack do próprio jogo: o último cycle precisa de -16384 no body.
        int bodyAdjust   = cycle == Cycles.Length - 1 ? -16384 : 0;
        int bodyOffset   = BodyBaseOffset + cycleOff + bodyAdjust;
        int paletteStart = PaletteOffset  + cycleOff + colorIndex * PaletteStride;

        if (bodyOffset < 0
         || paletteStart < 0
         || paletteStart + PaletteStride > mrg.Length
         || bodyOffset + BytesWidth * BytesHeight > mrg.Length)
        {
            return null;
        }

        // Pré-processa paleta (256 cores RGB555 + bit de alpha).
        // Aproveita pra detectar a cor "transparente": pixel de índice 0
        // costuma ser tratado como vazio. Marcamos com magenta pra debug
        // visual — o composer real desenha a arte por cima.
        var rgba = new (byte R, byte G, byte B, byte A)[NumColors];
        for (int i = 0; i < NumColors; i++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(
                mrg.Slice(paletteStart + i * 2, 2));
            byte r = (byte)((word & 31) * 8);
            byte g = (byte)((word >> 5  & 31) * 8);
            byte b = (byte)((word >> 10 & 31) * 8);
            byte a = (word == 0) ? (byte)0 : (byte)255;
            rgba[i] = (r, g, b, a);
        }

        // Canvas BGR top-down. Inicializa com magenta-debug (será 100%
        // sobrescrito se cobertura dos chunks for total).
        var bgr = new byte[CanvasWidth * CanvasHeight * 3];
        for (int i = 0; i < bgr.Length; i += 3)
        {
            bgr[i + 0] = 0;     // B
            bgr[i + 1] = 0;     // G
            bgr[i + 2] = 0;     // R
        }

        foreach (var c in CardTransform1)
        {
            for (int y = 0; y < c.Height; y++)
            {
                int srcY = c.BodyY + y;
                int dstY = c.CanvasY + y;
                if (srcY < 0 || srcY >= BytesHeight || dstY < 0 || dstY >= CanvasHeight) continue;

                int srcRow = bodyOffset + srcY * BytesWidth;
                int dstRow = dstY * CanvasWidth * 3;

                for (int x = 0; x < c.Width; x++)
                {
                    int srcX = c.BodyX + x;
                    int dstX = c.CanvasX + x;
                    if (srcX < 0 || srcX >= BytesWidth || dstX < 0 || dstX >= CanvasWidth) continue;

                    byte idx = mrg[srcRow + srcX];
                    var (r, g, b, _) = rgba[idx];
                    int o = dstRow + dstX * 3;
                    bgr[o + 0] = b;
                    bgr[o + 1] = g;
                    bgr[o + 2] = r;
                }
            }
        }

        return BmpEncoder.ToDataUrl(bgr, CanvasWidth, CanvasHeight);
    }

    /// <summary>
    /// Decodifica a coleção de frames disponíveis para o jogador escolher
    /// dentre eles. Retorna mapa <c>(cycle, color) → dataUrl</c>.
    /// </summary>
    public static Dictionary<(int Cycle, int Color), string> DecodeAll(ReadOnlySpan<byte> mrg)
    {
        var result = new Dictionary<(int, int), string>();
        for (int cy = 0; cy < Cycles.Length; cy++)
        {
            for (int co = 0; co < 7; co++)
            {
                var url = Decode(mrg, cy, co);
                if (url is not null) result[(cy, co)] = url;
            }
        }
        return result;
    }
}
