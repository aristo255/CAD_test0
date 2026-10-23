using CncDemo;
// Небольшой исполняемый набор проверок без внешней тестовой библиотеки.
// Исключение завершает процесс с ошибкой; сообщение Passed печатается только после всех проверок.
int count = 0;
void Check(bool pass) { if (!pass) throw new Exception("Failed check " + (count + 1)); count++; }
// Встроенный прямоугольник: проверяем число отрезков, конечную точку и подачи G0/G1.
var moves = GCode.Parse("G21 G90\nG0 Z10\nG0 X30 Y30\nG1 Z-2 F120\nG1 X180 F600\nG1 Y130\nG1 X30\nG1 Y30\nG0 Z10\nM30");
Check(moves.Count == 8);
Check(moves[^1].To == new Position(30,30,10));
Check(moves[2].Feed == 120 && !moves[2].Rapid);
Check(moves[0].Feed == 1500 && moves[0].Rapid);
// G91 должен прибавить смещение к предыдущей абсолютной точке.
Check(GCode.Parse("G1 X10 Y20\nG91\nX5 Y-2")[^1].To == new Position(15,18,0));
// Комментарии игнорируются, а M2 запрещает обработку последующего X20.
Check(GCode.Parse("(test) G1 X10 ;comment\nM2\nX20").Count == 1);
// Ошибочные программы: дуга, пределы осей, подача, дюймы, повторы,
// посторонний текст, перемещение вместе с M30 и отсутствие перемещений.
foreach (string code in new[]{"G2 X10", "G1 X301", "G1 Z-51", "G1 X-1", "G1 F0 X10", "G20", "G1 X1 X2", "garbage", "M30 X10", ""})
{
    bool rejected = false;
    try { GCode.Parse(code); } catch (FormatException) { rejected = true; }
    Check(rejected);
}
Console.WriteLine($"Passed {count} parser checks");
