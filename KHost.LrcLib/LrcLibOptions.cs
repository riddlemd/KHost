namespace KHost.LrcLib;

public class LrcLibOptions
{
    public const string SectionName = nameof(LrcLibClient);

    public string BaseAddress { get; set; } = "https://lrclib.net/";

    public string UserAgent { get; set; } = "";
}
