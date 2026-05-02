namespace yugiho_tools.Domain.Interfaces;

public interface IScreenCapture
{
    IReadOnlyList<string> GetWindowTitles();
    byte[]? CaptureWindow(string windowTitle);
    void SaveFrameToFile(byte[] frame, string path);
}
