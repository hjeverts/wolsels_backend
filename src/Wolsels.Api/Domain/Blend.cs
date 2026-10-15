namespace Wolsels.Api.Domain;

public enum CalculationBasis
{
    /// <summary>Gewichten worden berekend vanuit het totale inputgewicht.</summary>
    TotalWeight,
    /// <summary>Gewichten worden berekend vanuit het gewicht van één component.</summary>
    ComponentWeight,
}

public enum BlendStatus
{
    Planned,
    Spun,
}

/// <summary>Een mengsel van meerdere soorten wol in een vaste verhouding.</summary>
public class Blend : IHouseholdData
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public BlendStatus Status { get; set; }
    /// <summary>Of de gewichten van de componenten van de wolvoorraad zijn afgeboekt.</summary>
    public bool StockDeducted { get; set; }
    public CalculationBasis Basis { get; set; }
    /// <summary>Index (0-gebaseerd) van de component waarop is gerekend bij <see cref="CalculationBasis.ComponentWeight"/>.</summary>
    public int? BasisComponentIndex { get; set; }
    /// <summary>Totaal inputgewicht in gram (som van de componenten).</summary>
    public decimal InputWeightGrams { get; set; }

    // --- Resultaat na het spinnen ---

    /// <summary>Datum waarop het mengsel gesponnen is.</summary>
    public DateOnly? SpunOn { get; set; }
    /// <summary>Opbrengst: werkelijk outputgewicht in gram na het spinnen.</summary>
    public decimal? OutputWeightGrams { get; set; }
    /// <summary>Lengte van het garen in meter.</summary>
    public decimal? OutputLengthMeters { get; set; }
    /// <summary>Garendikte als categorie, bv. Lace, Fingering, DK, Worsted, Bulky.</summary>
    public string? YarnWeight { get; set; }
    /// <summary>Wraps per inch: gemeten dikte van het garen.</summary>
    public int? WrapsPerInch { get; set; }
    /// <summary>Aantal draden waarmee getwijnd is (1 = enkeldraads).</summary>
    public int? PlyCount { get; set; }
    /// <summary>Twijnwijze, bv. 2-draads, kettinggetwijnd (Navajo), cabled.</summary>
    public string? PlyMethod { get; set; }
    /// <summary>Spinrichting van de enkele draad: S of Z (twijnen gebeurt in tegenovergestelde richting).</summary>
    public string? SpinDirection { get; set; }

    // --- Verven ---

    public bool IsDyed { get; set; }
    public string? DyeColor { get; set; }
    public string? DyeColorHex { get; set; }
    /// <summary>Gebruikte verfstof, bv. zuurverf, meekrap, indigo.</summary>
    public string? DyeProduct { get; set; }
    /// <summary>Werkwijze van het verven (recept, temperatuur, tijd, beitsen, ...).</summary>
    public string? DyeMethod { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<BlendComponent> Components { get; set; } = [];
}

public class BlendComponent
{
    public int Id { get; set; }
    public int BlendId { get; set; }
    public int Position { get; set; }
    public int WoolId { get; set; }
    public Wool? Wool { get; set; }
    /// <summary>Aandeel in de verhouding (delen of procenten, wordt genormaliseerd).</summary>
    public decimal Ratio { get; set; }
    public decimal WeightGrams { get; set; }
}
