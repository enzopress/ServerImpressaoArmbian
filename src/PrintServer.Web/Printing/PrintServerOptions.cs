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

    /// <summary>
    /// Chave do POST /api/print (usado pelo wa-print). Vazia desliga o endpoint.
    /// Em produção vem de /etc/printserver.env (PrintServer__ApiKey), nunca do appsettings.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Endereço local do wa-print (ponte do WhatsApp), lido pela página WhatsApp.</summary>
    public string WaPrintUrl { get; set; } = "http://127.0.0.1:8085";

    public long MaxUploadBytes => MaxUploadMb * 1024L * 1024L;
}
