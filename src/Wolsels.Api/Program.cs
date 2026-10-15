using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolsels.Api.Data;
using Wolsels.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<WolselsDbContext>();

builder.Services.AddDbContext<WolselsDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Wolsels")));

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
