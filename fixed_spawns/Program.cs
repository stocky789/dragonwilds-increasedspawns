using System.Security.Cryptography;
using System.Text;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

if (args.Length != 4 || args[3] is not ("windows" or "linux")) throw new ArgumentException("Usage: FixedSpawns INPUT.umap OUTPUT.umap MAPPINGS.usmap windows|linux");
var asset = new UAsset(args[0], EngineVersion.VER_UE5_6, new Usmap(args[2]), CustomSerializationFlags.SkipParsingExports);
var levelIndex = asset.Exports.FindIndex(e => e.ObjectName.ToString() == "PersistentLevel");
var level = Parse<LevelExport>(asset, (RawExport)asset.Exports[levelIndex]);
asset.Exports[levelIndex] = level;
var wolfIndices = asset.Exports.Select((e, i) => (e, i)).Where(x => x.e.ObjectName.ToString().StartsWith("BP_SpawnPoint_DragonWolf_C_", StringComparison.Ordinal) && !x.e.ObjectName.ToString().Contains("_DWMod", StringComparison.Ordinal)).Select(x => x.i).ToArray();
if (args[3] == "linux") foreach (var i in wolfIndices) asset.Exports[i] = Parse<NormalExport>(asset, (RawExport)asset.Exports[i]);
var wolves = wolfIndices.Select(i => asset.Exports[i]).ToArray();
if (wolves.Length == 0) throw new InvalidDataException("No Dragon Wolf spawn points in map");
var initialActors = level.Actors.Count;
var expected = new List<(string Name, Guid Guid)>();

