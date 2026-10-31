namespace PrintServer.Web.Cups;

public interface ICupsClient
{
    /// <summary>Envia o arquivo para a fila e devolve o id do trabalho (ex.: HP135a-12).</summary>
    Task<string> SubmitAsync(string filePath, PrintOptions options, CancellationToken ct = default);

    Task<PrinterStatus> GetPrinterStatusAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PrintJob>> GetJobsAsync(CancellationToken ct = default);

    Task CancelAsync(string jobId, CancellationToken ct = default);

    Task<bool> IsSchedulerRunningAsync(CancellationToken ct = default);
}
