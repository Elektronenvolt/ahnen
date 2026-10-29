using System.Globalization;
using Ahnen.Web.Models;

namespace Ahnen.Web.Services;

/// <summary>
/// Turns a <see cref="RelationshipResult"/> into a localized human label
/// (English/German). Composition rules (great-/Ur-, cousin degrees) are
/// language-specific, so they live here rather than in flat resources.
/// </summary>
public static class RelationshipLabeler
{
    public static string Label(RelationshipResult r, CultureInfo culture)
    {
        var de = culture.TwoLetterISOLanguageName == "de";
        return r.Kind switch
        {
            RelationshipKind.None => de ? "nicht verwandt" : "not related",
            RelationshipKind.Self => de ? "diese Person" : "this person",
            RelationshipKind.Spouse => Spouse(r, de),
            RelationshipKind.Ancestor => Ancestor(r.Degree, r.TargetSex, de),
            RelationshipKind.Descendant => Descendant(r.Degree, r.TargetSex, de),
            RelationshipKind.Sibling => BySex(r.TargetSex, de, "Bruder", "Schwester", "Geschwister", "brother", "sister", "sibling"),
            RelationshipKind.HalfSibling => BySex(r.TargetSex, de, "Halbbruder", "Halbschwester", "Halbgeschwister", "half-brother", "half-sister", "half-sibling"),
            RelationshipKind.UncleAunt => UncleAunt(r.Degree, r.TargetSex, de),
            RelationshipKind.NephewNiece => NephewNiece(r.Degree, r.TargetSex, de),
            RelationshipKind.Cousin => Cousin(r, de),
            RelationshipKind.ParentInLaw => BySex(r.TargetSex, de, "Schwiegervater", "Schwiegermutter", "Schwiegerelternteil", "father-in-law", "mother-in-law", "parent-in-law"),
            RelationshipKind.ChildInLaw => BySex(r.TargetSex, de, "Schwiegersohn", "Schwiegertochter", "Schwiegerkind", "son-in-law", "daughter-in-law", "child-in-law"),
            RelationshipKind.SiblingInLaw => BySex(r.TargetSex, de, "Schwager", "Schwägerin", "Schwager/Schwägerin", "brother-in-law", "sister-in-law", "sibling-in-law"),
            RelationshipKind.StepParent => BySex(r.TargetSex, de, "Stiefvater", "Stiefmutter", "Stiefelternteil", "stepfather", "stepmother", "step-parent"),
            RelationshipKind.StepChild => BySex(r.TargetSex, de, "Stiefsohn", "Stieftochter", "Stiefkind", "stepson", "stepdaughter", "stepchild"),
            _ => de ? "verwandt" : "relative"
        };
    }

    private static string BySex(Sex sex, bool de, string deM, string deF, string deN, string enM, string enF, string enN) =>
        sex switch
        {
            Sex.M => de ? deM : enM,
            Sex.F => de ? deF : enF,
            _ => de ? deN : enN
        };

    private static string Spouse(RelationshipResult r, bool de)
    {
        var divorced = r.SpouseStatus is FamilyEventType.DIV or FamilyEventType.ANUL;
        var married = r.SpouseStatus is FamilyEventType.MARR;
        if (divorced)
            return BySex(r.TargetSex, de, "Ex-Mann", "Ex-Frau", "Ex-Partner", "ex-husband", "ex-wife", "ex-partner");
        if (married)
            return BySex(r.TargetSex, de, "Ehemann", "Ehefrau", "Ehepartner", "husband", "wife", "spouse");
        return BySex(r.TargetSex, de, "Partner", "Partnerin", "Partner", "partner", "partner", "partner");
    }

    private static string Ancestor(int degree, Sex sex, bool de)
    {
        if (degree == 1) return BySex(sex, de, "Vater", "Mutter", "Elternteil", "father", "mother", "parent");
        var baseWord = BySex(sex, de, "großvater", "großmutter", "großelternteil", "grandfather", "grandmother", "grandparent");
        return Compose(degree - 2, baseWord, de);
    }

    private static string Descendant(int degree, Sex sex, bool de)
    {
        if (degree == 1) return BySex(sex, de, "Sohn", "Tochter", "Kind", "son", "daughter", "child");
        var baseWord = BySex(sex, de, "enkel", "enkelin", "enkelkind", "grandson", "granddaughter", "grandchild");
        return Compose(degree - 2, baseWord, de);
    }

    private static string UncleAunt(int degree, Sex sex, bool de) =>
        Collateral(degree, BySex(sex, de, "onkel", "tante", "onkel/tante", "uncle", "aunt", "uncle/aunt"), de);

    private static string NephewNiece(int degree, Sex sex, bool de) =>
        Collateral(degree, BySex(sex, de, "neffe", "nichte", "neffe/nichte", "nephew", "niece", "nephew/niece"), de);

    /// <summary>Degree 0 = plain word; higher degrees add great-/Ur-groß (degree 1 = great-uncle / Großonkel).</summary>
    private static string Collateral(int degree, string baseWord, bool de)
    {
        if (degree == 0) return de ? Capitalize(baseWord) : baseWord;
        return de
            ? Capitalize(string.Concat(Enumerable.Repeat("ur", degree - 1)) + "groß" + baseWord)
            : string.Concat(Enumerable.Repeat("great-", degree)) + baseWord;
    }

    private static string Cousin(RelationshipResult r, bool de)
    {
        var degree = Math.Max(1, r.Degree);
        string label;
        if (de)
        {
            var baseWord = r.TargetSex == Sex.F ? "Cousine" : "Cousin";
            label = degree == 1 ? baseWord : $"{baseWord} {degree}. Grades";
            if (r.Removed > 0) label += $" ({r.Removed}x entfernt)";
        }
        else
        {
            var ordinal = degree switch
            {
                1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth",
                _ => $"{degree}th"
            };
            label = $"{ordinal} cousin";
            label += r.Removed switch
            {
                0 => "",
                1 => ", once removed",
                2 => ", twice removed",
                _ => $", {r.Removed} times removed"
            };
        }
        return label;
    }

    private static string Compose(int greats, string baseWord, bool de)
    {
        if (de)
            return Capitalize(string.Concat(Enumerable.Repeat("ur", greats)) + baseWord);
        return string.Concat(Enumerable.Repeat("great-", greats)) + baseWord;
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
