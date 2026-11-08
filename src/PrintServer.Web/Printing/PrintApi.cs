using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace PrintServer.Web.Printing;

public sealed record PrintApiResponse(bool Success, string? JobId, string? Error);

/// <summary>
/// POST /api/print: o mesmo envio da página, para outros programas do servidor (o wa-print).
/// Só aceita chamadas da própria máquina e com a chave X-Api-Key.
/// </summary>
public static class PrintApi
{
    public const string KeyHeader = "X-Api-Key";

    public static void MapPrintApi(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/print", HandleAsync).DisableAntiforgery();

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromForm] IFormFile? file,
        [FromForm] int? copies,
        [FromForm] string? pages,
        PrintService printer,
        IOptions<PrintServerOptions> options,
        CancellationToken ct)
    {
        var key = options.Value.ApiKey;
        if (string.IsNullOrEmpty(key))
            return Results.NotFound();
        // RemoteIpAddress nulo: chamada em processo (testes). Kestrel sempre preenche.
        var remote = http.Connection.RemoteIpAddress;
        if (remote is not null && !IPAddress.IsLoopback(remote))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!KeyMatches(http.Request.Headers[KeyHeader].ToString(), key))
            return Results.Unauthorized();

        var result = await printer.PrintAsync(file, copies ?? 1, pages, ct);
        var body = new PrintApiResponse(result.Success, result.JobId, result.Error);
        return result.Success ? Results.Ok(body) : Results.UnprocessableEntity(body);
    }

    private static bool KeyMatches(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
}
