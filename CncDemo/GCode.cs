using System.Globalization;
using System.Text.RegularExpressions;

namespace CncDemo;

public readonly record struct Position(double X, double Y, double Z);
public readonly record struct Move(Position From, Position To, double Feed, bool Rapid, int Line);

public static class GCode
{
    // Deliberately strict subset: reject unsupported commands instead of simulating them incorrectly.
    public static List<Move> Parse(string source)
    {
        var result = new List<Move>();
        var position = new Position();
        bool absolute = true;
        int motion = 0;
        double feed = 300;
        var lines = source.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string code = Regex.Replace(lines[i].Split(';')[0], @"\([^)]*\)", "").Trim().ToUpperInvariant();
            if (code.Length == 0 || code == "%") continue;
            try
            {
                var words = Regex.Matches(code, @"([A-Z])\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))");
                if (Regex.Replace(code, @"([A-Z])\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+))", "").Trim().Length != 0)
                    throw new FormatException("Некорректная запись команды");
                double? x = null, y = null, z = null;
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
                        case 'N': break;
                        case 'G':
                            switch (value)
                            {
                                case 0: motion = 0; break;
                                case 1: motion = 1; break;
                                case 21: break;
                                case 90: absolute = true; break;
                                case 91: absolute = false; break;
                                default: throw new FormatException("Поддерживаются только G0, G1, G21, G90, G91");
                            }
                            break;
                        case 'F':
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
                if (end && (x.HasValue || y.HasValue || z.HasValue)) throw new FormatException("M2/M30 должны быть без перемещения");
                if (end) break;
                double Axis(double current, double? next) => next.HasValue ? (absolute ? next.Value : current + next.Value) : current;
                var target = new Position(Axis(position.X, x), Axis(position.Y, y), Axis(position.Z, z));
                Validate(target);
                if (x.HasValue || y.HasValue || z.HasValue)
                    result.Add(new Move(position, target, motion == 0 ? 1500 : feed, motion == 0, i + 1));
                position = target;
            }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
                throw new FormatException($"Строка {i + 1}: {error.Message}");
            }
        }
        if (result.Count == 0) throw new FormatException("Программа не содержит перемещений");
        return result;
    }

    public static void Validate(Position p)
    {
        if (p.X < 0 || p.X > 300 || p.Y < 0 || p.Y > 200 || p.Z < -50 || p.Z > 100)
            throw new FormatException("Выход за пределы: X 0…300, Y 0…200, Z −50…100 мм");
    }
}
