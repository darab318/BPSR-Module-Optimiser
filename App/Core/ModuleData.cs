using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModuleOptimiser.Core;

// One module: its ID and the stat values it gives (stat ID -> roll value).
public sealed class Module
{
    public string Id = "";
    public int ConfigId;
    public Dictionary<int, int> Stats = new();
}

// Shapes of ModulesSaveData.json, as written by ZDPS.
internal sealed class SaveFile
{
    [JsonPropertyName("ModulesPackage")] public PackageDto ModulesPackage { get; set; }
    [JsonPropertyName("Mod")] public ModDto Mod { get; set; }
}

internal sealed class PackageDto
{
    [JsonPropertyName("Items")] public Dictionary<string, ItemDto> Items { get; set; }
}

internal sealed class ItemDto
{
    [JsonPropertyName("ConfigId")] public int ConfigId { get; set; }
    [JsonPropertyName("ModNewAttr")] public AttrDto ModNewAttr { get; set; }
}

internal sealed class AttrDto
{
    [JsonPropertyName("ModParts")] public List<int> ModParts { get; set; }
}

internal sealed class ModDto
{
    [JsonPropertyName("ModInfos")] public Dictionary<string, InfoDto> ModInfos { get; set; }
}

internal sealed class InfoDto
{
    [JsonPropertyName("InitLinkNums")] public List<int> InitLinkNums { get; set; }
}

public static class ModuleLoader
{
    public static List<Module> Load(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var save = JsonSerializer.Deserialize<SaveFile>(File.ReadAllText(path), options)
                   ?? throw new InvalidDataException("The file is empty.");

        var items = save.ModulesPackage?.Items
                    ?? throw new InvalidDataException("No modules were found in this file.");
        var infos = save.Mod?.ModInfos ?? new Dictionary<string, InfoDto>();

        var modules = new List<Module>();
        foreach (var pair in items)
        {
            if (!infos.TryGetValue(pair.Key, out var info))
            {
                continue;
            }

            var parts = pair.Value.ModNewAttr?.ModParts ?? new List<int>();
            var values = info.InitLinkNums ?? new List<int>();

            var module = new Module { Id = pair.Key, ConfigId = pair.Value.ConfigId };
            for (int i = 0; i < parts.Count && i < values.Count; i++)
            {
                module.Stats[parts[i]] = values[i];
            }

            if (module.Stats.Count > 0)
            {
                modules.Add(module);
            }
        }

        return modules;
    }
}
