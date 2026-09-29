using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

// Triples the size of every dynamic Dragon Wolf group in the AI spawn tables. Spawn chance, rarity,
// power level and despawn rules are left as authored. Each table's Windows copy is unversioned, so it
// must reserialize byte for byte and hold the same wolf groups as the Linux copy before it is edited.
// Only tables with a Dragon Wolf group are written to the output directories.
if (args.Length != 5) throw new ArgumentException("Usage: SpawnTables MAPPINGS.usmap LINUX_IN_DIR LINUX_OUT_DIR WINDOWS_IN_DIR WINDOWS_OUT_DIR");
const string Wolf = "BP_AI_DragonWolf_Character_C";
const int Multiplier = 3;
var usmap = new Usmap(args[0]);
var total = 0;
foreach (var linuxIn in Directory.EnumerateFiles(args[1], "DT_AISpawnData_*.uasset", SearchOption.AllDirectories).Order())
{
    var relative = Path.GetRelativePath(args[1], linuxIn);
    var linux = Triple(linuxIn, Path.Combine(args[2], relative), null);
    if (linux.Count == 0) continue;
    Triple(Path.Combine(args[3], relative), Path.Combine(args[4], relative), linux);
    foreach (var (key, (min, max)) in linux) Console.WriteLine($"{Path.GetFileNameWithoutExtension(relative)} {key}: {min}-{max}");
    total += linux.Count;
}
if (total == 0) throw new InvalidDataException("No Dragon Wolf groups found");
Console.WriteLine($"{total} Dragon Wolf groups tripled");

Dictionary<string, (int Min, int Max)> Triple(string input, string output, Dictionary<string, (int Min, int Max)>? reference)
{
    var asset = new UAsset(input, EngineVersion.VER_UE5_6, usmap, CustomSerializationFlags.SkipParsingExports);
    var index = asset.Exports.FindIndex(e => e.ObjectName.ToString().StartsWith("DT_", StringComparison.Ordinal));
    var table = Parse<DataTableExport>(asset, (RawExport)asset.Exports[index]);
    asset.Exports[index] = table;
    var expected = new Dictionary<string, (int Min, int Max)>();
    var groups = Groups(table).ToList();
    if (groups.Count == 0) return expected;
    var roundTrip = Path.Combine(Path.GetTempPath(), $"SpawnTables-{Guid.NewGuid():N}.uasset");
    asset.Write(roundTrip);
    var unchanged = File.ReadAllBytes(roundTrip).SequenceEqual(File.ReadAllBytes(input))
        && File.ReadAllBytes(Path.ChangeExtension(roundTrip, ".uexp")).SequenceEqual(File.ReadAllBytes(Path.ChangeExtension(input, ".uexp")));
    File.Delete(roundTrip);
    File.Delete(Path.ChangeExtension(roundTrip, ".uexp"));
    if (!unchanged) throw new InvalidDataException($"{input} does not reserialize unchanged");
    var before = groups.ToDictionary(g => g.Key, g => (g.Min.Value * Multiplier, g.Max.Value * Multiplier));
    if (reference != null && !before.OrderBy(x => x.Key).SequenceEqual(reference.OrderBy(x => x.Key)))
        throw new InvalidDataException($"{input} wolf groups differ from the Linux table");
    foreach (var (key, min, max) in groups)
    {
        min.Value *= Multiplier;
        max.Value *= Multiplier;
        expected[key] = (min.Value, max.Value);
    }
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    asset.Write(output);

    var check = new UAsset(output, EngineVersion.VER_UE5_6, usmap, CustomSerializationFlags.SkipParsingExports);
    var checkTable = Parse<DataTableExport>(check, (RawExport)check.Exports[index]);
    var written = Groups(checkTable).ToDictionary(g => g.Key, g => (g.Min.Value, g.Max.Value));
    if (checkTable.Table.Data.Count != table.Table.Data.Count || !written.OrderBy(x => x.Key).SequenceEqual(expected.OrderBy(x => x.Key)))
        throw new InvalidDataException($"{output} group sizes did not survive serialization");
    return expected;
}

static IEnumerable<(string Key, IntPropertyData Min, IntPropertyData Max)> Groups(DataTableExport data)
{
    foreach (var row in data.Table.Data)
    {
        if (row.Value.SingleOrDefault(p => p.Name.ToString() == "Group") is not ArrayPropertyData group) continue;
        for (var i = 0; i < group.Value.Length; i++)
        {
            var entry = ((StructPropertyData)group.Value[i]).Value;
            if (entry.SingleOrDefault(p => p.Name.ToString() == "AIClass") is not SoftObjectPropertyData ai || ai.Value.AssetPath.AssetName.ToString() != Wolf) continue;
            yield return ($"{row.Name}[{i}]", Int(entry, "MinQuantity"), Int(entry, "MaxQuantity"));
        }
    }
}

static IntPropertyData Int(List<PropertyData> properties, string name)
{
    if (properties.SingleOrDefault(p => p.Name.ToString() == name) is IntPropertyData value) return value;
    throw new InvalidDataException($"Missing {name}");
}

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
