using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace CncDemo;

// Одна форма разделена на две части: здесь поведение, в Designer — элементы интерфейса.
public partial class MainForm : Form
{
    // Учебная обработка прямоугольника: подъём, подход, заглубление, контур и отвод.
    const string Demo = "G21 G90\r\nG0 Z10\r\nG0 X30 Y30\r\nG1 Z-2 F120\r\nG1 X180 F600\r\nG1 Y130\r\nG1 X30\r\nG1 Y30\r\nG0 Z10\r\nM30";
    // Таймер вызывает Tick, а Stopwatch измеряет фактическое время между вызовами.
    readonly Stopwatch clock = new();
    // Эти кнопки блокируются при выполнении, паузе и аварии.
    readonly List<Button> idleButtons = new();
    // Подготовленная траектория и текущее положение инструмента в миллиметрах.
    List<Move> moves = new();
    Position position;
    int index; // Индекс выполняемого отрезка (нумерация с нуля).
    double distance; // Пройденное расстояние внутри текущего отрезка, мм.
    // Пауза сохраняет прогресс; авария запрещает новый запуск до снятия блокировки.
    bool running, paused, emergency;

    public MainForm()
    {
        InitializeComponent(); // Создание элементов и подключение событий из Designer.
        editor.Text = Demo;
        idleButtons.AddRange(new[] { openButton, exampleButton, previewButton, homeButton,
            xMinusButton, xPlusButton, yMinusButton, yPlusButton, zMinusButton, zPlusButton });
        UpdateUi("Готово. Проверьте программу или нажмите «Пуск».");
    }

