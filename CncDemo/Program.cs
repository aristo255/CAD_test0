namespace CncDemo;

internal static class Program
{
    // WinForms и стандартные диалоги Windows требуют однопоточной модели COM (STA).
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize(); // Настройка шрифтов, DPI и оформления .NET.
        // Создаём главное окно и запускаем цикл обработки оконных сообщений.
        Application.Run(new MainForm());
    }
}
