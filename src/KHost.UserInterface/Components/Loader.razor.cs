using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class Loader
{
    public enum LoaderType
    {
        Spinner,
        Dots,
        Bars,
        Pulse,
        Ripple,
        Vinyl,
        Typewriter,
        Kh
    }

    [Parameter] public LoaderType Type { get; set; } = LoaderType.Spinner;
    [Parameter] public string Class { get; set; } = "";
}
