namespace Ahnen.Web.Helpers;

public static class TextExtensions
{
    /// <summary>Joins the parts that are not null, empty or whitespace.</summary>
    public static string JoinNonBlank(this IEnumerable<string?> parts, string separator = " ") =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
