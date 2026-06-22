using System.Windows.Controls;

namespace SCLOCVerse.Interfaces
{
    public interface ICanvasManager
    {
        Canvas? GetCurrentVisibleCanvas();
        void SwitchCanvas(Canvas showCanvas, double durationSeconds = 0.3);
        void ShowCanvas(string which);
    }
}
