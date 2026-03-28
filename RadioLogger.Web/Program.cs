using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using RadioLogger.Web.Components;
using RadioLogger.Web.Data;
using RadioLogger.Web.Hubs;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add CORS for SignalR from WPF
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true)
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
builder.Services.AddSingleton<RadioLogger.Web.Services.TelegramService>();
builder.Services.AddSingleton<RadioLogger.Web.Services.MonitoringService>();
builder.Services.AddSingleton<RadioLogger.Web.Services.LicenseManager>();
builder.Services.AddHostedService<RadioLogger.Web.Services.WatchdogService>();

// Auth
builder.Services.AddSingleton<RadioLogger.Web.Services.AuthService>();
builder.Services.AddScoped<RadioLogger.Web.Services.CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<RadioLogger.Web.Services.CustomAuthStateProvider>());
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// Add SignalR support
builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true;
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

// app.UseAntiforgery(); // Re-enable if needed, but for now we prioritize connectivity
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

app.MapGet("/heartbeat", () => Results.Ok(new { status = "Healthy", time = DateTime.UtcNow }));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
