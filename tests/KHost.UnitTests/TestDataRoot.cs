using System.Runtime.CompilerServices;
using KHost.Common.Storage;

namespace KHost.UnitTests;

internal static class TestDataRoot
{
    // Before any test touches AppPaths: on macOS its default is the user's real Application Support folder.
    [ModuleInitializer]
    internal static void Isolate()
    {
        var root = Path.Combine(Path.GetTempPath(), "khost-unit-tests", Environment.ProcessId.ToString());
        Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
