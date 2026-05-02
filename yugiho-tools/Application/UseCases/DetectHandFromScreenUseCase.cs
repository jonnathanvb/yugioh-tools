using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Application.UseCases;

public class DetectHandFromScreenUseCase(IScreenCapture screenCapture, ICardDetector cardDetector)
{
    private static readonly string LogDir =
        Path.Combine(AppContext.BaseDirectory, "logs");

    public IReadOnlyList<int>? Execute(string windowTitle, IReadOnlyList<Card> cards)
    {
        byte[]? frame = screenCapture.CaptureWindow(windowTitle);
        if (frame is null) return null;

        SaveCaptureLog(frame);
        return cardDetector.DetectCards(frame, cards);
    }

    private void SaveCaptureLog(byte[] frame)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            string path = Path.Combine(
                LogDir,
                $"capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            screenCapture.SaveFrameToFile(frame, path);
        }
        catch
        {
            // Logging failure must never break detection
        }
    }
}
