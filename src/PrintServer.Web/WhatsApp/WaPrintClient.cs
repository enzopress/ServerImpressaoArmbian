using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PrintServer.Web.WhatsApp;

public sealed record WaPrintRecent(
    [property: JsonPropertyName("time")] DateTimeOffset Time,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("result")] string Result);

public sealed record WaPrintStatus(
    [property: JsonPropertyName("online")] bool Online,
    [property: JsonPropertyName("connected")] bool Connected,
    [property: JsonPropertyName("loggedIn")] bool LoggedIn,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("chatName")] string? ChatName,
    [property: JsonPropertyName("chatFound")] bool ChatFound,
    [property: JsonPropertyName("recent")] IReadOnlyList<WaPrintRecent>? Recent)
{
    public static WaPrintStatus Offline { get; } = new(false, false, false, null, null, false, []);
}

public sealed record WaPairResult(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>Conversa com o wa-print (ponte do WhatsApp) em 127.0.0.1:8085.</summary>
public sealed class WaPrintClient(HttpClient http, ILogger<WaPrintClient> logger)
{
    /// <summary>Estado atual; <see cref="WaPrintStatus.Offline"/> se o wa-print não responder.</summary>
    public async Task<WaPrintStatus> GetStatusAsync(CancellationToken ct)
    {
        try
        {
            return await http.GetFromJsonAsync<WaPrintStatus>("status", ct) ?? WaPrintStatus.Offline;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogDebug(ex, "wa-print indisponível");
            return WaPrintStatus.Offline;
        }
    }

    /// <summary>Pede o código de pareamento para vincular pelo número de telefone.</summary>
    public async Task<WaPairResult> PairAsync(string phone, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("pair", new { phone }, ct);
            return await response.Content.ReadFromJsonAsync<WaPairResult>(ct)
                ?? new WaPairResult(null, "Resposta vazia do wa-print.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Falha ao pedir o código de pareamento");
            return new WaPairResult(null, "O serviço do WhatsApp (wa-print) não está respondendo.");
        }
    }
}
