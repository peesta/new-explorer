using System.Globalization;

namespace CanvasExplorer.Services;

public static class Format
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    static readonly string[] Units = { "КБ", "МБ", "ГБ", "ТБ" };

    public static string Size(long bytes)
    {
        if (bytes < 1024) return bytes + " Б";
        double v = bytes;
        var i = -1;
        do { v /= 1024; i++; } while (Math.Round(v) >= 1024 && i < Units.Length - 1);
        return v.ToString(Math.Round(v, 1) < 10 ? "0.#" : "0", Ru) + " " + Units[i];
    }

    public static string ShortDate(DateTime d)
    {
        var today = DateTime.Today;
        if (d.Date == today) return d.ToString("HH:mm", Ru);
        if (d.Date == today.AddDays(-1)) return "Вчера";
        return d.ToString(d.Year == today.Year ? "dd.MM" : "dd.MM.yy", Ru);
    }

    public static string LongDate(DateTime d) => d.ToString("d MMMM yyyy, HH:mm", Ru);

    public static string Plural(int n, string one, string few, string many)
    {
        var m10 = n % 10;
        var m100 = n % 100;
        var word = m10 == 1 && m100 != 11 ? one : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? few : many;
        return n + " " + word;
    }
}
