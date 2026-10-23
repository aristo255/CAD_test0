using System.Globalization;
using System.Text.RegularExpressions;

namespace CncDemo;

// Неизменяемые координаты инструмента в миллиметрах. Значения копируются при присваивании.
public readonly record struct Position(double X, double Y, double Z);
// Один прямолинейный участок: начало, конец, подача, признак G0 и номер исходной строки.
public readonly record struct Move(Position From, Position To, double Feed, bool Rapid, int Line);

public static class GCode
{
    // Разбираем только учебное подмножество G-кода. Незнакомую команду отклоняем,
    // чтобы пользователь не получил правдоподобную, но неверную траекторию.
    // Результат — список отрезков; при ошибке выбрасывается FormatException.
    public static List<Move> Parse(string source)
    {
        var result = new List<Move>();
        // Каждый разбор начинается с X0 Y0 Z0, абсолютного режима и быстрого хода.
        var position = new Position();
        bool absolute = true;
        int motion = 0;
        double feed = 300; // Подача G1 по умолчанию, мм/мин; сохраняется до следующего F.
        // Убираем CR, чтобы одинаково обрабатывать переводы строк Windows и Linux.
        var lines = source.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            // Удаляем комментарии после ; и в скобках, разрешаем строчные буквы.
            string code = Regex.Replace(lines[i].Split(';')[0], @"\([^)]*\)", "").Trim().ToUpperInvariant();
            if (code.Length == 0 || code == "%") continue;
            try
            {
                // Слово состоит из буквы и числа, например X-2.5 или G01.
                // Десятичный разделитель — точка независимо от языка Windows.
                var words = Regex.Matches(code, @"([A-Z])\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))");
                // После удаления всех распознанных слов не должно остаться постороннего текста.
                if (Regex.Replace(code, @"([A-Z])\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))", "").Trim().Length != 0)
                    throw new FormatException("Некорректная запись команды");
                // null означает, что ось не задана: её текущая координата сохранится.
                double? x = null, y = null, z = null;
                // Несколько G-слов в строке допустимы; повтор остальных слов считается ошибкой.
                var seen = new HashSet<char>();
                bool end = false;
                foreach (Match word in words)
                {
                    char letter = word.Groups[1].Value[0];
                    double value = double.Parse(word.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (!double.IsFinite(value)) throw new FormatException("Недопустимое число");
                    if (letter != 'G' && !seen.Add(letter)) throw new FormatException("Повтор слова " + letter);
                    switch (letter)
                    {
                        case 'N': break; // Номер кадра не влияет на движение.
                        case 'G':
                            // Режимы модальные: действуют и в следующих строках до изменения.
                            switch (value)
                            {
                                case 0: motion = 0; break;
                                case 1: motion = 1; break;
                                case 21: break; // Все размеры уже в миллиметрах; G20 не поддерживается.
                                case 90: absolute = true; break;
                                case 91: absolute = false; break;
                                default: throw new FormatException("Поддерживаются только G0, G1, G21, G90, G91");
                            }
                            break;
                        case 'F': // Рабочая подача G1; скорость G0 задаётся отдельно.
                            if (value <= 0 || value > 10000) throw new FormatException("Подача: 0 < F ≤ 10000 мм/мин");
                            feed = value; break;
                        case 'X': x = value; break;
                        case 'Y': y = value; break;
                        case 'Z': z = value; break;
                        case 'M':
                            if (value != 2 && value != 30) throw new FormatException("Поддерживаются только M2 и M30");
                            end = true; break;
                        default: throw new FormatException("Неподдерживаемое слово " + letter);
                    }
                }
                // M2/M30 завершают разбор: последующие строки не выполняются.
                if (end && (x.HasValue || y.HasValue || z.HasValue)) throw new FormatException("M2/M30 должны быть без перемещения");
                if (end) break;
                // G90: число — абсолютная координата; G91: приращение к текущей.
                double Axis(double current, double? next)
                {
                    return next.HasValue ? (absolute ? next.Value : current + next.Value) : current;
                }
                var target = new Position(Axis(position.X, x), Axis(position.Y, y), Axis(position.Z, z));
                Validate(target); // Любой выход за рабочую область отменяет разбор всей программы.
                // Строка с одними режимами не создаёт отрезок. Для G0 используем 1500 мм/мин.
                if (x.HasValue || y.HasValue || z.HasValue)
                    result.Add(new Move(position, target, motion == 0 ? 1500 : feed, motion == 0, i + 1));
                position = target;
            }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
                // Номер физической строки помогает найти ошибку в редакторе (отсчёт с 1).
                throw new FormatException($"Строка {i + 1}: {error.Message}");
            }
        }
        if (result.Count == 0) throw new FormatException("Программа не содержит перемещений");
        return result;
    }

    // Общие пределы используются и парсером, и ручным перемещением формы.
    // Отрицательная Z допустима для учебного заглубления в заготовку.
    public static void Validate(Position p)
    {
        if (p.X < 0 || p.X > 300 || p.Y < 0 || p.Y > 200 || p.Z < -50 || p.Z > 100)
            throw new FormatException("Выход за пределы: X 0…300, Y 0…200, Z −50…100 мм");
    }
}
