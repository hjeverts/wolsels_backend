using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Wolsels.Api.Mail;

namespace Wolsels.Api.Tests;

public class WolselsFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wolsels-test-{Guid.NewGuid():N}");

    protected virtual bool AllowRegistration => true;

    /// <summary>Captured emails instead of a real mail server.</summary>
    public FakeMailSender Mail { get; } = new();

    protected virtual void Configure(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:RateLimitPerMinute", "10000");
        builder.UseSetting("Auth:RateLimitPerHour", "10000");
        builder.UseSetting("Auth:PasswordResetPerIpPerHour", "10000");
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Wolsels", $"Data Source={Path.Combine(_dir, "wolsels.db")};Pooling=False");
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(_dir, "keys"));
        builder.UseSetting("Blends:MaxComponents", "3");
        builder.UseSetting("Auth:AllowRegistration", AllowRegistration.ToString());
        builder.UseSetting("App:PublicUrl", "https://wol.example.nl");
        Configure(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IMailSender>(Mail));
    }

    /// <summary>Registers a new user and returns a logged-in client.</summary>
    public async Task<HttpClient> CreateUserClientAsync(string? email = null)
    {
        var client = CreateClient();
        var credentials = new { email = email ?? $"{Guid.NewGuid():N}@example.nl", password = TestJson.Password };
        (await client.PostAsJsonAsync("/api/auth/register", credentials)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/login?useCookies=true", credentials)).EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

public static class TestJson
{
    public const string Password = "correct paard batterij";

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Minimal TOTP implementation (RFC 6238) to imitate an authenticator app.</summary>
public static class Totp
{
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var key = Base32Decode(base32Key);
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var bytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        var hash = HMACSHA1.HashData(key, bytes);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var buffer = 0;
        var output = new List<byte>();
        foreach (var c in input.TrimEnd('=').ToUpperInvariant().Where(c => c != ' '))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits < 8) continue;
            output.Add((byte)(buffer >> (bits - 8)));
            bits -= 8;
        }
        return output.ToArray();
    }
}

public sealed class FakeMailSender : IMailSender
{
    public List<(string To, string Subject, string Text)> Sent { get; } = [];

    public bool IsConfigured => true;

    public Task SendAsync(string to, string subject, string text, string html, CancellationToken ct = default)
    {
        lock (Sent) Sent.Add((to, subject, text));
        return Task.CompletedTask;
    }
}
