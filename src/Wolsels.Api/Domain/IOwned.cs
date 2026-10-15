namespace Wolsels.Api.Domain;

/// <summary>Gegevens die bij één gebruiker horen; andere gebruikers zien ze nooit.</summary>
public interface IOwned
{
    string OwnerId { get; set; }
}
