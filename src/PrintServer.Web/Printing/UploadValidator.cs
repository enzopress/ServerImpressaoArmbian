using Microsoft.Extensions.Options;

namespace PrintServer.Web.Printing;

public enum UploadKind
{
    Pdf,
    Image,
    Text,
}

public sealed record UploadValidation(bool IsValid, string? Error, UploadKind Kind, string Extension)
{
    public static UploadValidation Fail(string error) => new(false, error, default, "");
}

/// <summary>
/// Confere extensão, tamanho e os primeiros bytes do arquivo, para um ".pdf" que não é PDF
/// não chegar ao CUPS.
/// </summary>
public sealed class UploadValidator(IOptions<PrintServerOptions> options)
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    public UploadValidation Validate(string fileName, long length, Stream content)
    {
        var opts = options.Value;
        if (length <= 0)
            return UploadValidation.Fail("Escolha um arquivo para imprimir.");
        if (length > opts.MaxUploadBytes)
            return UploadValidation.Fail($"O arquivo passa do limite de {opts.MaxUploadMb} MB.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!opts.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return UploadValidation.Fail($"Tipo não aceito. Envie: {string.Join(", ", opts.AllowedExtensions)}.");

        var head = new byte[4096];
        var read = content.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        var bytes = head.AsSpan(0, read);

        UploadKind? kind = ext switch
        {
            ".pdf" when bytes.StartsWith(PdfMagic) => UploadKind.Pdf,
            ".png" when bytes.StartsWith(PngMagic) => UploadKind.Image,
            ".jpg" or ".jpeg" when bytes.StartsWith(JpegMagic) => UploadKind.Image,
            ".txt" when !bytes.Contains((byte)0) => UploadKind.Text,
            _ => null,
        };

        return kind is null
            ? UploadValidation.Fail("O conteúdo do arquivo não corresponde à extensão.")
            : new UploadValidation(true, null, kind.Value, ext);
    }
}
