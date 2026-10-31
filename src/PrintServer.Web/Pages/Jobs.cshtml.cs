using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrintServer.Web.Cups;

namespace PrintServer.Web.Pages;

public sealed record JobsView(PrinterStatus Status, IReadOnlyList<PrintJob> Jobs, string? Error)
{
    /// <summary>Usuário do serviço: só os trabalhos enviados pelo app podem ser cancelados por aqui.</summary>
    public static string ServiceUser { get; } = Environment.UserName;

    public bool CanCancel(PrintJob job) => string.Equals(job.User, ServiceUser, StringComparison.Ordinal);
}

public class JobsModel(ICupsClient cups) : PageModel
{
    public JobsView View { get; private set; } = new(new(PrinterState.Unknown), [], null);

    public async Task OnGetAsync(CancellationToken ct) => View = await LoadAsync(ct);

    /// <summary>Só a tabela, para a atualização automática da página.</summary>
    public async Task<IActionResult> OnGetTableAsync(CancellationToken ct) =>
        Partial("_JobsTable", await LoadAsync(ct));

    public async Task<IActionResult> OnPostCancelAsync(string id, CancellationToken ct)
    {
        try
        {
            await cups.CancelAsync(id, ct);
            TempData["Message"] = $"Trabalho {id} cancelado.";
        }
        catch (Exception ex) when (ex is CupsException or ArgumentException)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage();
    }

    private async Task<JobsView> LoadAsync(CancellationToken ct)
    {
        var status = await cups.GetPrinterStatusAsync(ct);
        try
        {
            return new JobsView(status, await cups.GetJobsAsync(ct), null);
        }
        catch (CupsException ex)
        {
            return new JobsView(status, [], ex.Message);
        }
    }
}
