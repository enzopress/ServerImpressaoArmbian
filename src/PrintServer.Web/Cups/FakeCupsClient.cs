using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PrintServer.Web.Printing;

namespace PrintServer.Web.Cups;

/// <summary>CUPS de mentira, em memória, para desenvolver no Windows e para os testes.</summary>
public sealed class FakeCupsClient(IOptions<PrintServerOptions> options) : ICupsClient
{
    private readonly ConcurrentDictionary<string, PrintJob> _jobs = new();
    private int _nextId;

    public List<(string FilePath, PrintOptions Options)> Submitted { get; } = [];
    public List<string> Cancelled { get; } = [];
    public PrinterStatus Status { get; set; } = new(PrinterState.Idle);

    public Task<string> SubmitAsync(string filePath, PrintOptions printOptions, CancellationToken ct = default)
    {
        // Valida igual ao cliente real, para os erros aparecerem também no modo fake.
        LpArguments.Build(options.Value.PrinterName, filePath, printOptions);

        var id = $"{options.Value.PrinterName}-{Interlocked.Increment(ref _nextId)}";
        lock (Submitted)
            Submitted.Add((filePath, printOptions));
        _jobs[id] = new PrintJob(id, Environment.UserName, new FileInfo(filePath).Length, DateTime.Now);
        return Task.FromResult(id);
    }

    public Task<PrinterStatus> GetPrinterStatusAsync(CancellationToken ct = default) => Task.FromResult(Status);

    public Task<IReadOnlyList<PrintJob>> GetJobsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PrintJob>>(_jobs.Values.OrderBy(j => j.SubmittedAt).ToList());

    public Task CancelAsync(string jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryRemove(jobId, out _))
            throw new CupsException($"Não foi possível cancelar {jobId}: o trabalho não existe.");
        lock (Cancelled)
            Cancelled.Add(jobId);
        return Task.CompletedTask;
    }

    public Task<bool> IsSchedulerRunningAsync(CancellationToken ct = default) => Task.FromResult(true);
}
