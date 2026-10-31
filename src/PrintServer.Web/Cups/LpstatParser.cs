using System.Globalization;
using System.Text.RegularExpressions;

namespace PrintServer.Web.Cups;

/// <summary>Interpreta a saída do lpstat/lp com LANG=C.</summary>
public static partial class LpstatParser
{
    // printer HP135a is idle.  enabled since Sat Oct 31 01:18:23 2026
    // printer HP135a now printing HP135a-1.  enabled since Sat Oct 31 00:54:35 2026
    // printer HP135a disabled since Sat Oct 31 01:12:57 2026 -
    [GeneratedRegex(@"^printer\s+\S+\s+(?:(?<idle>is idle)|now printing\s+(?<job>\S+?)\.|(?<disabled>disabled))")]
    private static partial Regex PrinterLine();

    // HP135a-4                root              1024   Sat Oct 31 01:12:58 2026
    [GeneratedRegex(@"^(?<id>\S+-\d+)\s+(?<user>\S+)\s+(?<size>\d+)\s+(?<date>.+?)\s*$")]
    private static partial Regex JobLine();

    // request id is HP135a-5 (1 file(s))
    [GeneratedRegex(@"request id is\s+(?<id>\S+-\d+)")]
    private static partial Regex RequestId();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static PrinterStatus ParsePrinterStatus(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var first = lines.FirstOrDefault(l => l.StartsWith("printer ", StringComparison.Ordinal));
        if (first is null)
            return PrinterStatus.Unavailable("Resposta do CUPS não reconhecida.");

        var match = PrinterLine().Match(first);
        if (!match.Success)
            return PrinterStatus.Unavailable("Resposta do CUPS não reconhecida.");

        // Linhas seguintes com tab trazem a mensagem atual (ex.: "Processing page 1...").
        var message = lines
            .SkipWhile(l => l != first).Skip(1)
            .Where(l => l.StartsWith('\t') || l.StartsWith(' '))
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith("Description:") && !l.StartsWith("Location:"));

        if (match.Groups["idle"].Success)
            return new PrinterStatus(PrinterState.Idle, message);
        if (match.Groups["job"].Success)
            return new PrinterStatus(PrinterState.Printing, message, match.Groups["job"].Value);
        return new PrinterStatus(PrinterState.Disabled, message);
    }

    public static IReadOnlyList<PrintJob> ParseJobs(string output)
    {
        var jobs = new List<PrintJob>();
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = JobLine().Match(raw.TrimEnd('\r'));
            if (!match.Success)
                continue;

            jobs.Add(new PrintJob(
                match.Groups["id"].Value,
                match.Groups["user"].Value,
                long.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture),
                ParseDate(match.Groups["date"].Value)));
        }
        return jobs;
    }

    public static string? ParseRequestId(string lpOutput)
    {
        var match = RequestId().Match(lpOutput);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static DateTime? ParseDate(string value)
    {
        var normalized = Spaces().Replace(value.Trim(), " ");
        return DateTime.TryParseExact(normalized, "ddd MMM d HH:mm:ss yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date
            : null;
    }
}
