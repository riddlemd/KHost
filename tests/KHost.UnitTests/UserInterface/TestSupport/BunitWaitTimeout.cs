using System.Runtime.CompilerServices;
using Bunit;

namespace KHost.UnitTests.UserInterface;

internal static class BunitWaitTimeout
{
    // bUnit's one second fails WaitFor calls on a loaded CI runner; a longer wait costs time only when
    // the assertion was going to fail anyway.
    [ModuleInitializer]
    internal static void Lengthen() => BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(10);
}
