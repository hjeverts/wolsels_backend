using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolsels.Api.Auth;
using Wolsels.Api.Data;
using Wolsels.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<WolselsDbContext>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddDbContext<WolselsDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Wolsels")));

// --- Authenticatie ---
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddAuthorization();
builder.Services
    .AddIdentityApiEndpoints<IdentityUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        // Lengte boven complexiteit: een lange wachtzin is sterker dan "Welkom01!".
        o.Password.RequiredLength = 10;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddErrorDescriber<DutchIdentityErrorDescriber>()
    .AddEntityFrameworkStores<WolselsDbContext>();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "wolsels.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
});
// Sleutels voor de sessiecookies bewaren, anders is iedereen na een herstart uitgelogd.
builder.Services.AddDataProtection()
    .SetApplicationName("Wolsels")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeysPath"] ?? "data/keys"));

// Achter nginx: client-IP en https van de proxy overnemen (de backend is alleen intern bereikbaar).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = builder.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 2);
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "onbekend",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Auth:RateLimitPerMinute", 20),
            Window = TimeSpan.FromMinutes(1),
        }));
});

builder.Services.Configure<BlendOptions>(builder.Configuration.GetSection(BlendOptions.SectionName));
builder.Services.AddSingleton(sp => new BlendCalculator(sp.GetRequiredService<IOptions<BlendOptions>>().Value.MaxComponents));
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WolselsDbContext>();
    var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
    if (Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { } dir) Directory.CreateDirectory(dir);
    db.Database.Migrate();
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapControllers().RequireAuthorization();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
