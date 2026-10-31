namespace PrintServer.Web.Cups;

public enum PrinterState
{
    Unknown,
    Idle,
    Printing,
    Disabled,
}

public sealed record PrinterStatus(PrinterState State, string? Message = null, string? CurrentJobId = null)
{
    public static PrinterStatus Unavailable(string message) => new(PrinterState.Unknown, message);
}

public sealed record PrintJob(string Id, string User, long SizeBytes, DateTime? SubmittedAt);

public sealed record PrintOptions(string Title, int Copies = 1, string? PageRanges = null, bool FitToPage = false);

public sealed class CupsException(string message) : Exception(message);
