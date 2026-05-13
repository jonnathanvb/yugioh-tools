namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Serialização in-memory dos frames capturados da tela.
/// Formato: [int width][int height][int stride][raw BGR bytes].
/// Cross-platform — independe de Win32 ou CoreGraphics.
/// </summary>
public static class FrameCodec
{
    public static byte[] Encode(byte[] raw, int width, int height, int stride)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(width);
        bw.Write(height);
        bw.Write(stride);
        bw.Write(raw);
        return ms.ToArray();
    }

    public static (byte[] data, int width, int height, int stride) Decode(byte[] frame)
    {
        using var ms = new MemoryStream(frame);
        using var br = new BinaryReader(ms);
        int width  = br.ReadInt32();
        int height = br.ReadInt32();
        int stride = br.ReadInt32();
        byte[] data = br.ReadBytes((int)(ms.Length - ms.Position));
        return (data, width, height, stride);
    }
}
