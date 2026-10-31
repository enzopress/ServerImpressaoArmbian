using PrintServer.Web.Cups;

namespace PrintServer.Tests;

// Saídas reais coletadas no MX9 (CUPS 2.4, Debian 13, LANG=C).
public class LpstatParserTests
{
    [Fact]
    public void Idle_printer()
    {
        var status = LpstatParser.ParsePrinterStatus(
            "printer HP135a is idle.  enabled since Sat Oct 31 01:18:23 2026\n");

        Assert.Equal(PrinterState.Idle, status.State);
        Assert.Null(status.CurrentJobId);
    }

    [Fact]
    public void Printing_printer_with_message()
    {
        var status = LpstatParser.ParsePrinterStatus(
            "printer HP135a now printing HP135a-1.  enabled since Sat Oct 31 00:54:35 2026\n\tProcessing page 1...\n");

        Assert.Equal(PrinterState.Printing, status.State);
        Assert.Equal("HP135a-1", status.CurrentJobId);
        Assert.Equal("Processing page 1...", status.Message);
    }

    [Fact]
    public void Disabled_printer()
    {
        var status = LpstatParser.ParsePrinterStatus(
            "printer HP135a disabled since Sat Oct 31 01:12:57 2026 -\n\treason unknown\n");

        Assert.Equal(PrinterState.Disabled, status.State);
        Assert.Equal("reason unknown", status.Message);
    }

    [Fact]
    public void Long_listing_skips_description_lines()
    {
        var status = LpstatParser.ParsePrinterStatus(
            "printer HP135a is idle.  enabled since Sat Oct 31 01:18:23 2026\n\tDescription: HP Laser MFP 135a\n\tLocation: MX9\n");

        Assert.Equal(PrinterState.Idle, status.State);
        Assert.Null(status.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lpstat: Invalid destination name in list \"HP135a\".")]
    public void Unrecognized_output_is_unknown(string output)
    {
        Assert.Equal(PrinterState.Unknown, LpstatParser.ParsePrinterStatus(output).State);
    }

    [Fact]
    public void Jobs_are_parsed()
    {
        var jobs = LpstatParser.ParseJobs(
            "HP135a-5                root              1024   Sat Oct 31 01:18:58 2026\n" +
            "HP135a-4                printserver     148480   Sat Oct  3 01:12:58 2026\n");

        Assert.Equal(2, jobs.Count);
        Assert.Equal(new PrintJob("HP135a-5", "root", 1024, new DateTime(2026, 10, 31, 1, 18, 58)), jobs[0]);
        Assert.Equal("printserver", jobs[1].User);
        Assert.Equal(148480, jobs[1].SizeBytes);
        Assert.Equal(new DateTime(2026, 10, 3, 1, 12, 58), jobs[1].SubmittedAt);
    }

    [Fact]
    public void Empty_queue_has_no_jobs()
    {
        Assert.Empty(LpstatParser.ParseJobs(""));
    }

    [Theory]
    [InlineData("request id is HP135a-5 (1 file(s))\n", "HP135a-5")]
    [InlineData("nothing here", null)]
    public void Request_id_from_lp(string output, string? expected)
    {
        Assert.Equal(expected, LpstatParser.ParseRequestId(output));
    }
}
