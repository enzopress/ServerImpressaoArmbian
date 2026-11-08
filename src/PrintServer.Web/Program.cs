using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using PrintServer.Web.Cups;
using PrintServer.Web.Printing;
using PrintServer.Web.WhatsApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<PrintServerOptions>()
    .Bind(builder.Configuration.GetSection(PrintServerOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var printServer = builder.Configuration.GetSection(PrintServerOptions.Section).Get<PrintServerOptions>() ?? new();

// Limite de upload: o corpo inclui o arquivo e os outros campos do formulário.
var maxBody = printServer.MaxUploadBytes + 1024 * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxBody);
builder.Services.Configure<FormOptions>(f => f.MultipartBodyLengthLimit = maxBody);

builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
if (printServer.UseFakeCups)
    builder.Services.AddSingleton<ICupsClient, FakeCupsClient>();
else
    builder.Services.AddSingleton<ICupsClient, CupsCliClient>();
builder.Services.AddSingleton<UploadValidator>();
builder.Services.AddScoped<PrintService>();
builder.Services.AddHttpClient<WaPrintClient>(c =>
{
    c.BaseAddress = new Uri(printServer.WaPrintUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddRazorPages();
builder.Services.AddHealthChecks().AddCheck<CupsHealthCheck>("cups");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapHealthChecks("/health");
app.MapPrintApi();

app.Logger.LogInformation("Fila {Printer}, CUPS {Mode}",
    app.Services.GetRequiredService<IOptions<PrintServerOptions>>().Value.PrinterName,
    printServer.UseFakeCups ? "em memória (fake)" : "real");

app.Run();

public partial class Program;
