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

// Add SQL Server DbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<RadioDbContext>(options =>
    options.UseSqlServer(connectionString));

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

// app.UseHttpsRedirection();
app.UseAntiforgery();

// Use CORS before mapping Hubs
app.UseCors("AllowAll");

app.MapStaticAssets();

// Map SignalR Hub with Antiforgery disabled for the hub endpoint
app.MapHub<RadioHub>("/radiohub").DisableAntiforgery();

// Ensure Database is Created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/heartbeat", () => Results.Ok(new { status = "Healthy", time = DateTime.UtcNow }));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
