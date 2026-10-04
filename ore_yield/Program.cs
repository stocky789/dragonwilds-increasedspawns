using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

// Doubles the ore a mining node gives. Every mining Blueprint under World/Mining lists what it drops
// in an ItemDropComponent (ore nodes, dropped per hit) or an ItemDropOnDestructionComponent (rocks
// that break once). Each entry has MinToDrop and MaxToDrop, which are doubled for the ores below.
// Gems and other chance-based extras keep their amounts and chances, and bulk building rock (stone,
// granite, sandstone), dragon teeth and soul stone are left alone. Every item found must be listed
// as doubled or left alone, so a game update that adds an ore fails loudly instead of being skipped.
// The input is a directory of legacy assets (retoc to-legacy -f World/Mining/), written in the same
// layout to the output directory. Linux server assets carry property tags and Windows ones are
// unversioned; both parse here, so the tool can be run on either.
if (args.Length != 3) throw new ArgumentException("Usage: OreYield MAPPINGS.usmap INPUT_DIR OUTPUT_DIR");
string[] Doubled =
[
    "ITEM_Resources_CopperOre", "ITEM_Resources_TinOre", "ITEM_Resources_IronOre", "ITEM_Resources_SilverOre",
    "ITEM_Resources_GoldOre", "ITEM_Resources_MithrilOre", "ITEM_Resources_AdamantiteOre", "ITEM_Resources_BluriteOre",
    "ITEM_Resources_Ore_Runite", "ITEM_Resources_Ore_Luminite", "ITEM_Resources_Coal", "ITEM_Resources_Clay",
    "ITEM_Rune_Essence", "ITEM_Resources_Gypsum", "ITEM_Resources_Limestone",
];
string[] LeftAlone =
[
    "ITEM_Resources_Ruby", "ITEM_Resources_Sapphire", "ITEM_Resources_Jade", "ITEM_Resources_Opal",
    "ITEM_Resources_Red_Topaz", "ITEM_Resources_Diamond", "ITEM_Resources_Gem_Dragonstone",
    "ITEM_Resources_Stone", "ITEM_Resources_Granite", "ITEM_Resources_Sandstone",
    "ITEM_Resources_Dragon_Tooth", "ITEM_Currency_SoulFragment",
];
const int Multiplier = 2;
string[] Components = ["ItemDropComponent", "ItemDropOnDestructionComponent"];
var usmap = new Usmap(args[0]);
var input = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var seen = new HashSet<string>();
var edited = 0;
foreach (var path in Directory.EnumerateFiles(input, "*.uasset", SearchOption.AllDirectories).Order())
{
    var asset = new UAsset(path, EngineVersion.VER_UE5_6, usmap, CustomSerializationFlags.SkipParsingExports);
    var targets = asset.Exports.Select((e, i) => (e, i)).Where(x => Components.Contains(x.e.ObjectName.ToString())).ToList();
    var changed = false;
    foreach (var (raw, index) in targets)
    {
        var component = Parse<NormalExport>(asset, (RawExport)raw);
        if (component.Data.SingleOrDefault(p => p.Name.ToString() == "ItemsToDrop") is not ArrayPropertyData items) continue;
        foreach (var item in items.Value.Cast<StructPropertyData>())
        {
            var name = ItemName(item.Value);
            if (Doubled.Contains(name))
            {
                Int(item.Value, "MinToDrop").Value *= Multiplier;
                Int(item.Value, "MaxToDrop").Value *= Multiplier;
                changed = true;
            }
            else if (!LeftAlone.Contains(name)) throw new InvalidDataException($"{path} drops {name}, which is neither doubled nor left alone");
            seen.Add(name);
        }
        asset.Exports[index] = component;
    }
    if (!changed) continue;
    var destination = Path.Combine(output, Path.GetRelativePath(input, path));
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    asset.Write(destination);
    Verify(destination, path);
    edited++;
}
var missing = Doubled.Except(seen).ToList();
if (missing.Count > 0) throw new InvalidDataException($"No node drops {string.Join(", ", missing)}");
Console.WriteLine($"Doubled {edited} assets covering {Doubled.Length} resources");

// Reads the written asset back and checks every entry is doubled exactly once against the original.
void Verify(string written, string original)
{
    var before = Entries(original);
    var after = Entries(written);
    if (before.Count != after.Count) throw new InvalidDataException($"{written} lost drop entries");
    for (var i = 0; i < before.Count; i++)
    {
        var factor = Doubled.Contains(before[i].Item) ? Multiplier : 1;
        if (before[i].Item != after[i].Item || before[i].Min * factor != after[i].Min || before[i].Max * factor != after[i].Max || before[i].Chance != after[i].Chance)
            throw new InvalidDataException($"{written} entry {before[i].Item} did not survive serialization");
    }
}

List<(string Item, int Min, int Max, float Chance)> Entries(string path)
{
    var asset = new UAsset(path, EngineVersion.VER_UE5_6, usmap, CustomSerializationFlags.SkipParsingExports);
    var result = new List<(string, int, int, float)>();
    foreach (var raw in asset.Exports.Where(e => Components.Contains(e.ObjectName.ToString())))
    {
        var component = raw as NormalExport ?? Parse<NormalExport>(asset, (RawExport)raw);
        if (component.Data.SingleOrDefault(p => p.Name.ToString() == "ItemsToDrop") is not ArrayPropertyData items) continue;
        foreach (var item in items.Value.Cast<StructPropertyData>())
            result.Add((ItemName(item.Value), Int(item.Value, "MinToDrop").Value, Int(item.Value, "MaxToDrop").Value,
                ((FloatPropertyData)item.Value.Single(p => p.Name.ToString() == "ProbabilityOfDrop")).Value));
    }
    return result;
}

static string ItemName(List<PropertyData> entry) =>
    ((SoftObjectPropertyData)entry.Single(p => p.Name.ToString() == "ItemDataClass")).Value.AssetPath.AssetName.ToString().Split('.').Last();

static IntPropertyData Int(List<PropertyData> properties, string name) => (IntPropertyData)properties.Single(p => p.Name.ToString() == name);

static T Parse<T>(UAsset asset, RawExport source) where T : Export, new()
{
    var result = new T();
    foreach (var field in typeof(Export).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        field.SetValue(result, field.GetValue(source));
    using var reader = new AssetBinaryReader(new MemoryStream(source.Data), asset);
    result.Read(reader, source.Data.Length);
    result.Extras = source.Data[(int)reader.BaseStream.Position..];
    return result;
}
