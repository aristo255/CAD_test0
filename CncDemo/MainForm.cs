using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace CncDemo;

public sealed class MainForm : Form
{
    const string Demo = "G21 G90\r\nG0 Z10\r\nG0 X30 Y30\r\nG1 Z-2 F120\r\nG1 X180 F600\r\nG1 Y130\r\nG1 X30\r\nG1 Y30\r\nG0 Z10\r\nM30";
    readonly TextBox editor = new() { Multiline = true, ScrollBars = ScrollBars.Both, AcceptsTab = true, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 12), Text = Demo };
    readonly Canvas canvas = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 28, 40) };
    readonly Label coordinates = new() { AutoSize = true, Font = new Font("Consolas", 18, FontStyle.Bold), Padding = new Padding(8) };
    readonly Label status = new() { AutoSize = true, Padding = new Padding(8) };
    readonly NumericUpDown step = new() { Minimum = 1, Maximum = 50, Value = 5, Width = 65 };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 20 };
    readonly Stopwatch clock = new();
    readonly List<Button> idleButtons = new();
    readonly Button run, pause;
    List<Move> moves = new();
    Position position;
    int index;
    double distance;
    bool running, paused, emergency;

    public MainForm()
    {
        Text = "CNC Demo — симулятор ЧПУ XYZ";
        Size = new Size(1200, 780); MinimumSize = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "ДЕМОНСТРАЦИОННЫЙ РЕЖИМ • без подключения к оборудованию • миллиметры", AutoSize = true, Padding = new Padding(8), ForeColor = Color.DarkSlateBlue });
        root.Controls.Add(coordinates);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 365, Size = new Size(1100, 450) };
        split.Panel1.Controls.Add(editor); split.Panel2.Controls.Add(canvas); root.Controls.Add(split);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        root.Controls.Add(controls);
        Button Add(string text, Action action, bool idle = false)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 34, Margin = new Padding(4) };
            button.Click += (_, _) => action(); controls.Controls.Add(button);
            if (idle) idleButtons.Add(button);
            return button;
        }
        Add("Открыть G-код", LoadCode, true);
        Add("Пример", () => { editor.Text = Demo; Reset(); }, true);
        Add("Проверить", Preview, true);
        run = Add("▶ Пуск", Start);
        pause = Add("Ⅱ Пауза", Pause);
        Add("■ Стоп / сброс", Reset);
        var stop = Add("АВАРИЙНЫЙ СТОП", Emergency);
        stop.BackColor = Color.Firebrick; stop.ForeColor = Color.White;
        Add("Снять аварию", () => { emergency = false; Reset(); });
        Add("В ноль", () => { position = default; moves.Clear(); UpdateUi("Возврат в ноль (симуляция)"); }, true);
        controls.Controls.Add(new Label { Text = "Шаг, мм:", AutoSize = true, Margin = new Padding(8, 12, 0, 0) }); controls.Controls.Add(step);
        foreach (var axis in new[] { "X", "Y", "Z" })
            foreach (int sign in new[] { -1, 1 })
            {
                string selected = axis; int direction = sign;
                Add(axis + (sign > 0 ? "+" : "−"), () => Jog(selected, direction), true);
            }
        root.Controls.Add(status);
        canvas.Paint += PaintPath;
        timer.Tick += Tick;
        FormClosed += (_, _) => { timer.Dispose(); clock.Stop(); };
        UpdateUi("Готово. Проверьте программу или нажмите «Пуск».");
    }

    void UpdateUi(string message)
    {
        coordinates.Text = FormattableString.Invariant($"X {position.X,8:F2}   Y {position.Y,8:F2}   Z {position.Z,8:F2} мм");
        status.Text = message;
        editor.ReadOnly = running || paused || emergency;
        foreach (var button in idleButtons) button.Enabled = !running && !paused && !emergency;
        run.Enabled = !running && !emergency; pause.Enabled = running;
        canvas.Invalidate();
    }
    bool Parse()
    {
        try { moves = GCode.Parse(editor.Text); return true; }
        catch (FormatException e) { MessageBox.Show(this, e.Message, "Ошибка G-кода", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }
    void Preview() { if (Parse()) UpdateUi($"Проверено: {moves.Count} перемещений. XY — вид сверху; начало программы — X0 Y0 Z0."); }
    void Start()
    {
        if (!paused)
        {
            if (!Parse()) return;
            position = default; index = 0; distance = 0;
        }
        paused = false; running = true; clock.Restart(); timer.Start(); UpdateUi("Выполнение программы");
    }
    void Pause() { timer.Stop(); clock.Stop(); running = false; paused = true; UpdateUi("Пауза. «Пуск» — продолжить."); }
    void Reset()
    {
        timer.Stop(); clock.Reset(); running = paused = false; index = 0; distance = 0; position = default; moves.Clear();
        UpdateUi(emergency ? "Аварийная блокировка. Нажмите «Снять аварию»." : "Сброс. Координаты обнулены.");
    }
    void Emergency()
    {
        timer.Stop(); clock.Stop(); running = paused = false; emergency = true;
        UpdateUi("АВАРИЯ: движение остановлено. Для нового запуска снимите аварию.");
    }
    void Jog(string axis, int sign)
    {
        var delta = (double)step.Value * sign;
        var next = axis switch { "X" => position with { X = position.X + delta }, "Y" => position with { Y = position.Y + delta }, _ => position with { Z = position.Z + delta } };
        try { GCode.Validate(next); position = next; moves.Clear(); UpdateUi("Ручное перемещение"); }
        catch (FormatException e) { UpdateUi(e.Message); }
    }
    void LoadCode()
    {
        using var dialog = new OpenFileDialog { Filter = "G-код|*.nc;*.tap;*.gcode;*.txt|Все файлы|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { string text = File.ReadAllText(dialog.FileName); Reset(); editor.Text = text; Preview(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show(this, e.Message, "Ошибка чтения"); }
    }
    void Tick(object? sender, EventArgs e)
    {
        double seconds = Math.Min(clock.Elapsed.TotalSeconds, 0.2); clock.Restart();
        var move = moves[index];
        double dx = move.To.X - move.From.X, dy = move.To.Y - move.From.Y, dz = move.To.Z - move.From.Z;
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        distance += seconds * move.Feed / 60;
        double t = length == 0 ? 1 : Math.Min(1, distance / length);
        position = new Position(move.From.X + dx * t, move.From.Y + dy * t, move.From.Z + dz * t);
        if (t >= 1) { index++; distance = 0; }
        if (index == moves.Count)
        {
            timer.Stop(); running = false; UpdateUi("Программа завершена."); return;
        }
        UpdateUi($"Строка {moves[index].Line} • перемещение {index + 1}/{moves.Count} • F {moves[index].Feed:F0} мм/мин");
    }
    void PaintPath(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Max(0.01f, Math.Min((canvas.Width - 70) / 300f, (canvas.Height - 70) / 200f));
        PointF Map(Position p) => new(35 + (float)p.X * scale, canvas.Height - 35 - (float)p.Y * scale);
        using var grid = new Pen(Color.FromArgb(48, 60, 76));
        for (int x = 0; x <= 300; x += 25) g.DrawLine(grid, Map(new(x, 0, 0)), Map(new(x, 200, 0)));
        for (int y = 0; y <= 200; y += 25) g.DrawLine(grid, Map(new(0, y, 0)), Map(new(300, y, 0)));
        using var rapid = new Pen(Color.Goldenrod, 1) { DashStyle = DashStyle.Dash };
        using var cut = new Pen(Color.DeepSkyBlue, 2);
        foreach (var move in moves) g.DrawLine(move.Rapid ? rapid : cut, Map(move.From), Map(move.To));
        var p = Map(position);
        g.FillEllipse(Brushes.LimeGreen, p.X - 5, p.Y - 5, 10, 10);
        g.DrawString("XY: 300 × 200 мм • синий: G1 • пунктир: G0 • зелёный: инструмент", Font, Brushes.WhiteSmoke, 8, 8);
    }
    sealed class Canvas : Panel { public Canvas() { DoubleBuffered = true; ResizeRedraw = true; } }
}