    // Единая точка обновления координат, сообщения, доступности кнопок и рисунка.
    void UpdateUi(string message)
    {
        coordinates.Text = FormattableString.Invariant($"X {position.X,8:F2}   Y {position.Y,8:F2}   Z {position.Z,8:F2} мм");
        status.Text = message;
        // Во время выполнения нельзя менять программу, для которой рассчитаны moves.
        editor.ReadOnly = running || paused || emergency;
        foreach (var button in idleButtons) button.Enabled = !running && !paused && !emergency;
        run.Enabled = !running && !emergency; pause.Enabled = running;
        canvas.Invalidate(); // Запрос перерисовки: Windows позднее вызовет PaintPath.
    }
    // При ошибке показываем её пользователю и запрещаем переход к запуску.
    bool Parse()
    {
        try { moves = GCode.Parse(editor.Text); return true; }
        catch (FormatException e) { MessageBox.Show(this, e.Message, "Ошибка G-кода", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }
    // Проверка строит всю траекторию, но не запускает таймер движения.
    private void Preview()
    {
        if (Parse())
        {
            UpdateUi($"Проверено: {moves.Count} перемещений. XY — вид сверху; начало программы — X0 Y0 Z0.");
        }
    }
    // Новый запуск начинается с нуля; продолжение после паузы сохраняет index и distance.
    void Start()
    {
        if (!paused)
        {
            if (!Parse()) return;
            position = default; index = 0; distance = 0;
        }
        paused = false; running = true; clock.Restart(); timer.Start(); UpdateUi("Выполнение программы");
    }
    // Останавливаем оба источника времени: длительность паузы не должна стать движением.
    private void Pause()
    {
        timer.Stop();
        clock.Stop();
        running = false;
        paused = true;
        UpdateUi("Пауза. «Пуск» — продолжить.");
    }
    // Полный сброс обнуляет прогресс и координаты, но не снимает аварийную блокировку.
    void Reset()
    {
        timer.Stop(); clock.Reset(); running = paused = false; index = 0; distance = 0; position = default; moves.Clear();
        UpdateUi(emergency ? "Аварийная блокировка. Нажмите «Снять аварию»." : "Сброс. Координаты обнулены.");
    }
    // Авария немедленно останавливает симуляцию, оставляя инструмент на текущем месте.
    void Emergency()
    {
        timer.Stop(); clock.Stop(); running = paused = false; emergency = true;
        UpdateUi("АВАРИЯ: движение остановлено. Для нового запуска снимите аварию.");
    }
    // Ручной шаг по одной оси. sign задаёт направление: -1 или +1.
    void Jog(string axis, int sign)
    {
        var delta = (double)step.Value * sign;
        // with создаёт копию Position с изменённой осью; остальные координаты сохраняются.
        var next = axis switch { "X" => position with { X = position.X + delta }, "Y" => position with { Y = position.Y + delta }, _ => position with { Z = position.Z + delta } };
        // Сначала проверяем пределы и только затем меняем положение инструмента.
        try { GCode.Validate(next); position = next; moves.Clear(); UpdateUi("Ручное перемещение"); }
        catch (FormatException e) { UpdateUi(e.Message); }
    }
    // Отмена диалога сохраняет текущую программу; ошибки чтения показываются в окне.
    void LoadCode()
    {
        using var dialog = new OpenFileDialog { Filter = "G-код|*.nc;*.tap;*.gcode;*.txt|Все файлы|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { string text = File.ReadAllText(dialog.FileName); Reset(); editor.Text = text; Preview(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show(this, e.Message, "Ошибка чтения"); }
    }
    // Один шаг симуляции в UI-потоке. Здесь нет связи с контроллером реального станка.
    private void Tick(object? sender, EventArgs e)
    {
        // Ограничение 0,2 с предотвращает большой скачок при задержке оконного интерфейса.
        double seconds = Math.Min(clock.Elapsed.TotalSeconds, 0.2); clock.Restart();
        var move = moves[index];
        double dx = move.To.X - move.From.X, dy = move.To.Y - move.From.Y, dz = move.To.Z - move.From.Z;
        // Длина отрезка учитывает все три оси, даже если на рисунке видна только XY.
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        // F задано в мм/мин: деление на 60 даёт мм/с, умножение на время — путь.
        distance += seconds * move.Feed / 60;
        // t — доля пройденного отрезка от 0 до 1. Нулевой отрезок завершается сразу.
        double t = length == 0 ? 1 : Math.Min(1, distance / length);
        // Линейная интерполяция: начало + доля пути × разность координат.
        position = new Position(move.From.X + dx * t, move.From.Y + dy * t, move.From.Z + dz * t);
        // За один Tick завершается не более одного отрезка; остаток времени не переносится.
        if (t >= 1) { index++; distance = 0; }
        if (index == moves.Count)
        {
            timer.Stop(); running = false; UpdateUi("Программа завершена."); return;
        }
        UpdateUi($"Строка {moves[index].Line} • перемещение {index + 1}/{moves.Count} • F {moves[index].Feed:F0} мм/мин");
    }
    // Рисуем всю запланированную траекторию сверху; Z отображается только в координатах.
    private void PaintPath(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        // Единый масштаб по обеим осям сохраняет пропорции рабочего поля 300 × 200 мм.
        float scale = Math.Max(0.01f, Math.Min((canvas.Width - 70) / 300f, (canvas.Height - 70) / 200f));
        // Экранная Y растёт вниз, поэтому вычитаем её из высоты панели. Отступ — 35 px.
        PointF Map(Position p)
        {
            return new PointF(35 + (float)p.X * scale, canvas.Height - 35 - (float)p.Y * scale);
        }
        // using освобождает графические ресурсы после завершения отрисовки.
        using var grid = new Pen(Color.FromArgb(48, 60, 76));
        for (int x = 0; x <= 300; x += 25) g.DrawLine(grid, Map(new(x, 0, 0)), Map(new(x, 200, 0)));
        for (int y = 0; y <= 200; y += 25) g.DrawLine(grid, Map(new(0, y, 0)), Map(new(300, y, 0)));
        // Пунктир — быстрый ход G0, сплошная линия — рабочий ход G1.
        using var rapid = new Pen(Color.Goldenrod, 1) { DashStyle = DashStyle.Dash };
        using var cut = new Pen(Color.DeepSkyBlue, 2);
        foreach (var move in moves) g.DrawLine(move.Rapid ? rapid : cut, Map(move.From), Map(move.To));
        var p = Map(position);
        g.FillEllipse(Brushes.LimeGreen, p.X - 5, p.Y - 5, 10, 10);
        g.DrawString("XY: 300 × 200 мм • синий: G1 • пунктир: G0 • зелёный: инструмент", Font, Brushes.WhiteSmoke, 8, 8);
    }
    // Обработчики связаны с Click в Designer и вызывают методы логики выше.
    // Обычные тела { ... } сохранены для перехода двойным щелчком из конструктора.
    // «Открыть G-код»: выбрать файл и проверить его содержимое.
    private void OpenButton_Click(object? sender, EventArgs e)
    { LoadCode(); }

    // «Пример»: вернуть учебную программу и сбросить текущий прогресс.
    private void ExampleButton_Click(object? sender, EventArgs e)
    { editor.Text = Demo; Reset(); }

    // «Проверить»: разобрать текст и показать маршрут без движения.
    private void PreviewButton_Click(object? sender, EventArgs e)
    { Preview(); }

    // «Пуск»: начать программу или продолжить её после паузы.
    private void Run_Click(object? sender, EventArgs e)
    { Start(); }

    // «Пауза»: сохранить положение и текущий отрезок.
    private void Pause_Click(object? sender, EventArgs e)
    { Pause(); }

    // «Стоп / сброс»: остановить выполнение и обнулить координаты.
    private void ResetButton_Click(object? sender, EventArgs e)
    { Reset(); }

    // «АВАРИЙНЫЙ СТОП»: остановить движение с блокировкой управления.
    private void EmergencyButton_Click(object? sender, EventArgs e)
    { Emergency(); }

    // «Снять аварию»: убрать блокировку и подготовить новый запуск с нуля.
    private void ClearEmergencyButton_Click(object? sender, EventArgs e)
    { emergency = false; Reset(); }

    // «В ноль»: мгновенное обнуление в симуляторе, а не реальный поиск концевиков.
    private void HomeButton_Click(object? sender, EventArgs e)
    {
        position = default;
        moves.Clear();
        UpdateUi("Возврат в ноль (симуляция)");
    }
    // Шесть кнопок ручного перемещения используют выбранный в step шаг.
    private void XMinusButton_Click(object? sender, EventArgs e)
    { Jog("X", -1); }

    private void XPlusButton_Click(object? sender, EventArgs e)
    { Jog("X", 1); }

    private void YMinusButton_Click(object? sender, EventArgs e)
    { Jog("Y", -1); }

    private void YPlusButton_Click(object? sender, EventArgs e)
    { Jog("Y", 1); }

    private void ZMinusButton_Click(object? sender, EventArgs e)
    { Jog("Z", -1); }

    private void ZPlusButton_Click(object? sender, EventArgs e)
    { Jog("Z", 1); }

    // Timer освобождается контейнером components в Dispose формы; здесь выключаем секундомер.
    private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
    { clock.Stop(); }

}
