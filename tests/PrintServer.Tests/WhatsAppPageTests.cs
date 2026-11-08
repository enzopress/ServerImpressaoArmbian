using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrintServer.Web.Cups;
using PrintServer.Web.WhatsApp;

namespace PrintServer.Tests;

/// <summary>Página /WhatsApp com um wa-print falso (status e QR configuráveis por teste).</summary>
public class WhatsAppPageTests
{
    private static readonly byte[] QrPng = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private sealed class FakeWaPrint : HttpMessageHandler
    {
        public bool QrActive { get; set; }
        public int QrStarts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response = path switch
            {
                "/status" => Json($$"""
                    {"online":true,"connected":false,"loggedIn":false,"phone":"","chatName":"Impressora",
                     "chatFound":false,"qrActive":{{(QrActive ? "true" : "false")}},"recent":[]}
                    """),
                "/pair-qr" => StartQr(),
                "/qr.png" when QrActive => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(QrPng) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
            return Task.FromResult(response);
        }

        private HttpResponseMessage StartQr()
        {
            QrStarts++;
            QrActive = true;
            return Json("""{"ok":true}""");
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class App(FakeWaPrint wa) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("PrintServer:SpoolDir", Path.Combine(Path.GetTempPath(), "printserver-wa", Guid.NewGuid().ToString("N")));
            builder.UseSetting("PrintServer:UseFakeCups", "true");
            builder.ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<ICupsClient, FakeCupsClient>());
                s.AddHttpClient<WaPrintClient>().ConfigurePrimaryHttpMessageHandler(() => wa);
            });
        }
    }

    [Fact]
    public async Task Not_linked_offers_qr_button()
    {
        using var app = new App(new FakeWaPrint());

        var html = await app.CreateClient().GetStringAsync("/WhatsApp");

        Assert.Contains("Vincular com QR code", html);
        Assert.DoesNotContain("handler=QrImage", html);
    }

    [Fact]
    public async Task Active_qr_is_shown_and_served_as_png()
    {
        using var app = new App(new FakeWaPrint { QrActive = true });
        var client = app.CreateClient();

        var html = await client.GetStringAsync("/WhatsApp");
        var image = await client.GetAsync("/WhatsApp?handler=QrImage");

        Assert.Contains("handler=QrImage", html);
        Assert.Contains("http-equiv=\"refresh\"", html);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(QrPng, await image.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Qr_image_is_404_without_active_pairing()
    {
        using var app = new App(new FakeWaPrint());

        var image = await app.CreateClient().GetAsync("/WhatsApp?handler=QrImage");

        Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);
    }
}
