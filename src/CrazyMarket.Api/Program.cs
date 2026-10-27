using System.Text;
using System.Text.Json.Serialization;
using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Data;
using CrazyMarket.Api.Endpoints;
using CrazyMarket.Api.Hubs;
using CrazyMarket.Api.Services;
using Microsoft.EntityFrameworkCore;

// Registrace Windows-1250 kódování pro Stormware POHODA XML.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// --- Čas ---
builder.Services.AddSingleton(TimeProvider.System);

// --- Databáze ---
builder.Services.AddDbContext<CrazyMarketDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("CrazyMarket")));

// --- Doménové a hardwarové produkční služby ---
builder.Services.AddSingleton<PricingService>();
builder.Services.AddSingleton<TotpAccessService>();
builder.Services.AddSingleton<IDoorController, MqttDoorController>();

// RS232 váha jako Singleton + BackgroundService (otevírá SerialPort a vyvolává OnWeightStabilized)
builder.Services.AddSingleton<SerialPortScaleService>();
builder.Services.AddSingleton<IScaleService>(sp => sp.GetRequiredService<SerialPortScaleService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SerialPortScaleService>());

// Platební terminál a Pokladní stavový automat
builder.Services.AddHttpClient<PaymentTerminalService>();
builder.Services.AddSingleton<IPaymentTerminal>(sp => sp.GetRequiredService<PaymentTerminalService>());
builder.Services.AddSingleton<CheckoutSessionService>();

builder.Services.AddHttpClient<IEslGateway, EslImageGenerator>();
builder.Services.AddHttpClient<IPohodaClient, PohodaXmlService>();

// --- JSON (HTTP i SignalR shodně: enumy jako text) ---
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// --- CORS pro Next.js (SignalR vyžaduje AllowCredentials + explicitní originy) ---
const string WebCorsPolicy = "crazymarket-web";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(WebCorsPolicy, p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SupportNonNullableReferenceTypes());

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "CrazyMarket 24 API");
}

app.UseCors(WebCorsPolicy);

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapProductEndpoints();
app.MapCustomerEndpoints();
app.MapAccessEndpoints();
app.MapSupplierAndPohodaEndpoints();
app.MapKioskEndpoints();
app.MapAdminEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapDevTestEndpoints();
}

app.MapHub<StoreHub>(StoreHub.Path);

app.Run();

/// <summary>Zpřístupnění pro integrační testy (WebApplicationFactory).</summary>
public partial class Program;
