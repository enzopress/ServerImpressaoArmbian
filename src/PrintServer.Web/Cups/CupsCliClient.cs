using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PrintServer.Web.Printing;

namespace PrintServer.Web.Cups;

/// <summary>Fala com o CUPS pelos comandos lp, lpstat e cancel.</summary>
public sealed class CupsCliClient(
    ICommandRunner runner,
    IOptions<PrintServerOptions> options,
    ILogger<CupsCliClient> logger) : ICupsClient
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SubmitTimeout = TimeSpan.FromSeconds(60);

    private string Printer => options.Value.PrinterName;

    public async Task<string> SubmitAsync(string filePath, PrintOptions printOptions, CancellationToken ct = default)
    {
        var args = LpArguments.Build(Printer, filePath, printOptions);
        var result = await runner.RunAsync("lp", args, SubmitTimeout, ct);
        if (result.ExitCode != 0)
        {
            logger.LogWarning("lp falhou ({ExitCode}): {StdErr}", result.ExitCode, result.StdErr);
            throw new CupsException($"O CUPS recusou o trabalho: {result.StdErr.Trim()}");
        }

        var jobId = LpstatParser.ParseRequestId(result.StdOut)
            ?? throw new CupsException("O CUPS aceitou o trabalho, mas não informou o número.");
        logger.LogInformation("Trabalho {JobId} enviado ({Title}, {Copies} cópia(s))", jobId, printOptions.Title, printOptions.Copies);
        return jobId;
    }

    public async Task<PrinterStatus> GetPrinterStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await runner.RunAsync("lpstat", ["-p", Printer, "-l"], QueryTimeout, ct);
            return result.ExitCode == 0
                ? LpstatParser.ParsePrinterStatus(result.StdOut)
                : PrinterStatus.Unavailable(result.StdErr.Trim());
        }
        catch (CupsException ex)
        {
            return PrinterStatus.Unavailable(ex.Message);
        }
    }

    public async Task<IReadOnlyList<PrintJob>> GetJobsAsync(CancellationToken ct = default)
    {
        var result = await runner.RunAsync("lpstat", ["-o", Printer], QueryTimeout, ct);
        if (result.ExitCode != 0)
            throw new CupsException($"Não foi possível ler a fila: {result.StdErr.Trim()}");
        return LpstatParser.ParseJobs(result.StdOut);
    }

    public async Task CancelAsync(string jobId, CancellationToken ct = default)
    {
        // Só aceita ids desta fila, para o valor vindo do formulário nunca virar outra opção do cancel.
        if (!Regex.IsMatch(jobId, $"^{Regex.Escape(Printer)}-\\d+$"))
            throw new ArgumentException("Trabalho inválido.", nameof(jobId));

        var result = await runner.RunAsync("cancel", [jobId], QueryTimeout, ct);
        if (result.ExitCode != 0)
            throw new CupsException($"Não foi possível cancelar {jobId}: {result.StdErr.Trim()}");
        logger.LogInformation("Trabalho {JobId} cancelado", jobId);
    }

    public async Task<bool> IsSchedulerRunningAsync(CancellationToken ct = default)
    {
        var result = await runner.RunAsync("lpstat", ["-r"], QueryTimeout, ct);
        return result.ExitCode == 0 && result.StdOut.Contains("scheduler is running", StringComparison.Ordinal);
    }
}
