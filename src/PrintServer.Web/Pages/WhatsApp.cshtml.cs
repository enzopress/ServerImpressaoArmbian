using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrintServer.Web.WhatsApp;

namespace PrintServer.Web.Pages;

public class WhatsAppModel(WaPrintClient waPrint) : PageModel
{
    public WaPrintStatus Status { get; private set; } = WaPrintStatus.Offline;

    /// <summary>Código para digitar no celular (Aparelhos conectados → Conectar com número de telefone).</summary>
    public string? PairCode { get; private set; }

    [BindProperty]
    [Display(Name = "Número do WhatsApp")]
    [RegularExpression(@"^\+?[\d\s()-]{10,20}$", ErrorMessage = "Informe o número com DDI e DDD, ex.: 55 44 99999-9999.")]
    public string? Phone { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => Status = await waPrint.GetStatusAsync(ct);

    public async Task<IActionResult> OnPostPairAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Phone))
            ModelState.AddModelError(nameof(Phone), "Informe o número com DDI e DDD.");
        if (ModelState.IsValid)
        {
            var digits = new string(Phone!.Where(char.IsDigit).ToArray());
            var result = await waPrint.PairAsync(digits, ct);
            if (result.Code is not null)
                PairCode = result.Code;
            else
                ModelState.AddModelError(string.Empty, result.Error ?? "Não foi possível gerar o código.");
        }
        Status = await waPrint.GetStatusAsync(ct);
        return Page();
    }
}
