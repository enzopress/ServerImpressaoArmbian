using System.Diagnostics;

namespace PrintServer.Web.Cups;

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr);

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>
/// Executa comandos do CUPS sem shell: cada argumento vai separado em ArgumentList,
/// então nome de arquivo ou título não conseguem injetar comandos.
/// </summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        // Saída do lpstat em inglês, para o parser não depender do idioma do sistema.
        psi.Environment["LANG"] = "C";
        psi.Environment["LC_ALL"] = "C";

        Process? started;
        try
        {
            started = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new CupsException($"Não foi possível executar '{fileName}': {ex.Message}");
        }
        using var process = started ?? throw new CupsException($"Não foi possível iniciar '{fileName}'.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var stdOut = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stdErr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new CupsException($"'{fileName}' não respondeu em {timeout.TotalSeconds:0} s.");
        }

        return new CommandResult(process.ExitCode, await stdOut, await stdErr);
    }
}
