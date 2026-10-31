using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintServer.Web.Cups;

namespace PrintServer.Tests;

public partial class PagesTests : IClassFixture<PagesTests.App>
{
    public sealed class App : WebApplicationFactory<Program>
    {
        public string SpoolDir { get; } = Path.Combine(Path.GetTempPath(), "printserver-tests", Guid.NewGuid().ToString("N"));

        public FakeCupsClient Cups => (FakeCupsClient)Services.GetRequiredService<ICupsClient>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("PrintServer:SpoolDir", SpoolDir);
            builder.UseSetting("PrintServer:UseFakeCups", "true");
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<ICupsClient, FakeCupsClient>()));
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private readonly App _app;

    public PagesTests(App app) => _app = app;

    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> TokenFrom(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        return TokenPattern().Match(html).Groups[1].Value;
    }

    [Fact]
    public async Task Home_shows_printer_status()
    {
        var html = await Client().GetStringAsync("/");

        Assert.Contains("Imprimir um arquivo", html);
        Assert.Contains("Pronta", html);
    }

    [Fact]
    public async Task Valid_pdf_upload_is_sent_to_cups_and_temp_file_is_removed()
    {
        var client = Client();
        var token = await TokenFrom(client, "/");
        var before = _app.Cups.Submitted.Count;

        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("2"), "Copies" },
            { new StringContent("1-2"), "Pages" },
        };
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "Upload", "contrato.pdf");

        var response = await client.PostAsync("/", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var (path, options) = _app.Cups.Submitted[before];
        Assert.Equal("contrato.pdf", options.Title);
        Assert.Equal(2, options.Copies);
        Assert.Equal("1-2", options.PageRanges);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Fake_pdf_is_rejected_with_message()
    {
        var client = Client();
        var token = await TokenFrom(client, "/");
        var before = _app.Cups.Submitted.Count;

        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("1"), "Copies" },
            { new ByteArrayContent(Encoding.ASCII.GetBytes("MZ nao sou pdf")), "Upload", "falso.pdf" },
        };

        var response = await client.PostAsync("/", form);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("não corresponde à extensão", WebUtility.HtmlDecode(html));
        Assert.Equal(before, _app.Cups.Submitted.Count);
    }

    [Fact]
    public async Task Jobs_page_lists_and_cancels_jobs()
    {
        var tmp = Path.GetTempFileName();
        var jobId = await _app.Cups.SubmitAsync(tmp, new PrintOptions("teste.pdf"));
        File.Delete(tmp);

        var client = Client();
        var html = await client.GetStringAsync("/Jobs");
        Assert.Contains(jobId, html);
        var token = TokenPattern().Match(html).Groups[1].Value;

        var response = await client.PostAsync($"/Jobs?handler=Cancel&id={jobId}",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(jobId, _app.Cups.Cancelled);
    }

    [Fact]
    public async Task Jobs_table_handler_returns_partial()
    {
        var html = await Client().GetStringAsync("/Jobs?handler=Table");

        Assert.DoesNotContain("<html", html);
        Assert.Contains("status", html);
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        var response = await Client().PostAsync("/Jobs?handler=Cancel&id=HP135a-1", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_healthy()
    {
        var response = await Client().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
