namespace Wandur.Core.Settings;

public static class WindowSkinId
{
    public const string Fleet = "Fleet", Armored = "Armored", System = "System";
    public static IReadOnlyList<string> All { get; } = [Fleet, Armored, System];
    public static string Normalize(string? value) => value switch
    {
        Armored => Armored, System => System, _ => Fleet
    };
}
