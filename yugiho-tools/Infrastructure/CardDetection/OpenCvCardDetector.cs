using System.Runtime.InteropServices;
using OpenCvSharp;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Infrastructure.ScreenCapture;
using OcvSize  = OpenCvSharp.Size;
using OcvPoint = OpenCvSharp.Point;

namespace yugiho_tools.Infrastructure.CardDetection;

/// <summary>
/// Port of getCardsInImage() using OpenCvSharp4 template matching.
/// </summary>
public class OpenCvCardDetector : ICardDetector
{
    private const double MatchThreshold = 0.75;
    private const int TargetWidth  = 320;
    private const int TargetHeight = 240;
    private const int DedupRadius  = 10;
    private const int ThumbWidth   = 40;
    private const int ThumbHeight  = 32;

    public IReadOnlyList<int> DetectCards(byte[] frameBytes, IReadOnlyList<Card> cards)
    {
        var (data, width, height, stride) = Win32Helper.DecodeFrame(frameBytes);

        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        using var srcMat = Mat.FromPixelData(height, width, MatType.CV_8UC3, handle.AddrOfPinnedObject(), stride);
        handle.Free();

        using var resized = new Mat();
        Cv2.Resize(srcMat, resized, new OcvSize(TargetWidth, TargetHeight), interpolation: InterpolationFlags.Area);
        using var gray = new Mat();
        Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

        var matched = new List<int>();

        foreach (var card in cards)
        {
            if (card.ThumbnailPixels is null) continue;

            using var tmpl = new Mat(ThumbHeight, ThumbWidth, MatType.CV_8UC1);
            tmpl.SetArray(card.ThumbnailPixels);

            using var result = new Mat();
            Cv2.MatchTemplate(gray, tmpl, result, TemplateMatchModes.CCoeffNormed);

            var accepted = new List<OcvPoint>();
            for (int row = 0; row < result.Rows; row++)
            {
                for (int col = 0; col < result.Cols; col++)
                {
                    if (result.At<float>(row, col) < MatchThreshold) continue;

                    var pt = new OcvPoint(col, row);
                    bool near = accepted.Any(a =>
                        Math.Abs(a.X - pt.X) < DedupRadius &&
                        Math.Abs(a.Y - pt.Y) < DedupRadius);

                    if (!near)
                    {
                        accepted.Add(pt);
                        matched.Add(card.CardId - 1);
                    }
                }
            }
        }

        return matched;
    }
}
