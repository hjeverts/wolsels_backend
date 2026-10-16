using Microsoft.AspNetCore.Identity;
using Wolsels.Api.Localization;

namespace Wolsels.Api.Auth;

/// <summary>Meldingen van Identity in de taal van het verzoek.</summary>
public class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = Texts.Get("Identity.DuplicateEmail", email) };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = Texts.Get("Identity.DuplicateEmail", userName) };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = Texts.Get("Identity.InvalidEmail", email) };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = Texts.Get("Identity.InvalidEmail", userName) };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = Texts.Get("Identity.PasswordTooShort", length) };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = Texts.Get("Identity.PasswordRequiresUniqueChars", uniqueChars) };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = Texts.Get("Identity.PasswordMismatch") };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = Texts.Get("Identity.InvalidToken") };

    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = Texts.Get("Identity.DefaultError") };
}
