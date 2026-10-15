namespace Wolsels.Api.Services;

public class BlendOptions
{
    public const string SectionName = "Blends";

    /// <summary>Maximaal aantal soorten wol in één mengsel.</summary>
    public int MaxComponents { get; set; } = 6;
}
