using Microsoft.Extensions.Localization;

namespace Wolsels.Api.Localization;

/// <summary>
/// Kiest de taal van meldingen uit de Accept-Language-header (de frontend stuurt de taal van de app mee).
/// De cultuur van het proces verandert niet: getallen en datums in de API zelf blijven cultuuronafhankelijk.
/// </summary>
public sealed class RequestLanguageMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // q=0 means "not acceptable" (RFC 9110), so such a language must never be chosen.
        var language = context.Request.GetTypedHeaders().AcceptLanguage
            .Where(l => (l.Quality ?? 1) > 0)
            .OrderByDescending(l => l.Quality ?? 1)
            .Select(l => Texts.Normalize(l.Value.Value))
            .FirstOrDefault(l => l is not null) ?? Texts.DefaultLanguage;
        Texts.CurrentLanguage = language;
        await next(context);
    }
}

/// <summary>Laat foutmeldingen van validatieattributen (ErrorMessage = sleutel) vertalen via <see cref="Texts"/>.</summary>
public sealed class TextsStringLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] =>
        new(name, Texts.Exists(name) ? Texts.Get(name) : name, resourceNotFound: !Texts.Exists(name));

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, Texts.Exists(name) ? Texts.Get(name, arguments) : string.Format(Texts.CultureFor(Texts.CurrentLanguage), name, arguments),
            resourceNotFound: !Texts.Exists(name));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        Texts.AllTexts[Texts.CurrentLanguage].Select(t => new LocalizedString(t.Key, t.Value));
}
