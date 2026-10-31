using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PrintServer.Web.Cups;

public sealed class CupsHealthCheck(ICupsClient cups) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            if (!await cups.IsSchedulerRunningAsync(ct))
                return HealthCheckResult.Unhealthy("O CUPS não está rodando.");

            var status = await cups.GetPrinterStatusAsync(ct);
            return status.State switch
            {
                PrinterState.Unknown => HealthCheckResult.Degraded($"Impressora sem resposta: {status.Message}"),
                PrinterState.Disabled => HealthCheckResult.Degraded("Fila da impressora pausada."),
                _ => HealthCheckResult.Healthy(),
            };
        }
        catch (CupsException ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
