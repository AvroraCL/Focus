using System.Runtime.CompilerServices;
using Focus.Core;

namespace Focus.Tests;

internal static class TestInstance
{
    [ModuleInitializer]
    internal static void Initialize() => FocusInstance.Override = "tests-" + Guid.NewGuid().ToString("N");
}