foreach (var wolf in wolves)
{
    var originalIndex = asset.Exports.IndexOf(wolf) + 1;
    var rootIndex = asset.Exports.FindIndex(e => e.OuterIndex.Index == originalIndex && e.ObjectName.ToString() == "Root") + 1;
    var root = Parse<NormalExport>(asset, (RawExport)asset.Exports[rootIndex - 1]);
    asset.Exports[rootIndex - 1] = root;
    if (!level.Actors.Any(a => a.Index == originalIndex)) throw new InvalidDataException($"Unlisted wolf actor {wolf.ObjectName}");
    for (var copy = 1; copy <= 2; copy++)
    {
        var actorIndex = asset.Exports.Count + 1;
        var newRootIndex = actorIndex + 1;
        var id = $"{Path.GetFileNameWithoutExtension(args[0])}:{wolf.ObjectName}:{copy}";
        var actor = (Export)wolf.Clone();
        actor.ObjectName = FName.FromString(asset, wolf.ObjectName + $"_DWMod{copy}");
        expected.Add((actor.ObjectName.ToString(), new Guid(NewGuid(id + ":actor"))));
        var newRoot = (NormalExport)root.Clone();
        newRoot.Data = root.Data.Select(p => (PropertyData)p.Clone()).ToList();
        newRoot.OuterIndex = new FPackageIndex(actorIndex);
        var location = (StructPropertyData)newRoot.Data.Single(p => p.Name.ToString() == "RelativeLocation");
        var vector = (VectorPropertyData)location.Value.Single();
        var old = vector.Value;
        vector.Value = new FVector(old.X + (copy == 1 ? 250 : -250), old.Y, old.Z);

        if (actor is RawExport raw)
        {
            raw.Data = ((RawExport)wolf).Data.ToArray();
            var oldRef = BitConverter.GetBytes(rootIndex);
            var refs = Enumerable.Range(16, raw.Data.Length - 19).Where(i => raw.Data.AsSpan(i, 4).SequenceEqual(oldRef)).ToArray();
            if (refs.Length != 1) throw new InvalidDataException($"Unexpected Root reference in {wolf.ObjectName}");
            var at = refs[0];
            NewGuid(id + ":actor").CopyTo(raw.Data, at - 16);
            BitConverter.GetBytes(newRootIndex).CopyTo(raw.Data, at);
            raw.Data = RewriteActorTail(raw.Data, id, copy);
        }
        else if (actor is NormalExport normal)
        {
            normal.Data = ((NormalExport)wolf).Data.Select(p => (PropertyData)p.Clone()).ToList();
            var guid = (StructPropertyData)normal.Data.Single(p => p.Name.ToString() == "Guid");
            ((GuidPropertyData)guid.Value.Single()).Value = new Guid(NewGuid(id + ":actor"));
            ((ObjectPropertyData)normal.Data.Single(p => p.Name.ToString() == "RootComponent")).Value = new FPackageIndex(newRootIndex);
            normal.Extras = RewriteActorTail(wolf.Extras, id, copy);
        }
        else throw new InvalidDataException($"Unsupported actor export {wolf.GetType().Name}");

        asset.Exports.Add(actor);
        asset.Exports.Add(newRoot);
        level.Actors.Add(new FPackageIndex(actorIndex));
    }
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
asset.Write(args[1]);
var check = new UAsset(args[1], EngineVersion.VER_UE5_6, new Usmap(args[2]), CustomSerializationFlags.SkipParsingExports);
var checkLevel = Parse<LevelExport>(check, (RawExport)check.Exports.Single(e => e.ObjectName.ToString() == "PersistentLevel"));
if (checkLevel.Actors.Count != initialActors + wolves.Length * 2)
    throw new InvalidDataException("Wolf actor count did not survive serialization");
if (expected.Select(x => x.Guid).Distinct().Count() != expected.Count)
    throw new InvalidDataException("Duplicate generated wolf GUID");
foreach (var item in expected)
{
    var matches = check.Exports.Select((e, i) => (e, i)).Where(x => x.e.ObjectName.ToString() == item.Name).ToArray();
    if (matches.Length != 1) throw new InvalidDataException($"Missing or duplicate wolf {item.Name}");
    var actorIndex = matches[0].i + 1;
    if (!checkLevel.Actors.Any(a => a.Index == actorIndex)) throw new InvalidDataException($"Unlisted wolf {item.Name}");
    var roots = check.Exports.Select((e, i) => (e, i)).Where(x => x.e.OuterIndex.Index == actorIndex && x.e.ObjectName.ToString() == "Root").ToArray();
    if (roots.Length != 1) throw new InvalidDataException($"Missing Root for {item.Name}");
    var rootIndex = roots[0].i + 1;
    if (args[3] == "windows")
    {
        var bytes = ((RawExport)matches[0].e).Data;
        var refs = Enumerable.Range(16, bytes.Length - 19).Where(i => bytes.AsSpan(i, 4).SequenceEqual(BitConverter.GetBytes(rootIndex))).ToArray();
        if (refs.Length != 1 || new Guid(bytes.AsSpan(refs[0] - 16, 16)) != item.Guid)
            throw new InvalidDataException($"Bad Root or GUID for {item.Name}");
    }
    else
    {
        var parsed = Parse<NormalExport>(check, (RawExport)matches[0].e);
        var rootRef = (ObjectPropertyData)parsed.Data.Single(p => p.Name.ToString() == "RootComponent");
        var guid = (StructPropertyData)parsed.Data.Single(p => p.Name.ToString() == "Guid");
        if (rootRef.Value.Index != rootIndex || ((GuidPropertyData)guid.Value.Single()).Value != item.Guid)
            throw new InvalidDataException($"Bad Root or GUID for {item.Name}");
    }
}
Console.WriteLine($"{Path.GetFileName(args[0])}: {wolves.Length} fixed points -> {wolves.Length * 3}");

static byte[] NewGuid(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16];

static byte[] RewriteActorTail(byte[] input, string id, int copy)
{
    var prefix = Encoding.UTF8.GetBytes("BP_SpawnPoint_DragonWolf");
    var labels = Enumerable.Range(8, input.Length - prefix.Length - 7)
        .Where(i => input.AsSpan(i, prefix.Length).SequenceEqual(prefix)).ToArray();
    if (labels.Length != 1) throw new InvalidDataException("Unexpected actor label position");
    var start = labels[0] - 8;
    if (BitConverter.ToInt32(input, start) != 1) throw new InvalidDataException("Unexpected actor tail");
    var length = BitConverter.ToInt32(input, start + 4);
    var after = start + 8 + length;
    if (length < 2 || after + 32 != input.Length || input[after - 1] != 0) throw new InvalidDataException("Unexpected actor label");
    var oldLabel = Encoding.UTF8.GetString(input, start + 8, length - 1);
    var label = Encoding.UTF8.GetBytes($"{oldLabel}_DWMod{copy}\0");
    using var stream = new MemoryStream();
    stream.Write(input.AsSpan(0, start + 4));
    stream.Write(BitConverter.GetBytes(label.Length));
    stream.Write(label);
    stream.Write(NewGuid(id + ":label"));
    var optional = input.AsSpan(after + 16, 16);
    stream.Write(optional.IndexOfAnyExcept((byte)0) >= 0 ? NewGuid(id + ":instance") : optional.ToArray());
    return stream.ToArray();
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
