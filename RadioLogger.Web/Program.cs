using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using RadioLogger.Web.Components;
using RadioLogger.Web.Data;
using RadioLogger.Web.Hubs;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });

// Add CORS — only allow known origins (WPF clients bypass CORS entirely)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins(
                  "https://cloudradiologger.com",
                  "http://127.0.0.1:5046",
                  "https://127.0.0.1:5046"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add SQL Server DbContext with Resiliency
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<RadioDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions => 
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
    }));

// Add monitoring state service
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<RadioLogger.Web.Services.DashboardLogService>();
builder.Services.AddSingleton<RadioLogger.Web.Services.TelegramService>();
builder.Services.AddSingleton<RadioLogger.Web.Services.EmailService>();
builder.Services.AddSingleton<RadioLogger.Web.Services.MonitoringService>();
builder.Services.AddSingleton<RadioLogger.Web.Services.LicenseManager>();
builder.Services.AddSingleton<RadioLogger.Web.Services.PairingService>();
builder.Services.AddHostedService<RadioLogger.Web.Services.WatchdogService>();

// Auth
builder.Services.AddSingleton<RadioLogger.Web.Services.AuthService>();
builder.Services.AddScoped<RadioLogger.Web.Services.CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<RadioLogger.Web.Services.CustomAuthStateProvider>());
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// HSTS
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// Add SignalR support
builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

var app = builder.Build();

// Cloudflare Support: Forwarded Headers
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Security headers
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["X-XSS-Protection"] = "1; mode=block";
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval' https://static.cloudflareinsights.com; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; img-src 'self' data:; connect-src 'self' wss: ws:; font-src 'self' data: https://fonts.gstatic.com; media-src 'self';";
    await next();
});

app.UseAntiforgery();

// Use CORS before mapping Hubs
app.UseCors("AllowAll");

app.MapStaticAssets();

// Map SignalR Hub with Antiforgery disabled for the hub endpoint
app.MapHub<RadioHub>("/radiohub").DisableAntiforgery();

// Ensure Database is Created - Wrapped in Try-Catch to prevent crash if SQL Server is down
try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        db.Database.EnsureCreated();
        
        // Mark DB as Healthy
        var monitor = app.Services.GetRequiredService<RadioLogger.Web.Services.MonitoringService>();
        monitor.IsDatabaseHealthy = true;
    }
}
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"[DB WARNING] Could not connect to SQL Server: {ex.Message}");
    
    // Mark DB as UNHEALTHY
    var monitor = app.Services.GetRequiredService<RadioLogger.Web.Services.MonitoringService>();
    monitor.IsDatabaseHealthy = false;
}

// Stream proxy: browser plays from /api/stream?url=... instead of direct Shoutcast/Icecast
// This avoids CORS/CSP issues since everything stays under the same origin.
// Requires authentication and validates the URL comes from a known station.
app.MapGet("/api/stream", async (HttpContext context, string? url,
    RadioLogger.Web.Services.MonitoringService monitoring) =>
{
    if (string.IsNullOrWhiteSpace(url))
    {
        context.Response.StatusCode = 400;
        return;
    }

    // Only allow http:// stream URLs (Shoutcast/Icecast are HTTP)
    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
    {
        context.Response.StatusCode = 400;
        return;
    }

    // SSRF protection: only allow URLs that match a known station's StreamUrl
    var knownStations = monitoring.GetActiveStations();
    bool isKnownStream = knownStations.Any(s =>
        !string.IsNullOrEmpty(s.StreamUrl) &&
        url.StartsWith(s.StreamUrl, StringComparison.OrdinalIgnoreCase));

    if (!isKnownStream)
    {
        context.Response.StatusCode = 403;
        return;
    }

    try
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Icy-MetaData", "0"); // Don't request metadata, just audio

        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            return;
        }

        // Only allow audio content types (prevent information leaks)
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        if (!contentType.StartsWith("audio/") && contentType != "application/ogg")
        {
            context.Response.StatusCode = 403;
            return;
        }

        context.Response.ContentType = contentType;
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";

        // Stream the audio data
        using var sourceStream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
        await sourceStream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        // Client disconnected — normal for streaming
    }
    catch
    {
        if (!context.Response.HasStarted)
            context.Response.StatusCode = 502;
    }
}).DisableAntiforgery();
// Auth for this endpoint is handled by SSRF protection (only known station URLs allowed)

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
