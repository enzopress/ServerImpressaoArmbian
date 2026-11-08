using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintServer.Web.Cups;
using PrintServer.Web.Printing;

namespace PrintServer.Tests;

public class ApiPrintTests : IClassFixture<ApiPrintTests.App>
{
    public const string Key = "chave-de-teste";

    public sealed class App : WebApplicationFactory<Program>
    {
        public FakeCupsClient Cups => (FakeCupsClient)Services.GetRequiredService<ICupsClient>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("PrintServer:SpoolDir", Path.Combine(Path.GetTempPath(), "printserver-api", Guid.NewGuid().ToString("N")));
            builder.UseSetting("PrintServer:UseFakeCups", "true");
            builder.UseSetting("PrintServer:ApiKey", Key);
            // Porta sem ninguém ouvindo: o wa-print aparece como desconectado.
            builder.UseSetting("PrintServer:WaPrintUrl", "http://127.0.0.1:9");
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<ICupsClient, FakeCupsClient>()));
        }
    }

    private readonly App _app;

    public ApiPrintTests(App app) => _app = app;

    private static MultipartFormDataContent Form(byte[] content, string fileName, string? copies = null, string? pages = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (copies is not null) form.Add(new StringContent(copies), "copies");
        if (pages is not null) form.Add(new StringContent(pages), "pages");
        return form;
    }

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private async Task<HttpResponseMessage> PostAsync(MultipartFormDataContent form, string? key = Key)
    {
        var client = _app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/print") { Content = form };
        if (key is not null) request.Headers.Add(PrintApi.KeyHeader, key);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Without_key_is_unauthorized()
    {
        var response = await PostAsync(Form(Pdf, "a.pdf"), key: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_key_is_unauthorized()
    {
        var response = await PostAsync(Form(Pdf, "a.pdf"), key: "errada");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pdf_with_key_is_printed_with_copies_and_pages()
    {
        var before = _app.Cups.Submitted.Count;

        var response = await PostAsync(Form(Pdf, "boleto.pdf", copies: "2", pages: "1"));
        var body = await response.Content.ReadFromJsonAsync<PrintApiResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body!.Success);
        Assert.NotNull(body.JobId);
        var (_, options) = _app.Cups.Submitted[before];
        Assert.Equal("boleto.pdf", options.Title);
        Assert.Equal(2, options.Copies);
        Assert.Equal("1", options.PageRanges);
        Assert.False(options.FitToPage);
    }

    [Fact]
    public async Task Image_is_fit_to_page()
    {
        var before = _app.Cups.Submitted.Count;

        var response = await PostAsync(Form(Png, "foto.png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_app.Cups.Submitted[before].Options.FitToPage);
    }

    [Fact]
    public async Task Unsupported_type_returns_error_and_prints_nothing()
    {
        var before = _app.Cups.Submitted.Count;

        var response = await PostAsync(Form(Encoding.ASCII.GetBytes("PK\x03\x04docx"), "contrato.docx"));
        var body = await response.Content.ReadFromJsonAsync<PrintApiResponse>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(body!.Success);
        Assert.False(string.IsNullOrEmpty(body.Error));
        Assert.Equal(before, _app.Cups.Submitted.Count);
    }

    [Fact]
    public async Task WhatsApp_page_shows_disconnected_when_wa_print_is_down()
    {
        var html = await _app.CreateClient().GetStringAsync("/WhatsApp");

        Assert.Contains("Desconectado", html);
    }
}
