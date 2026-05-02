using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace yugiho_tools.Infrastructure.Parsing;

public class RomParser : IRomParser
{
    private const int CardCount = 722;

    private const int OffsetCardAttribs = 0x1C4A44;
    private const int OffsetLevelAttr   = 0x1C5B33;
    private const int OffsetNamePtrs    = 0x1C6002;
    private const int OffsetNameTable   = 0x1C6800;
    private const int NamePtrBase       = 0x6000;
    private const int OffsetDescPtrs    = 0x1B0A02;
    private const int OffsetDescTable   = 0x1B11F4;
    private const int DescPtrBase       = 0x9F4;

    private const int OffsetFusions      = 0xB87800;
    private const int FusionBlockSize    = 0x10000;
    private const int OffsetThumbnails   = 0x16BAE0;
    private const int ThumbnailBlockSize = 14336;
    private const int ThumbWidth         = 40;
    private const int ThumbHeight        = 32;
    private const int ThumbPixelCount    = ThumbWidth * ThumbHeight;
    private const int ClutSize           = 256 * 2;

    public async Task<IReadOnlyList<Card>> ParseAsync(
        string gameFilePath,
        string mrgFilePath,
        IProgress<int>? progress = null)
    {
        byte[] game = await File.ReadAllBytesAsync(gameFilePath);
        byte[] mrg  = await File.ReadAllBytesAsync(mrgFilePath);
        progress?.Report(10);

        string[] charList = await LoadCharTableAsync(gameFilePath);
        progress?.Report(15);

        var cards = ParseCardData(game, mrg, charList);
        progress?.Report(30);

        return cards;
    }

    public async Task LoadThumbnailsAsync(
        IReadOnlyList<Card> cards,
        string mrgFilePath,
        IProgress<int>? progress = null)
    {
        byte[] mrg = await File.ReadAllBytesAsync(mrgFilePath);
        ParseThumbnails(mrg, cards, progress);
        progress?.Report(100);
    }

    private static string ReadName(ReadOnlySpan<byte> game, int addr, string[] charList)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            byte bt = game[addr + i];
            if (bt == 254)       sb.Append("\r\n");
            else if (bt == 255)  break;
            else if (bt < charList.Length) sb.Append(charList[bt]);
        }
        return sb.ToString();
    }

    private static List<Card> ParseCardData(byte[] game, byte[] mrg, string[] charList)
    {
        var cards = new List<Card>(CardCount);
        for (int i = 0; i < CardCount; i++) cards.Add(new Card());

        int addr = OffsetCardAttribs;
        for (int i = 0; i < CardCount; i++)
        {
            uint d = BinaryPrimitives.ReadUInt32LittleEndian(game.AsSpan(addr, 4));
            addr += 4;
            cards[i].CardId        = i + 1;
            cards[i].Attack        = (int)(d & 0x1FF) * 10;
            cards[i].Defense       = (int)(d >> 9  & 0x1FF) * 10;
            cards[i].GuardianStar1 = (int)(d >> 18 & 0xF);
            cards[i].GuardianStar2 = (int)(d >> 22 & 0xF);
            cards[i].CardType      = (int)(d >> 26 & 0x1F);
        }

        addr = OffsetLevelAttr;
        for (int i = 0; i < CardCount; i++)
        {
            byte b = game[addr++];
            cards[i].Level     = b & 0xF;
            cards[i].Attribute = (b >> 4) & 0xF;
        }

        for (int i = 0; i < CardCount; i++)
        {
            int num  = BinaryPrimitives.ReadUInt16LittleEndian(game.AsSpan(OffsetNamePtrs + i * 2, 2));
            cards[i].Name = ReadName(game, OffsetNameTable + num - NamePtrBase, charList);
        }

        for (int i = 0; i < CardCount; i++)
        {
            int off  = BinaryPrimitives.ReadUInt16LittleEndian(game.AsSpan(OffsetDescPtrs + i * 2, 2));
            cards[i].Description = ReadName(game, OffsetDescTable + (off - DescPtrBase), charList);
        }

        ParseFusions(mrg, cards);
        return cards;
    }

    private static void ParseFusions(byte[] mrg, List<Card> cards)
    {
        var fuseDat = mrg.AsSpan(OffsetFusions, FusionBlockSize);

        for (int i = 0; i < CardCount; i++)
        {
            int position = i * 2 + 2;
            int num = BinaryPrimitives.ReadUInt16LittleEndian(fuseDat.Slice(position, 2));
            position = num & 0xFFFF;
            if (position == 0) continue;

            int fusionAmt = fuseDat[position++];
            if (fusionAmt == 0) fusionAmt = 511 - fuseDat[position++];

            int num2 = fusionAmt;
            while (num2 > 0)
            {
                byte b0 = fuseDat[position], b1 = fuseDat[position + 1],
                     b2 = fuseDat[position + 2], b3 = fuseDat[position + 3],
                     b4 = fuseDat[position + 4];
                position += 5;

                cards[i].FusionMaterials.Add(((b0 & 3) << 8 | b1) - 1);
                cards[i].FusionResults  .Add(((b0 >> 2 & 3) << 8 | b2) - 1);
                num2--;
                if (num2 <= 0) continue;

                cards[i].FusionMaterials.Add(((b0 >> 4 & 3) << 8 | b3) - 1);
                cards[i].FusionResults  .Add(((b0 >> 6 & 3) << 8 | b4) - 1);
                num2--;
            }
            cards[i].FusionCount = cards[i].FusionMaterials.Count;
        }
    }

    private static void ParseThumbnails(byte[] mrg, IReadOnlyList<Card> cards, IProgress<int>? progress)
    {
        for (int i = 0; i < CardCount; i++)
        {
            if (i % 100 == 0) progress?.Report(30 + i / 10);

            int pixelStart = OffsetThumbnails + i * ThumbnailBlockSize;
            var pixels = mrg.AsSpan(pixelStart, ThumbPixelCount);
            var clut   = mrg.AsSpan(pixelStart + ThumbPixelCount, ClutSize);

            var gray = new byte[ThumbPixelCount];
            var bgra = new byte[ThumbPixelCount * 4];
            for (int p = 0; p < ThumbPixelCount; p++)
            {
                ushort color = BinaryPrimitives.ReadUInt16LittleEndian(clut.Slice(pixels[p] * 2, 2));
                byte r = (byte)((color & 31) * 8);
                byte g = (byte)((color >> 5 & 31) * 8);
                byte b = (byte)((color >> 10 & 31) * 8);
                gray[p] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);

                int o = p * 4;
                bgra[o + 0] = b;
                bgra[o + 1] = g;
                bgra[o + 2] = r;
                bgra[o + 3] = 255;
            }
            cards[i].ThumbnailPixels  = gray;
            cards[i].ThumbnailDataUrl = BgraToPngDataUrl(bgra, ThumbWidth, ThumbHeight);
        }
    }

    private static string BgraToPngDataUrl(byte[] bgra, int width, int height)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride == width * 4)
            {
                Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
            }
            else
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(bgra, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private static async Task<string[]> LoadCharTableAsync(string gameFilePath)
    {
        // Look next to the game file first, then next to the executable
        string dir  = Path.GetDirectoryName(gameFilePath) ?? "";
        string path = Path.Combine(dir, "chartable.tbl");
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "chartable.tbl");

        var charList = new string[256];
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && int.TryParse(parts[0],
                System.Globalization.NumberStyles.HexNumber, null, out int idx))
                charList[idx] = parts[1];
        }
        return charList;
    }
}
