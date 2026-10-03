using System.Runtime.CompilerServices;

namespace Wandur.Core.Tests;

/// <summary>Keeps every test off the real directory. A window or catalog built without an address uses
/// WANDUR_DIRECTORY_URL, and before this each test that opened a window fetched https://api.wandur.net, so a
/// suite run showed up on the site as hundreds of client visits. The address points at a closed loopback port
/// instead. The live tests (WANDUR_LIVE=1) pass their own address, so they are unaffected.</summary>
internal static class OfflineDirectory
{
    internal const string Address = "http://127.0.0.1:9/";

    [ModuleInitializer]
    internal static void Initialize() => Environment.SetEnvironmentVariable("WANDUR_DIRECTORY_URL", Address);
}
