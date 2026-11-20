namespace ModuleOptimiser.Core;

// Stat names from ZDPS's Data/ModEffectTable.json (game stat ID -> in-game name).
public static class StatNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [1110] = "Strength Boost",
        [1111] = "Agility Boost",
        [1112] = "Intellect Boost",
        [1113] = "Special Attack",
        [1114] = "Elite Strike",
        [1205] = "Healing Boost",
        [1206] = "Healing Enhance",
        [1307] = "Resistance",
        [1308] = "Armor",
        [1407] = "Cast Focus",
        [1408] = "Attack SPD",
        [1409] = "Crit Focus",
        [1410] = "Luck Focus",
        [2104] = "DMG Stack",
        [2105] = "Agile",
        [2204] = "Life Condense",
        [2205] = "First Aid",
        [2304] = "Final Protection",
        [2404] = "Life Wave",
        [2405] = "Life Steal",
        [2406] = "Team Luck & Crit",
    };

    public static string Name(int statId) =>
        Names.TryGetValue(statId, out var name) ? name : $"Unknown ({statId})";

    public static string Label(int statId) =>
        Names.TryGetValue(statId, out var name) ? $"{name} ({statId})" : $"Unknown ({statId})";
}
