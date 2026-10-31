using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintServer.Web.Cups;
using PrintServer.Web.Printing;

namespace PrintServer.Tests;

public class CupsCliClientTests
{
    private sealed class FakeRunner : ICommandRunner
    {
        public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = [];
        public CommandResult Next { get; set; } = new(0, "", "");

        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct = default)
        {
            Calls.Add((fileName, arguments));
            return Task.FromResult(Next);
        }
    }

    private readonly FakeRunner _runner = new();
    private readonly CupsCliClient _client;

    public CupsCliClientTests()
    {
        _client = new CupsCliClient(_runner, Options.Create(new PrintServerOptions()), NullLogger<CupsCliClient>.Instance);
    }

    [Fact]
    public async Task Submit_runs_lp_and_returns_job_id()
    {
        _runner.Next = new(0, "request id is HP135a-7 (1 file(s))\n", "");

        var id = await _client.SubmitAsync("/var/lib/printserver/uploads/x.pdf", new PrintOptions("doc.pdf"));

        Assert.Equal("HP135a-7", id);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("lp", call.File);
        Assert.Equal("/var/lib/printserver/uploads/x.pdf", call.Args[^1]);
    }

    [Fact]
    public async Task Submit_failure_throws_with_cups_message()
    {
        _runner.Next = new(1, "", "lp: The printer or class does not exist.\n");

        var ex = await Assert.ThrowsAsync<CupsException>(() => _client.SubmitAsync("/tmp/x.pdf", new PrintOptions("x")));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task Status_reads_lpstat_long_listing()
    {
        _runner.Next = new(0, "printer HP135a is idle.  enabled since Sat Oct 31 01:18:23 2026\n", "");

        var status = await _client.GetPrinterStatusAsync();

        Assert.Equal(PrinterState.Idle, status.State);
        Assert.Equal(["-p", "HP135a", "-l"], _runner.Calls[0].Args);
    }

    [Fact]
    public async Task Status_error_becomes_unknown()
    {
        _runner.Next = new(1, "", "lpstat: Bad file descriptor\n");

        var status = await _client.GetPrinterStatusAsync();

        Assert.Equal(PrinterState.Unknown, status.State);
        Assert.Equal("lpstat: Bad file descriptor", status.Message);
    }

    [Fact]
    public async Task Cancel_runs_cancel_for_valid_id()
    {
        await _client.CancelAsync("HP135a-12");

        var call = Assert.Single(_runner.Calls);
        Assert.Equal("cancel", call.File);
        Assert.Equal(["HP135a-12"], call.Args);
    }

    [Theory]
    [InlineData("-a")]
    [InlineData("OutraFila-1")]
    [InlineData("HP135a-1 -a")]
    [InlineData("HP135a-")]
    public async Task Cancel_rejects_ids_from_other_queues_or_options(string id)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CancelAsync(id));
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Scheduler_running()
    {
        _runner.Next = new(0, "scheduler is running\n", "");
        Assert.True(await _client.IsSchedulerRunningAsync());
    }
}
