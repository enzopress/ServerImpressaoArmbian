using PrintServer.Web.Cups;

namespace PrintServer.Web.Pages;

public static class StatusText
{
    public static string For(PrinterState state) => state switch
    {
        PrinterState.Idle => "Pronta",
        PrinterState.Printing => "Imprimindo",
        PrinterState.Disabled => "Pausada",
        _ => "Sem resposta",
    };
}
