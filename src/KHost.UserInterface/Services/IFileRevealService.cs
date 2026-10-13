namespace KHost.UserInterface.Services;

/// <summary>Shows a library file in the OS file manager on the machine KHost runs on.</summary>
public interface IFileRevealService
{
    /// <summary>What the action is called on this OS, for a button's tooltip.</summary>
    string Label { get; }

    /// <summary>Whether the file, or failing that the folder it was in, is there to show.</summary>
    bool CanReveal(string? path);

    /// <summary>Opens the file's folder with the file selected; the folder alone when the file is gone.</summary>
    void Reveal(string path);
}
