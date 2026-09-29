using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

using System.Collections.Concurrent;

// Triples the amount of undead bones and ectoplasm in every enemy loot row. Drop chances are left
// as authored. Linux rows carry property tags. Windows rows are unversioned, and the mappings predate
// the Recipes field, so it is added to the schema from the Linux layout. The Windows table must then
// reserialize byte for byte and hold the same bone and ectoplasm rows as Linux before it is edited.
if (args.Length != 5) throw new ArgumentException("Usage: LootDrops MAPPINGS.usmap LINUX_IN.uasset LINUX_OUT.uasset WINDOWS_IN.uasset WINDOWS_OUT.uasset");
string[] Items = ["ITEM_Resources_Bone_Undead", "ITEM_Resources_Ectoplasm"];
const int Multiplier = 3;
var usmap = new Usmap(args[0]);
AddRecipes(usmap);
var linux = Triple(args[1], args[2], null);
Triple(args[3], args[4], linux);
foreach (var (key, (min, max)) in linux.OrderBy(x => x.Key)) Console.WriteLine($"{key}: {min}-{max}");

Dictionary<string, (int Min, int Max)> Triple(string input, string output, Dictionary<string, (int Min, int Max)>? reference)
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
    var before = Drops(asset, table).ToDictionary(d => d.Key, d => (d.Min.Value, d.Max.Value));
    if (reference != null && !before.OrderBy(x => x.Key).SequenceEqual(reference.ToDictionary(x => x.Key, x => (x.Value.Min / Multiplier, x.Value.Max / Multiplier)).OrderBy(x => x.Key)))
        throw new InvalidDataException($"{input} drops differ from the Linux table");
    var expected = new Dictionary<string, (int Min, int Max)>();
    foreach (var (key, min, max) in Drops(asset, table))
    {
        min.Value *= Multiplier;
        max.Value *= Multiplier;
        expected[key] = (min.Value, max.Value);
    }
    if (expected.Count == 0) throw new InvalidDataException("No undead bone or ectoplasm drops found");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    asset.Write(output);

    var check = new UAsset(output, EngineVersion.VER_UE5_6, usmap, CustomSerializationFlags.SkipParsingExports);
    var checkTable = Parse<DataTableExport>(check, (RawExport)check.Exports.Single());
    var written = Drops(check, checkTable).ToDictionary(d => d.Key, d => (d.Min.Value, d.Max.Value));
    if (checkTable.Table.Data.Count != table.Table.Data.Count || !written.OrderBy(x => x.Key).SequenceEqual(expected.OrderBy(x => x.Key)))
        throw new InvalidDataException($"{output} drop amounts did not survive serialization");
    return expected;
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

IEnumerable<(string Key, IntPropertyData Min, IntPropertyData Max)> Drops(UAsset source, DataTableExport data)
{
    foreach (var row in data.Table.Data)
    {
        if (row.Value.SingleOrDefault(p => p.Name.ToString() == "Resources") is not ArrayPropertyData resources) continue;
        for (var i = 0; i < resources.Value.Length; i++)
        {
            var resource = ((StructPropertyData)resources.Value[i]).Value;
            var item = ((ObjectPropertyData)resource.Single(p => p.Name.ToString() == "SpawnedItemData")).Value;
            var name = item.IsImport() ? item.ToImport(source).ObjectName.ToString() : "";
            if (!Items.Contains(name)) continue;
            yield return ($"{row.Name}[{i}] {name}", Int(resource, "MinimumDropAmount"), Int(resource, "MaximumDropAmount"));
        }
    }
}

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
