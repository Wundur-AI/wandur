namespace Wandur.Core.Settings;

public static class WindowSkinId
{
    public const string Fleet = "Fleet", Armored = "Armored", System = "System";
    public static IReadOnlyList<string> All { get; } = [Fleet, Armored, System];
    public static string LabelKey(string id) => Normalize(id) switch
    {
        Armored => nameof(Localization.Strings.SkinArmored),
        System => nameof(Localization.Strings.SkinSystem),
        _ => nameof(Localization.Strings.SkinFleet)
    };
    public static string DisplayName(string id) => Normalize(id) switch
    {
        Armored => Localization.Strings.SkinArmored,
        System => Localization.Strings.SkinSystem,
        _ => Localization.Strings.SkinFleet
    };
    public static string Normalize(string? value) => value switch
    {
        Armored => Armored, System => System, _ => Fleet
    };
}
