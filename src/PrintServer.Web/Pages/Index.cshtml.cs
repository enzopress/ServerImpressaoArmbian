using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrintServer.Web.Cups;
using PrintServer.Web.Printing;

namespace PrintServer.Web.Pages;

public class IndexModel(ICupsClient cups, PrintService printService) : PageModel
{
    public PrinterStatus Status { get; private set; } = new(PrinterState.Unknown);

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty, Range(1, LpArguments.MaxCopies, ErrorMessage = "Cópias deve estar entre 1 e 20.")]
    public int Copies { get; set; } = 1;

    [BindProperty]
    public string? Pages { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Status = await cups.GetPrinterStatusAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await printService.PrintAsync(Upload, Copies, Pages, ct);
            if (result.Success)
            {
                TempData["Message"] = $"Enviado para a impressora (trabalho {result.JobId}).";
                return RedirectToPage();
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        Status = await cups.GetPrinterStatusAsync(ct);
        return Page();
    }
}
