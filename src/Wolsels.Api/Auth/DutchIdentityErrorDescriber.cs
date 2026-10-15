using Microsoft.AspNetCore.Identity;

namespace Wolsels.Api.Auth;

public class DutchIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = $"Er bestaat al een account met {email}." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = $"Er bestaat al een account met {userName}." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = $"'{email}' is geen geldig e-mailadres." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = $"'{userName}' is geen geldig e-mailadres." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Het wachtwoord moet minimaal {length} tekens lang zijn." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"Het wachtwoord moet minimaal {uniqueChars} verschillende tekens bevatten." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Het huidige wachtwoord klopt niet." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "Ongeldige code." };

    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "Er ging iets mis." };
}
