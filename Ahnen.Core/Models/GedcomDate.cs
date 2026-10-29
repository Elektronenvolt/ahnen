using System.Globalization;
using System.Text.RegularExpressions;

namespace Ahnen.Core.Models;

/// <summary>
/// Parses and formats GEDCOM 7 date values (subset: exact/partial dates with
/// optional ABT/BEF/AFT qualifier). The canonical form is stored in the JSON
/// file, e.g. "12 APR 1950", "APR 1950", "1950", "ABT 1950", "BEF 12 APR 1950".
/// </summary>
public static partial class GedcomDate
{
    private static readonly string[] Months =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    [GeneratedRegex(@"^(?:(?<q>ABT|BEF|AFT)\s+)?(?:(?:(?<d>\d{1,2})\s+)?(?<m>[A-Z]{3})\s+)?(?<y>\d{1,4})$")]
    private static partial Regex CanonicalRegex();

    [GeneratedRegex(@"^(~|about|abt\.?|ca\.?|circa|<|before|bef\.?|vor|>|after|aft\.?|nach)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex QualifierRegex();

    [GeneratedRegex(@"^(\d{4})-(\d{1,2})(?:-(\d{1,2}))?$")]
    private static partial Regex IsoRegex();

    [GeneratedRegex(@"^(?:(\d{1,2})[./])?(\d{1,2})[./](\d{3,4})$")]
    private static partial Regex EuropeanRegex();

    [GeneratedRegex(@"^\d{3,4}$")]
    private static partial Regex YearRegex();

    /// <summary>True when <paramref name="value"/> is a canonical GEDCOM date this app supports.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var m = CanonicalRegex().Match(value.Trim());
        if (!m.Success) return false;
        if (m.Groups["m"].Success && Array.IndexOf(Months, m.Groups["m"].Value) < 0) return false;
        if (m.Groups["d"].Success)
        {
            var day = int.Parse(m.Groups["d"].Value);
            if (day is < 1 or > 31) return false;
        }
        return true;
    }

    /// <summary>
    /// Parses user input into canonical GEDCOM form. Accepts:
    /// "1950", "04/1950", "4.1950", "12.04.1950", "12/04/1950", "1950-04-12" (ISO),
    /// "~1950" / "about 1950" (ABT), "&lt;1950" / "before 1950" (BEF),
    /// "&gt;1950" / "after 1950" (AFT) and already-canonical GEDCOM strings.
    /// Returns null when the input cannot be understood.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();

        var qualifier = "";
        var qm = QualifierRegex().Match(s);
        if (qm.Success)
        {
            var q = qm.Groups[1].Value.ToLowerInvariant().TrimEnd('.');
            qualifier = q switch
            {
                "~" or "about" or "abt" or "ca" or "circa" => "ABT ",
                "<" or "before" or "bef" or "vor" => "BEF ",
                _ => "AFT "
            };
            s = s[qm.Length..].Trim();
        }

        var upper = s.ToUpperInvariant();
        if (IsValid(upper)) return qualifier + upper;

        // ISO: 1950-04-12 or 1950-04
        var iso = IsoRegex().Match(s);
        if (iso.Success)
            return Build(qualifier, iso.Groups[3].Value, iso.Groups[2].Value, iso.Groups[1].Value);

        // European d.m.yyyy / d/m/yyyy, or m.yyyy / m/yyyy
        var eu = EuropeanRegex().Match(s);
        if (eu.Success)
            return Build(qualifier, eu.Groups[1].Value, eu.Groups[2].Value, eu.Groups[3].Value);

        // Year only
        var yr = YearRegex().Match(s);
        if (yr.Success) return qualifier + yr.Value;

        // Culture-aware parsing as a last resort ("12 April 1950", "April 12, 1950")
        foreach (var culture in new[] { CultureInfo.CurrentCulture, CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("de-DE") })
        {
            if (DateTime.TryParse(s, culture, DateTimeStyles.None, out var dt))
                return $"{qualifier}{dt.Day} {Months[dt.Month - 1]} {dt.Year}";
        }

        return null;
    }

    private static string? Build(string qualifier, string day, string month, string year)
    {
        if (!int.TryParse(year, out var y) || y is < 1 or > 9999) return null;
        if (!int.TryParse(month, out var mo) || mo is < 1 or > 12) return null;
        if (string.IsNullOrEmpty(day)) return $"{qualifier}{Months[mo - 1]} {y}";
        if (!int.TryParse(day, out var d) || d is < 1 or > 31) return null;
        return $"{qualifier}{d} {Months[mo - 1]} {y}";
    }

    /// <summary>Extracts the year from a canonical GEDCOM date, for sorting and "life years" display.</summary>
    public static int? Year(string? canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical)) return null;
        var m = CanonicalRegex().Match(canonical.Trim());
        return m.Success ? int.Parse(m.Groups["y"].Value) : null;
    }

    /// <summary>Approximate value (earliest moment the date could denote), for comparisons.</summary>
    public static DateTime? Approximate(string? canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical)) return null;
        var m = CanonicalRegex().Match(canonical.Trim());
        if (!m.Success) return null;
        var y = int.Parse(m.Groups["y"].Value);
        var mo = m.Groups["m"].Success ? Array.IndexOf(Months, m.Groups["m"].Value) + 1 : 1;
        var d = m.Groups["d"].Success ? int.Parse(m.Groups["d"].Value) : 1;
        if (mo < 1) return null;
        try { return new DateTime(y, mo, Math.Min(d, DateTime.DaysInMonth(y, mo))); }
        catch { return null; }
    }

    /// <summary>Formats a canonical GEDCOM date for display in the given culture.</summary>
    public static string Display(string? canonical, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(canonical)) return "";
        var m = CanonicalRegex().Match(canonical.Trim());
        if (!m.Success) return canonical;

        var german = culture.TwoLetterISOLanguageName == "de";
        var prefix = m.Groups["q"].Value switch
        {
            "ABT" => german ? "um " : "about ",
            "BEF" => german ? "vor " : "before ",
            "AFT" => german ? "nach " : "after ",
            _ => ""
        };
        var y = m.Groups["y"].Value;
        if (!m.Groups["m"].Success) return prefix + y;

        var monthIndex = Array.IndexOf(Months, m.Groups["m"].Value);
        if (monthIndex < 0) return canonical;
        var monthName = culture.DateTimeFormat.GetAbbreviatedMonthName(monthIndex + 1);
        if (!m.Groups["d"].Success) return $"{prefix}{monthName} {y}";

        var d = int.Parse(m.Groups["d"].Value);
        return german ? $"{prefix}{d}. {monthName} {y}" : $"{prefix}{monthName} {d}, {y}";
    }
}
