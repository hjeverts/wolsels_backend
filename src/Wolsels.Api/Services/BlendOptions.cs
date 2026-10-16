namespace Wolsels.Api.Services;

public class BlendOptions
{
    public const string SectionName = "Blends";

    /// <summary>Maximum number of wools in one blend.</summary>
    public int MaxComponents { get; set; } = 6;
}
