namespace Wandur.Tests;

/// <summary>How long a test waits for something that should happen before calling it a hang. A test that
/// passes never waits this long: it polls or awaits the event and returns the moment it arrives. The guard
/// only decides when a genuine hang fails, so it is generous, and more so on CI runners, where spawning a
/// worker process and pumping the dispatcher under load has taken longer than the old 5 seconds.</summary>
internal static class TestTimeouts
{
    public static TimeSpan Hang { get; } =
        TimeSpan.FromSeconds(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ? 20 : 60);
}
