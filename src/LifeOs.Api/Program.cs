using System.Data.Common;
using System.Text.Json.Serialization;
using LifeOs.Api.Data;
using LifeOs.Api.Endpoints;
using LifeOs.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LifeOs.Api.Components;
using LifeOs.Api.Ui;

var builder = WebApplication.CreateBuilder(args);
// Also resolve framework assets when running locally without a Development launch profile.
builder.WebHost.UseStaticWebAssets();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
// Swashbuckle reads MVC JSON options even though these endpoints use Minimal APIs.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AddDbContext<LifeOsDbContext>(o => o.UseNpgsql(
    builder.Configuration.GetConnectionString("LifeOs") ?? throw new InvalidOperationException("Configure ConnectionStrings__LifeOs.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<LlmOptions>().BindConfiguration("Llm")
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", "Invalid Llm:BaseUrl")
    .Validate(o => o.TimeoutSeconds is >= 1 and <= 300, "Llm:TimeoutSeconds must be 1–300")
    .ValidateOnStart();
builder.Services.AddHttpClient<ILifeOsLlmService, OpenAiCompatibleLlmService>((sp, http) =>
    http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<LlmOptions>>().Value.TimeoutSeconds));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient("LifeOsUi", client => client.Timeout = TimeSpan.FromMinutes(6));
builder.Services.AddScoped<LifeOsApiClient>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception e)
    {
        // Exception messages can contain personal data; log only type and trace ID.
        app.Logger.LogError("Request failed: {ErrorType}, trace {TraceId}", e.GetType().Name, context.TraceIdentifier);
        var status = e switch { BadHttpRequestException => 400, DbException or DbUpdateException => 503, _ => 500 };
        if (!context.Response.HasStarted)
            await Results.Problem(statusCode: status, title: status switch
            {
                400 => "Nieprawidłowe żądanie. Sprawdź JSON, datę i wartości pól.",
                503 => "Baza danych jest niedostępna. Sprawdź historię przed ponownym wysłaniem notatki.",
                _ => "Wystąpił nieoczekiwany błąd."
            }).ExecuteAsync(context);
    }
});
app.UseSwagger();
app.UseSwaggerUI();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapLifeOs();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
public partial class Program;
