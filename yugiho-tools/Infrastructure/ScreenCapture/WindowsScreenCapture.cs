using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using yugiho_tools.Domain.Interfaces;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace yugiho_tools.Infrastructure.ScreenCapture;

public class WindowsScreenCapture : IScreenCapture
{
    public IReadOnlyList<string> GetWindowTitles() => Win32Helper.GetVisibleWindowTitles();

    public byte[]? CaptureWindow(string windowTitle) => Win32Helper.CaptureWindow(windowTitle);

    public void SaveFrameToFile(byte[] frame, string path)
    {
        var (data, width, height, stride) = Win32Helper.DecodeFrame(frame);

        using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, width, height);
        var bd   = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            int rowBytes = Math.Min(Math.Abs(stride), Math.Abs(bd.Stride));
            for (int y = 0; y < height; y++)
                Marshal.Copy(data, y * stride, bd.Scan0 + y * bd.Stride, rowBytes);
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        bmp.Save(path, ImageFormat.Png);
    }
}
