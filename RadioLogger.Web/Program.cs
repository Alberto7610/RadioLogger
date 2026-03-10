using Microsoft.EntityFrameworkCore;
using RadioLogger.Web.Components;
using RadioLogger.Web.Data;
using RadioLogger.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Force Kestrel to listen on IPv4 loopback only on port 5000
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, 5000); 
});

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
builder.Services.AddSingleton<RadioLogger.Web.Services.MonitoringService>();

// Add SignalR support
builder.Services.AddSignalR(options => {
    options.EnableDetailedErrors = true;
});

var app = builder.Build();

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
