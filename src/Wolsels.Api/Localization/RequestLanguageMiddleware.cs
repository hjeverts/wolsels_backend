using Microsoft.Extensions.Localization;

namespace Wolsels.Api.Localization;

/// <summary>
/// Picks the language of messages from the Accept-Language header (the frontend sends the app language).
/// The process culture does not change: numbers and dates in the API itself stay culture-invariant.
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

/// <summary>Translates error messages of validation attributes (ErrorMessage = key) via <see cref="Texts"/>.</summary>
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
