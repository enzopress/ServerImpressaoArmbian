using Microsoft.Extensions.Options;
using PrintServer.Web.Cups;

namespace PrintServer.Web.Printing;

public sealed record PrintResult(bool Success, string? JobId, string? Error)
{
    public static PrintResult Fail(string error) => new(false, null, error);
}

/// <summary>Valida o upload, grava num arquivo temporário, envia ao CUPS e apaga o temporário.</summary>
public sealed class PrintService(
    ICupsClient cups,
    UploadValidator validator,
    IOptions<PrintServerOptions> options,
    ILogger<PrintService> logger)
{
    public async Task<PrintResult> PrintAsync(IFormFile? file, int copies, string? pageRanges, CancellationToken ct = default)
    {
        if (file is null)
            return PrintResult.Fail("Escolha um arquivo para imprimir.");
        if (copies is < 1 or > LpArguments.MaxCopies)
            return PrintResult.Fail($"Cópias deve estar entre 1 e {LpArguments.MaxCopies}.");
        if (!LpArguments.IsValidPageRanges(pageRanges))
            return PrintResult.Fail("Intervalo de páginas inválido. Use, por exemplo, 1-3,5.");

        UploadValidation validation;
        await using (var head = file.OpenReadStream())
            validation = validator.Validate(file.FileName, file.Length, head);
        if (!validation.IsValid)
            return PrintResult.Fail(validation.Error!);

        Directory.CreateDirectory(options.Value.SpoolDir);
        // Nome aleatório: o nome enviado pelo usuário só vai como título do trabalho.
        var tempPath = Path.Combine(options.Value.SpoolDir, $"{Guid.NewGuid():N}{validation.Extension}");
        try
        {
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
                await file.CopyToAsync(target, ct);

            var printOptions = new PrintOptions(
                Title: Path.GetFileName(file.FileName),
                Copies: copies,
                PageRanges: validation.Kind == UploadKind.Image ? null : pageRanges,
                FitToPage: validation.Kind == UploadKind.Image);

            var jobId = await cups.SubmitAsync(tempPath, printOptions, ct);
            return new PrintResult(true, jobId, null);
        }
        catch (CupsException ex)
        {
            return PrintResult.Fail(ex.Message);
        }
        finally
        {
            // O CUPS copia o arquivo para o próprio spool ao aceitar o trabalho.
            try { File.Delete(tempPath); }
            catch (IOException ex) { logger.LogWarning(ex, "Não foi possível apagar {TempPath}", tempPath); }
        }
    }
}
