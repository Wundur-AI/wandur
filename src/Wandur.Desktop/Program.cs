using Avalonia;

namespace Wandur.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--script-worker"))
        {
            Console.InputEncoding = new System.Text.UTF8Encoding(false);
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            Wandur.Core.Scripting.ScriptWorker.RunAsync(Console.In, Console.Out).GetAwaiter().GetResult();
            return;
        }
        // The perf probe counts frames through Avalonia's own meters, which exist only when this switch is on before
        // Avalonia starts. Off unless the probe is.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WANDUR_PERF_PROBE")))
            AppContext.SetSwitch("Avalonia.Diagnostics.Diagnostic.IsEnabled", true);
        var dataIndex = Array.IndexOf(args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < args.Length) App.DataDirectory = Path.GetFullPath(args[dataIndex + 1]);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
