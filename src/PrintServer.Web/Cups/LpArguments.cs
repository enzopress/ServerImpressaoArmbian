using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PrintServer.Web.Cups;

public static partial class LpArguments
{
    public const int MaxCopies = 20;

    // 1  |  1-3  |  1,3,5-7
    [GeneratedRegex(@"^\d{1,4}(-\d{1,4})?(,\d{1,4}(-\d{1,4})?)*$")]
    private static partial Regex PageRangesPattern();

    public static bool IsValidPageRanges(string? value) =>
        string.IsNullOrWhiteSpace(value) || PageRangesPattern().IsMatch(value.Replace(" ", ""));

    /// <summary>Monta os argumentos do lp. Lança ArgumentException para opções inválidas.</summary>
    public static IReadOnlyList<string> Build(string printerName, string filePath, PrintOptions options)
    {
        if (options.Copies is < 1 or > MaxCopies)
            throw new ArgumentException($"Cópias deve estar entre 1 e {MaxCopies}.", nameof(options));
        if (!IsValidPageRanges(options.PageRanges))
            throw new ArgumentException("Intervalo de páginas inválido. Use, por exemplo, 1-3,5.", nameof(options));

        var args = new List<string>
        {
            "-d", printerName,
            "-n", options.Copies.ToString(CultureInfo.InvariantCulture),
            "-t", SanitizeTitle(options.Title),
        };

        if (!string.IsNullOrWhiteSpace(options.PageRanges))
            args.AddRange(["-P", options.PageRanges.Replace(" ", "")]);

        if (options.FitToPage)
            args.AddRange(["-o", "fit-to-page"]);

        args.Add(filePath);
        return args;
    }

    public static string SanitizeTitle(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var c in title)
            if (!char.IsControl(c))
                sb.Append(c);

        var clean = sb.ToString().Trim();
        if (clean.Length == 0)
            return "Documento";
        return clean.Length <= 100 ? clean : clean[..100];
    }
}
