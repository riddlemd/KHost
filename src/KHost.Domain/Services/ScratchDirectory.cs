namespace KHost.Domain.Services;

/// <summary>Clean-up of folders the host can afford to leave behind.</summary>
internal static class ScratchDirectory
{
    /// <summary>Deletes <paramref name="directory"/> and everything under it; a folder that is
    /// missing, locked or otherwise refuses is left for the next attempt.</summary>
    public static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
            // Scratch and superseded folders; a locked one is cleared on the next attempt.
        }
    }
}
