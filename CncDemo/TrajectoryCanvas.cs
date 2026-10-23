namespace CncDemo;

// Отдельный публичный элемент доступен конструктору Visual Studio.
// Сам маршрут рисует MainForm.PaintPath через событие Paint.
public class TrajectoryCanvas : Panel
{
    public TrajectoryCanvas()
    {
        DoubleBuffered = true; // Буферизация уменьшает мерцание при движении инструмента.
        ResizeRedraw = true; // При изменении размера нужно пересчитать масштаб и перерисовать поле.
    }
}
