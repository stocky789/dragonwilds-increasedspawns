using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

using System.Collections.Concurrent;

// Triples undead bones and ectoplasm in every enemy loot row. Drop chances are left as authored.
// Most drops triple their amount. A drop marked one instance per player spawns a single item per
// player whatever its amount, so it is repeated three times instead. When such a drop has its own
// chance, the three copies must come from one roll: the chance moves to the row and the copies drop
// certainly, while the row's other drops are scaled up so each keeps its overall chance. Linux rows
// carry property tags. Windows rows are unversioned, and old mappings predate the Recipes field, so
// it is added to the schema from the Linux layout when missing. The Windows table must then
// reserialize byte for byte and hold the same bone and ectoplasm drops as Linux before it is edited.
if (args.Length != 5) throw new ArgumentException("Usage: LootDrops MAPPINGS.usmap LINUX_IN.uasset LINUX_OUT.uasset WINDOWS_IN.uasset WINDOWS_OUT.uasset");
string[] Items = ["ITEM_Resources_Bone_Undead", "ITEM_Resources_Ectoplasm"];
const int Multiplier = 3;
var usmap = new Usmap(args[0]);
AddRecipes(usmap);
var linux = Triple(args[1], args[2], null);
Triple(args[3], args[4], linux.Before);
foreach (var drop in linux.After) Console.WriteLine(drop);

(List<string> Before, List<string> After) Triple(string input, string output, List<string>? reference)
{
    var asset = new UAsset(input, EngineVersion.VER_UE5_6, usmap, CustomSerializationFlags.SkipParsingExports);
    var table = Parse<DataTableExport>(asset, (RawExport)asset.Exports.Single());
    asset.Exports[0] = table;
    var roundTrip = Path.Combine(Path.GetTempPath(), $"LootDrops-{Guid.NewGuid():N}.uasset");
    asset.Write(roundTrip);
    var unchanged = File.ReadAllBytes(roundTrip).SequenceEqual(File.ReadAllBytes(input))
        && File.ReadAllBytes(Path.ChangeExtension(roundTrip, ".uexp")).SequenceEqual(File.ReadAllBytes(Path.ChangeExtension(input, ".uexp")));
    File.Delete(roundTrip);
    File.Delete(Path.ChangeExtension(roundTrip, ".uexp"));
    if (!unchanged) throw new InvalidDataException($"{input} does not reserialize unchanged");
    var before = Drops(asset, table);
    if (before.Count == 0) throw new InvalidDataException("No undead bone or ectoplasm drops found");
    if (reference != null && !before.SequenceEqual(reference)) throw new InvalidDataException($"{input} drops differ from the Linux table");

    foreach (var row in table.Table.Data)
    {
        if (row.Value.SingleOrDefault(p => p.Name.ToString() == "Resources") is not ArrayPropertyData resources) continue;
        var all = resources.Value.Cast<StructPropertyData>().ToList();
        var perPlayer = all.Where(r => Items.Contains(ItemName(asset, r.Value)) && PerPlayer(r.Value)).ToList();
        var chances = perPlayer.Select(r => Chance(r.Value).Value).Distinct().ToList();
        if (chances.Count > 1) throw new InvalidDataException($"{row.Name} has per-player drops with different chances");
        if (chances is [var own and < 100])
        {
            if (row.Value.SingleOrDefault(p => p.Name.ToString() == "Recipes") is ArrayPropertyData { Value.Length: > 0 })
                throw new InvalidDataException($"{row.Name} has recipes that would share the moved chance");
            Chance(row.Value).Value *= own / 100;
            foreach (var resource in all)
            {
                var chance = Chance(resource.Value);
                chance.Value = perPlayer.Contains(resource) ? 100 : chance.Value * 100 / own;
                if (chance.Value > 100) throw new InvalidDataException($"{row.Name} has a drop more likely than its per-player drop");
            }
        }
        var edited = new List<PropertyData>();
        foreach (var resource in all)
        {
            edited.Add(resource);
            if (!Items.Contains(ItemName(asset, resource.Value))) continue;
            if (PerPlayer(resource.Value))
                for (var copy = 1; copy < Multiplier; copy++) edited.Add((PropertyData)resource.Clone());
            else
                foreach (var name in new[] { "MinimumDropAmount", "MaximumDropAmount" }) Int(resource.Value, name).Value *= Multiplier;
        }
        resources.Value = [.. edited];
    }
    var expected = Drops(asset, table);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    asset.Write(output);

    var check = new UAsset(output, EngineVersion.VER_UE5_6, usmap, CustomSerializationFlags.SkipParsingExports);
    var checkTable = Parse<DataTableExport>(check, (RawExport)check.Exports.Single());
    if (checkTable.Table.Data.Count != table.Table.Data.Count || !Drops(check, checkTable).SequenceEqual(expected))
        throw new InvalidDataException($"{output} drops did not survive serialization");
    return (before, expected);
}

static void AddRecipes(Usmap usmap)
{
    var row = usmap.Schemas["LootDropTableRow"];
    if (row.Properties.Values.Any(p => p.Name == "Recipes")) return;
    var props = new ConcurrentDictionary<int, UsmapProperty>(row.Properties);
    props[3] = new UsmapProperty("Recipes", 3, 0, 1, new UsmapArrayData(EPropertyType.ArrayProperty) { InnerType = new UsmapStructData("GrantableRecipe") });
    usmap.Schemas[row.Name] = new UsmapSchema(row.Name, row.SuperType, 4, props, false, row.SuperTypeModulePath, false);
    usmap.Schemas["GrantableRecipe"] = new UsmapSchema("GrantableRecipe", null, 3, new ConcurrentDictionary<int, UsmapProperty>
    {
        [0] = new("GrantedRecipe", 0, 0, 1, new UsmapPropertyData(EPropertyType.ObjectProperty)),
        [1] = new("DropChance", 1, 0, 1, new UsmapPropertyData(EPropertyType.FloatProperty)),
        [2] = new("bOnlyForPlayersThatInflictedDamage", 2, 0, 1, new UsmapPropertyData(EPropertyType.BoolProperty)),
    }, false, null, false);
}

List<string> Drops(UAsset source, DataTableExport data)
{
    var drops = new List<string>();
    foreach (var row in data.Table.Data)
    {
        if (row.Value.SingleOrDefault(p => p.Name.ToString() == "Resources") is not ArrayPropertyData resources) continue;
        var all = resources.Value.Cast<StructPropertyData>().Select(r => r.Value).ToList();
        if (!all.Any(r => Items.Contains(ItemName(source, r)))) continue;
        for (var i = 0; i < all.Count; i++)
            drops.Add($"{row.Name} ({Chance(row.Value).Value}%)[{i}] {ItemName(source, all[i])}: {Int(all[i], "MinimumDropAmount").Value}-{Int(all[i], "MaximumDropAmount").Value} at {Chance(all[i]).Value}%{(PerPlayer(all[i]) ? ", one per player" : "")}");
    }
    return drops;
}

static FloatPropertyData Chance(List<PropertyData> properties) => (FloatPropertyData)properties.Single(p => p.Name.ToString() == "DropChance");

static string ItemName(UAsset source, List<PropertyData> resource)
{
    var item = ((ObjectPropertyData)resource.Single(p => p.Name.ToString() == "SpawnedItemData")).Value;
    return item.IsImport() ? item.ToImport(source).ObjectName.ToString() : item.Index.ToString();
}

static bool PerPlayer(List<PropertyData> resource) =>
    resource.SingleOrDefault(p => p.Name.ToString() == "bOneInstancePerPlayerOnlyVisibleToThem") is BoolPropertyData { Value: true };

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
