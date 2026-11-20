namespace ModuleOptimiser.Core;

// Module types and names from ZDPS's Data/ModTable.json. Attack, Support and Guard are the three module types.
public static class ModuleTypes
{
    public sealed record Info(string Name, string Type);

    private static readonly Dictionary<int, Info> Infos = new()
    {
        [5500101] = new("Basic Attack Module", "Attack"),
        [5500102] = new("Advanced Attack Module", "Attack"),
        [5500103] = new("Excellent Attack Module", "Attack"),
        [5500104] = new("EXT - Excellent Guard Module", "Attack"),
        [5500201] = new("Basic Support Module", "Support"),
        [5500202] = new("Advanced Support Module", "Support"),
        [5500203] = new("Excellent Support Module", "Support"),
        [5500204] = new("EXT - Excellent Guard Module", "Support"),
        [5500301] = new("Basic Guard Module", "Guard"),
        [5500302] = new("Advanced Guard Module", "Guard"),
        [5500303] = new("Excellent Guard Module", "Guard"),
        [5500304] = new("EXT - Excellent Guard Module", "Guard"),
    };

    public static Info Get(int configId) =>
        Infos.TryGetValue(configId, out var info) ? info : new Info("Unknown module", "Unknown");
}
