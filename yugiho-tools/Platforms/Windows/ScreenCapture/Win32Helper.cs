using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using yugiho_tools.Application.Helpers;

namespace yugiho_tools.Infrastructure.ScreenCapture;

[SupportedOSPlatform("windows")]
internal static class Win32Helper
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);
    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? lpClassName, string lpWindowName);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public static IReadOnlyList<string> GetVisibleWindowTitles()
    {
        var titles = new List<string>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new System.Text.StringBuilder(256);
            if (GetWindowText(hWnd, sb, sb.Capacity) > 0 && sb.Length > 0)
                titles.Add(sb.ToString());
            return true;
        }, nint.Zero);
        return titles;
    }

    public static byte[]? CaptureWindow(string windowTitle)
    {
        nint hWnd = FindWindow(null, windowTitle);
        if (hWnd == nint.Zero) return null;

        SetForegroundWindow(hWnd);
        Thread.Sleep(150);

        if (!GetWindowRect(hWnd, out RECT rect)) return null;

        int width  = rect.Right  - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;

        using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g   = Graphics.FromImage(bmp);
        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);

        var bmpData = bmp.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            int stride = bmpData.Stride;
            byte[] bytes = new byte[Math.Abs(stride) * height];
            Marshal.Copy(bmpData.Scan0, bytes, 0, bytes.Length);
            return FrameCodec.Encode(bytes, width, height, stride);
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }
    }
}
