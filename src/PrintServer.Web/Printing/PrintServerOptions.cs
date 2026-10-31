using System.ComponentModel.DataAnnotations;

namespace PrintServer.Web.Printing;

public sealed class PrintServerOptions
{
    public const string Section = "PrintServer";

    /// <summary>Nome da fila no CUPS.</summary>
    [Required]
    public string PrinterName { get; set; } = "HP135a";

    [Range(1, 100)]
    public int MaxUploadMb { get; set; } = 20;

    public string[] AllowedExtensions { get; set; } = [".pdf", ".png", ".jpg", ".jpeg", ".txt"];

    /// <summary>Pasta temporária dos uploads antes de irem para o CUPS.</summary>
    [Required]
    public string SpoolDir { get; set; } = "/var/lib/printserver/uploads";

    /// <summary>Usa o CUPS em memória (desenvolvimento no Windows).</summary>
    public bool UseFakeCups { get; set; }

    public long MaxUploadBytes => MaxUploadMb * 1024L * 1024L;
}
