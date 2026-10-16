using System.Globalization;
using System.Text.Json;

namespace Wolsels.Api.Localization;

/// <summary>
/// User-facing texts (messages, emails) in the language of the request. The translations live in
/// Localization/Texts/&lt;language&gt;.json; Dutch is the source and the fallback. Parameters such as {0} work
/// as in <see cref="string.Format(IFormatProvider, string, object[])"/>.
/// </summary>
public static class Texts
{
    /// <summary>Languages of the app; the same as in the frontend.</summary>
    public static readonly IReadOnlyList<string> Languages = ["nl", "de", "en", "fr", "it", "sv", "nb", "da", "es", "pt", "pl"];

    public const string DefaultLanguage = "nl";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> All = Load();

    /// <summary>The text in the language of the current request (see <see cref="RequestLanguageMiddleware"/>).</summary>
    public static string Get(string key, params object?[] args) => GetFor(CurrentLanguage, key, args);

    /// <summary>The text in a specific language, e.g. the one from someone's profile for an email.</summary>
    public static string GetFor(string? language, string key, params object?[] args)
    {
        var lang = Normalize(language) ?? DefaultLanguage;
        var text = All[lang].GetValueOrDefault(key) ?? All[DefaultLanguage].GetValueOrDefault(key) ?? key;
        return args.Length == 0 ? text : string.Format(CultureFor(lang), text, args);
    }

    public static bool Exists(string key) => All[DefaultLanguage].ContainsKey(key);

    private static readonly AsyncLocal<string?> RequestLanguage = new();

    /// <summary>The language of the current request; Dutch outside a request (e.g. in tests).</summary>
    public static string CurrentLanguage
    {
        get => RequestLanguage.Value ?? DefaultLanguage;
        set => RequestLanguage.Value = Normalize(value);
    }

    /// <summary>A supported language for a language tag such as 'de', 'de-AT' or 'no', or null.</summary>
    public static string? Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var code = tag.Trim().Split('-', '_')[0].ToLowerInvariant();
        if (code is "no" or "nn") code = "nb";
        return Languages.Contains(code) ? code : null;
    }

    /// <summary>For numbers and dates in texts, e.g. "12,5 g" in Dutch.</summary>
    public static CultureInfo CultureFor(string language) => CultureInfo.GetCultureInfo(language switch
    {
        "en" => "en-GB",
        "pt" => "pt-PT",
        "nb" => "nb-NO",
        _ => language,
    });

    /// <summary>All translations per language; for tests.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllTexts => All;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var assembly = typeof(Texts).Assembly;
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var language in Languages)
        {
            var name = $"Wolsels.Api.Localization.Texts.{language}.json";
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Translation missing: {name}");
            result[language] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException($"Translation is empty: {name}");
        }
        return result;
    }
}
