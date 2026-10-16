using System.Globalization;
using System.Text.Json;

namespace Wolsels.Api.Localization;

/// <summary>
/// Teksten voor gebruikers (meldingen, e-mails) in de taal van het verzoek. De vertalingen staan in
/// Localization/Texts/&lt;taal&gt;.json; Nederlands is de bron en het terugvalpunt. Parameters zoals {0} werken
/// als bij <see cref="string.Format(IFormatProvider, string, object[])"/>.
/// </summary>
public static class Texts
{
    /// <summary>Talen van de app; gelijk aan die in de frontend.</summary>
    public static readonly IReadOnlyList<string> Languages = ["nl", "de", "en", "fr", "it", "sv", "nb", "da", "es", "pt", "pl"];

    public const string DefaultLanguage = "nl";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> All = Load();

    /// <summary>De tekst in de taal van het huidige verzoek (zie <see cref="RequestLanguageMiddleware"/>).</summary>
    public static string Get(string key, params object?[] args) => GetFor(CurrentLanguage, key, args);

    /// <summary>De tekst in een bepaalde taal, bv. die uit iemands profiel voor een e-mail.</summary>
    public static string GetFor(string? language, string key, params object?[] args)
    {
        var lang = Normalize(language) ?? DefaultLanguage;
        var text = All[lang].GetValueOrDefault(key) ?? All[DefaultLanguage].GetValueOrDefault(key) ?? key;
        return args.Length == 0 ? text : string.Format(CultureFor(lang), text, args);
    }

    public static bool Exists(string key) => All[DefaultLanguage].ContainsKey(key);

    private static readonly AsyncLocal<string?> RequestLanguage = new();

    /// <summary>De taal van het huidige verzoek; Nederlands buiten een verzoek (bv. in tests).</summary>
    public static string CurrentLanguage
    {
        get => RequestLanguage.Value ?? DefaultLanguage;
        set => RequestLanguage.Value = Normalize(value);
    }

    /// <summary>Een ondersteunde taal bij een taalcode zoals 'de', 'de-AT' of 'no', of null.</summary>
    public static string? Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var code = tag.Trim().Split('-', '_')[0].ToLowerInvariant();
        if (code is "no" or "nn") code = "nb";
        return Languages.Contains(code) ? code : null;
    }

    /// <summary>Voor getallen en datums in teksten, bv. "12,5 g" in het Nederlands.</summary>
    public static CultureInfo CultureFor(string language) => CultureInfo.GetCultureInfo(language switch
    {
        "en" => "en-GB",
        "pt" => "pt-PT",
        "nb" => "nb-NO",
        _ => language,
    });

    /// <summary>Alle vertalingen per taal; voor tests.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllTexts => All;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var assembly = typeof(Texts).Assembly;
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var language in Languages)
        {
            var name = $"Wolsels.Api.Localization.Texts.{language}.json";
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Vertaling ontbreekt: {name}");
            result[language] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException($"Vertaling is leeg: {name}");
        }
        return result;
    }
}
