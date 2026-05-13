using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using yugiho_tools.Application.Helpers;
using yugiho_tools.Domain.Interfaces;
using IsPngEncoder = SixLabors.ImageSharp.Formats.Png.PngEncoder;

namespace yugiho_tools.Infrastructure.ScreenCapture;

/// <summary>
/// macOS screen capture via CoreGraphics:
///   - <c>CGWindowListCopyWindowInfo</c> lista janelas visíveis (kCGWindowListOptionOnScreenOnly).
///   - <c>CGWindowListCreateImage</c> rasteriza uma janela específica por ID.
///   - O bitmap é desempacotado em buffer BGRA top-down e empacotado no
///     mesmo formato <see cref="FrameCodec"/> que o Windows usa, pra que
///     <see cref="OpenCvCardDetector"/> consuma sem se importar com plataforma.
///
/// Permissões: na primeira chamada o macOS pede ao usuário pra habilitar
/// "Screen Recording" em System Settings → Privacy &amp; Security. Sem
/// isso a API retorna uma imagem do desktop sem conteúdo de outras janelas.
/// </summary>
[SupportedOSPlatform("maccatalyst")]
public sealed class MacScreenCapture : IScreenCapture
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ImageIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    // kCGWindowListOptionOnScreenOnly = 1, kCGWindowListExcludeDesktopElements = 16
    private const uint ListOnScreenAndExcludeDesktop = 1 | 16;
    private const uint ListOptionIncludingWindow = 1u << 3;

    // CGWindowImageOption flags
    private const uint ImageBoundsIgnoreFraming = 1u << 0;
    private const uint ImageNominalResolution   = 1u << 4;

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGWindowListCreateImage(CGRect screenBounds, uint listOption, uint windowId, uint imageOption);

    [DllImport(CoreGraphics)]
    private static extern void CGImageRelease(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern nuint CGImageGetWidth(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern nuint CGImageGetHeight(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern nuint CGImageGetBytesPerRow(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGImageGetDataProvider(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGDataProviderCopyData(IntPtr provider);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint idx);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);

    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern IntPtr CFStringCreateWithCharacters(IntPtr alloc, string str, nint length);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr str);

    [DllImport(CoreFoundation)]
    private static extern bool CFStringGetCString(IntPtr str, byte[] buffer, nint bufferSize, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern bool CFNumberGetValue(IntPtr number, nint type, out uint value);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X, Y, Width, Height;
        public static readonly CGRect Null = new() { X = double.PositiveInfinity, Y = double.PositiveInfinity, Width = 0, Height = 0 };
    }

    private const uint Utf8Encoding = 0x08000100;
    private const nint CFNumberSInt32Type = 3;

    private static IntPtr CFStr(string s) => CFStringCreateWithCharacters(IntPtr.Zero, s, s.Length);

    public IReadOnlyList<string> GetWindowTitles()
    {
        var titles = new List<string>();
        IntPtr arr = CGWindowListCopyWindowInfo(ListOnScreenAndExcludeDesktop, 0);
        if (arr == IntPtr.Zero) return titles;

        IntPtr keyName  = CFStr("kCGWindowName");
        IntPtr keyOwner = CFStr("kCGWindowOwnerName");
        try
        {
            nint count = CFArrayGetCount(arr);
            var seen = new HashSet<string>();
            for (nint i = 0; i < count; i++)
            {
                IntPtr dict = CFArrayGetValueAtIndex(arr, i);
                string? name  = ReadCFString(CFDictionaryGetValue(dict, keyName));
                string? owner = ReadCFString(CFDictionaryGetValue(dict, keyOwner));
                string? combined = !string.IsNullOrEmpty(name) ? name :
                                   !string.IsNullOrEmpty(owner) ? owner : null;
                if (combined != null && seen.Add(combined))
                    titles.Add(combined);
            }
        }
        finally
        {
            CFRelease(keyName);
            CFRelease(keyOwner);
            CFRelease(arr);
        }
        return titles;
    }

    public byte[]? CaptureWindow(string windowTitle)
    {
        uint windowId = FindWindowId(windowTitle);
        if (windowId == 0) return null;

        IntPtr image = CGWindowListCreateImage(
            CGRect.Null,
            ListOptionIncludingWindow,
            windowId,
            ImageBoundsIgnoreFraming | ImageNominalResolution);
        if (image == IntPtr.Zero) return null;

        try
        {
            int width  = (int)CGImageGetWidth(image);
            int height = (int)CGImageGetHeight(image);
            int strideBgra = (int)CGImageGetBytesPerRow(image);
            if (width <= 0 || height <= 0) return null;

            IntPtr provider = CGImageGetDataProvider(image);
            IntPtr cfData   = CGDataProviderCopyData(provider);
            try
            {
                int totalBytes = (int)CFDataGetLength(cfData);
                IntPtr ptr = CFDataGetBytePtr(cfData);

                // CoreGraphics retorna BGRA premultiplied (kCGImageAlphaPremultipliedFirst little-endian).
                // OpenCvCardDetector espera BGR 24bpp. Converte aqui pra manter o
                // contrato do FrameCodec consistente com o caminho Windows.
                int strideBgr = width * 3;
                byte[] bgr = new byte[strideBgr * height];
                unsafe
                {
                    byte* src = (byte*)ptr;
                    for (int y = 0; y < height; y++)
                    {
                        byte* srcRow = src + y * strideBgra;
                        int dstOffset = y * strideBgr;
                        for (int x = 0; x < width; x++)
                        {
                            int s = x * 4;
                            int d = dstOffset + x * 3;
                            bgr[d + 0] = srcRow[s + 0]; // B
                            bgr[d + 1] = srcRow[s + 1]; // G
                            bgr[d + 2] = srcRow[s + 2]; // R
                        }
                    }
                }
                return FrameCodec.Encode(bgr, width, height, strideBgr);
            }
            finally
            {
                CFRelease(cfData);
            }
        }
        finally
        {
            CGImageRelease(image);
        }
    }

    public void SaveFrameToFile(byte[] frame, string path)
    {
        var (data, width, height, stride) = FrameCodec.Decode(frame);
        using var image = new Image<Rgb24>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var dstRow = accessor.GetRowSpan(y);
                int srcOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int o = srcOffset + x * 3;
                    dstRow[x] = new Rgb24(data[o + 2], data[o + 1], data[o]);
                }
            }
        });
        using var fs = File.Create(path);
        image.Save(fs, new IsPngEncoder());
    }

    private uint FindWindowId(string title)
    {
        IntPtr arr = CGWindowListCopyWindowInfo(ListOnScreenAndExcludeDesktop, 0);
        if (arr == IntPtr.Zero) return 0;

        IntPtr keyName  = CFStr("kCGWindowName");
        IntPtr keyOwner = CFStr("kCGWindowOwnerName");
        IntPtr keyId    = CFStr("kCGWindowNumber");
        try
        {
            nint count = CFArrayGetCount(arr);
            for (nint i = 0; i < count; i++)
            {
                IntPtr dict = CFArrayGetValueAtIndex(arr, i);
                string? name  = ReadCFString(CFDictionaryGetValue(dict, keyName));
                string? owner = ReadCFString(CFDictionaryGetValue(dict, keyOwner));
                bool match = (!string.IsNullOrEmpty(name) && name.Contains(title, StringComparison.OrdinalIgnoreCase))
                          || (!string.IsNullOrEmpty(owner) && owner.Contains(title, StringComparison.OrdinalIgnoreCase));
                if (!match) continue;

                IntPtr idVal = CFDictionaryGetValue(dict, keyId);
                if (idVal != IntPtr.Zero && CFNumberGetValue(idVal, CFNumberSInt32Type, out uint id))
                    return id;
            }
        }
        finally
        {
            CFRelease(keyName);
            CFRelease(keyOwner);
            CFRelease(keyId);
            CFRelease(arr);
        }
        return 0;
    }

    private static string? ReadCFString(IntPtr cfStr)
    {
        if (cfStr == IntPtr.Zero) return null;
        nint len = CFStringGetLength(cfStr);
        if (len <= 0) return null;
        // UTF-8 pode usar até 4 bytes/char + null terminator.
        var buf = new byte[len * 4 + 1];
        if (!CFStringGetCString(cfStr, buf, buf.Length, Utf8Encoding)) return null;
        int actualLen = Array.IndexOf<byte>(buf, 0);
        if (actualLen < 0) actualLen = buf.Length;
        return System.Text.Encoding.UTF8.GetString(buf, 0, actualLen);
    }
}
