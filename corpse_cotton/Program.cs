using System.Security.Cryptography;
using System.Text;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

// Adds three corpse cotton plants around each original plant in one world cell. Linux exports are
// parsed; Windows exports are unversioned, so their references are byte-patched and checked against
// the references found in the Linux copy of the same cell.
if (args.Length != 5) throw new ArgumentException("Usage: CorpseCotton MAPPINGS.usmap LINUX_IN.umap LINUX_OUT.umap WINDOWS_IN.umap WINDOWS_OUT.umap");
const string Prefix = "BP_Plant_Explosive_CorpseCotton_C_";
const string Label = "BP_Plant_Explosive_CorpseCotton";
const int Copies = 3;
const double Radius = 250;
var usmap = new Usmap(args[0]);
var linuxRefs = Clone(args[1], args[2], null);
Clone(args[3], args[4], linuxRefs);

Dictionary<string, List<string>> Clone(string input, string output, Dictionary<string, List<string>>? reference)
{
    var linux = reference == null;
    var asset = Load(input);
    var levelIndex = asset.Exports.FindIndex(e => e.ObjectName.ToString() == "PersistentLevel");
    var level = Parse<LevelExport>(asset, (RawExport)asset.Exports[levelIndex]);
    asset.Exports[levelIndex] = level;
    var originals = Enumerable.Range(1, asset.Exports.Count).Where(i => IsPlant(asset.Exports[i - 1])).ToArray();
    if (originals.Length == 0) throw new InvalidDataException("No corpse cotton plants in map");
    var refs = new Dictionary<string, List<string>>();
    var windowsRefs = linux ? null : RefOffsets(asset, originals);
    var expected = new List<(string Name, Vector Position, Dictionary<string, List<string>> Refs)>();
    var initialActors = level.Actors.Count;

    foreach (var actor in originals)
    {
        var subtree = Subtree(asset, actor);
        var rootOld = subtree.Single(i => asset.Exports[i - 1].ObjectName.ToString() == "DefaultSceneRoot");
        var (origin, up, _) = Transform(Parse<NormalExport>(asset, (RawExport)asset.Exports[rootOld - 1]));
        if (!level.Actors.Any(a => a.Index == actor)) throw new InvalidDataException($"Unlisted plant {asset.Exports[actor - 1].ObjectName}");
        for (var copy = 1; copy <= Copies; copy++)
        {
            var map = subtree.Select((old, k) => (old, k)).ToDictionary(x => x.old, x => asset.Exports.Count + 1 + x.k);
            var id = $"{Path.GetFileNameWithoutExtension(input)}:{asset.Exports[actor - 1].ObjectName}:{copy}";
            var name = $"{asset.Exports[actor - 1].ObjectName}_CCMod{copy}";
            var angle = 2 * Math.PI * (copy - 1) / Copies;
            var (dx, dy) = (Radius * Math.Cos(angle), Radius * Math.Sin(angle));
            // Follow the slope the original plant is tilted to.
            var position = new Vector(origin.X + dx, origin.Y + dy, origin.Z - (up.X * dx + up.Y * dy) / up.Z);
            var cloneRefs = new Dictionary<string, List<string>>();
            foreach (var old in subtree)
            {
                var source = (RawExport)asset.Exports[old - 1];
                var path = RelativeName(asset, actor, old);
                var key = $"{asset.Exports[actor - 1].ObjectName}/{path}";
                Export clone;
                if (old == rootOld || linux)
                {
                    var parsed = Parse<NormalExport>(asset, source);
                    var found = new List<string>();
                    foreach (var p in parsed.Data) Walk(p, x => Track(x, map, asset, actor, found));
                    if (old == rootOld)
                    {
                        if (found.Count != 0) throw new InvalidDataException("Plant root references its actor");
                        SetLocation(parsed, position);
                    }
                    if (old == actor) parsed.Extras = RewriteActorTail(parsed.Extras, id, copy);
                    if (linux && copy == 1) refs[key] = found.Order().ToList();
                    clone = parsed;
                }
                else
                {
                    var data = source.Data.ToArray();
                    var offsets = windowsRefs![(actor, path)];
                    var names = offsets.Select(at => RelativeName(asset, actor, BitConverter.ToInt32(data, at))).Order();
                    if (!names.SequenceEqual(reference![key])) throw new InvalidDataException($"Windows references in {key} do not match Linux");
                    foreach (var at in offsets) BitConverter.GetBytes(map[BitConverter.ToInt32(data, at)]).CopyTo(data, at);
                    if (old == actor) data = RewriteActorTail(data, id, copy);
                    clone = (RawExport)source.Clone();
                    ((RawExport)clone).Data = data;
                }
                clone.ObjectName = old == actor ? FName.FromString(asset, name) : source.ObjectName;
                clone.OuterIndex = Remap(source.OuterIndex, map);
                clone.SerializationBeforeSerializationDependencies = source.SerializationBeforeSerializationDependencies.Select(x => Remap(x, map)).ToList();
                clone.CreateBeforeSerializationDependencies = source.CreateBeforeSerializationDependencies.Select(x => Remap(x, map)).ToList();
                clone.SerializationBeforeCreateDependencies = source.SerializationBeforeCreateDependencies.Select(x => Remap(x, map)).ToList();
                clone.CreateBeforeCreateDependencies = source.CreateBeforeCreateDependencies.Select(x => Remap(x, map)).ToList();
                asset.Exports.Add(clone);
                cloneRefs[path] = (linux ? refs : reference!)[key];
            }
            var newActor = new FPackageIndex(map[actor]);
            level.Actors.Add(newActor);
            foreach (var deps in new[] { level.SerializationBeforeSerializationDependencies, level.CreateBeforeSerializationDependencies, level.SerializationBeforeCreateDependencies, level.CreateBeforeCreateDependencies })
                if (deps.Any(d => d.Index == actor)) deps.Add(newActor);
            expected.Add((name, position, cloneRefs));
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    asset.Write(output);
    Verify(output, linux, initialActors, expected);
    Console.WriteLine($"{(linux ? "linux" : "windows")} {Path.GetFileName(input)}: {originals.Length} corpse cotton plants -> {originals.Length * (Copies + 1)}");
    return refs;
}

void Verify(string output, bool linux, int initialActors, List<(string Name, Vector Position, Dictionary<string, List<string>> Refs)> expected)
{
    var check = Load(output);
    var level = Parse<LevelExport>(check, (RawExport)check.Exports.Single(e => e.ObjectName.ToString() == "PersistentLevel"));
    if (level.Actors.Count != initialActors + expected.Count) throw new InvalidDataException("Plant actor count did not survive serialization");
    var labels = new HashSet<string>();
    var clones = expected.Select(item => check.Exports.FindIndex(e => e.ObjectName.ToString() == item.Name) + 1).ToArray();
    var windowsRefs = linux ? null : RefOffsets(check, clones);
    foreach (var item in expected)
    {
        var actor = check.Exports.FindIndex(e => e.ObjectName.ToString() == item.Name) + 1;
        if (actor == 0 || !level.Actors.Any(a => a.Index == actor)) throw new InvalidDataException($"Missing or unlisted {item.Name}");
        var subtree = Subtree(check, actor);
        if (!subtree.Select(i => RelativeName(check, actor, i)).Order().SequenceEqual(item.Refs.Keys.Order())) throw new InvalidDataException($"Bad components for {item.Name}");
        var own = subtree.ToDictionary(i => i, i => i);
        foreach (var i in subtree)
        {
            var path = RelativeName(check, actor, i);
            var raw = (RawExport)check.Exports[i - 1];
            var found = new List<string>();
            if (linux || path == "DefaultSceneRoot")
                foreach (var p in Parse<NormalExport>(check, raw).Data) Walk(p, x => Track(x, own, check, actor, found));
            else
                found = windowsRefs![(actor, path)].Select(at => RelativeName(check, actor, BitConverter.ToInt32(raw.Data, at))).ToList();
            if (!found.Order().SequenceEqual(item.Refs[path])) throw new InvalidDataException($"Bad references in {item.Name} {path}");
        }
        var root = subtree.Single(i => check.Exports[i - 1].ObjectName.ToString() == "DefaultSceneRoot");
        var (position, _, _) = Transform(Parse<NormalExport>(check, (RawExport)check.Exports[root - 1]));
        if (Math.Abs(position.X - item.Position.X) + Math.Abs(position.Y - item.Position.Y) + Math.Abs(position.Z - item.Position.Z) > .01) throw new InvalidDataException($"Bad position for {item.Name}");
        var data = linux ? Parse<NormalExport>(check, (RawExport)check.Exports[actor - 1]).Extras : ((RawExport)check.Exports[actor - 1]).Data;
        if (!labels.Add(Convert.ToHexString(data[^32..^16]))) throw new InvalidDataException("Duplicate plant GUID");
    }
}

UAsset Load(string path) => new(path, EngineVersion.VER_UE5_6, usmap, CustomSerializationFlags.SkipParsingExports);

static bool IsPlant(Export e) => e.ObjectName.ToString().StartsWith(Prefix, StringComparison.Ordinal) && !e.ObjectName.ToString().Contains("_CCMod", StringComparison.Ordinal);

static int[] Subtree(UAsset asset, int actor) => Enumerable.Range(1, asset.Exports.Count).Where(i =>
{
    for (var at = i; at > 0; at = asset.Exports[at - 1].OuterIndex.Index) if (at == actor) return true;
    return false;
}).ToArray();

// Unversioned Windows data has no property tags, so an object reference is an int32 that points into
// the plant's own components at the same offset, with the same relative target, in every plant.
static Dictionary<(int Actor, string Path), int[]> RefOffsets(UAsset asset, int[] plants)
{
    var subtrees = plants.Select(p => Subtree(asset, p).ToDictionary(i => RelativeName(asset, p, i))).ToArray();
    var paths = subtrees[0].Keys.Order().ToArray();
    if (subtrees.Any(s => !s.Keys.Order().SequenceEqual(paths))) throw new InvalidDataException("Plants have different components");
    var result = new Dictionary<(int, string), int[]>();
    // The root is parsed instead: its location and rotation differ per plant.
    foreach (var path in paths.Where(p => p != "DefaultSceneRoot"))
    {
        var data = subtrees.Select(s => ((RawExport)asset.Exports[s[path] - 1]).Data).ToArray();
        if (data.Any(d => d.Length != data[0].Length)) throw new InvalidDataException($"Plants serialize {path} differently");
        string? Target(int k, int at)
        {
            var value = BitConverter.ToInt32(data[k], at);
            return subtrees[k].ContainsValue(value) ? RelativeName(asset, plants[k], value) : null;
        }
        var offsets = Enumerable.Range(0, data[0].Length - 3)
            .Where(at => Target(0, at) is { } first && Enumerable.Range(1, plants.Length - 1).All(k => Target(k, at) == first)).ToArray();
        for (var k = 0; k < plants.Length; k++) result[(plants[k], path)] = offsets;
    }
    return result;
}

static string RelativeName(UAsset asset, int actor, int index)
{
    var parts = new List<string>();
    for (var at = index; at != actor; at = asset.Exports[at - 1].OuterIndex.Index) parts.Insert(0, asset.Exports[at - 1].ObjectName.ToString());
    return string.Join('.', parts);
}

static FPackageIndex Remap(FPackageIndex index, Dictionary<int, int> map) => map.TryGetValue(index.Index, out var mapped) ? new FPackageIndex(mapped) : index;

static FPackageIndex Track(FPackageIndex index, Dictionary<int, int> map, UAsset asset, int actor, List<string> found)
{
    if (!map.TryGetValue(index.Index, out var mapped)) return index;
    found.Add(RelativeName(asset, actor, index.Index));
    return new FPackageIndex(mapped);
}

static void Walk(PropertyData property, Func<FPackageIndex, FPackageIndex> remap)
{
    switch (property)
    {
        case ObjectPropertyData o: o.Value = remap(o.Value); break;
        case DelegatePropertyData d: d.Value.Object = remap(d.Value.Object); break;
        case MulticastDelegatePropertyData m: foreach (var d in m.Value) d.Object = remap(d.Object); break;
        case ArrayPropertyData a: foreach (var p in a.Value) Walk(p, remap); break;
        case StructPropertyData s: foreach (var p in s.Value) Walk(p, remap); break;
        case MapPropertyData m:
            foreach (var (key, value) in m.Value) { Walk(key, remap); Walk(value, remap); }
            break;
    }
}

static (Vector Position, Vector Up, FRotator Rotation) Transform(NormalExport root)
{
    var location = ((VectorPropertyData)((StructPropertyData)root.Data.Single(p => p.Name.ToString() == "RelativeLocation")).Value.Single()).Value;
    var rotation = root.Data.FirstOrDefault(p => p.Name.ToString() == "RelativeRotation") is StructPropertyData r ? ((RotatorPropertyData)r.Value.Single()).Value : new FRotator(0, 0, 0);
    double Rad(double degrees) => degrees * Math.PI / 180;
    var (sp, cp, sy, cy, sr, cr) = (Math.Sin(Rad(rotation.Pitch)), Math.Cos(Rad(rotation.Pitch)), Math.Sin(Rad(rotation.Yaw)), Math.Cos(Rad(rotation.Yaw)), Math.Sin(Rad(rotation.Roll)), Math.Cos(Rad(rotation.Roll)));
    // Unreal's rotation matrix Z axis: the plant's up direction.
    var up = new Vector(-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp);
    if (up.Z < .5) throw new InvalidDataException("Plant is tilted too steeply to place copies");
    return (new Vector(location.X, location.Y, location.Z), up, rotation);
}

static void SetLocation(NormalExport root, Vector position)
{
    var vector = (VectorPropertyData)((StructPropertyData)root.Data.Single(p => p.Name.ToString() == "RelativeLocation")).Value.Single();
    vector.Value = new FVector(position.X, position.Y, position.Z);
}

static byte[] NewGuid(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16];

static byte[] RewriteActorTail(byte[] input, string id, int copy)
{
    var prefix = Encoding.UTF8.GetBytes(Label);
    var labels = Enumerable.Range(8, input.Length - prefix.Length - 7)
        .Where(i => input.AsSpan(i, prefix.Length).SequenceEqual(prefix)).ToArray();
    if (labels.Length != 1) throw new InvalidDataException("Unexpected actor label position");
    var start = labels[0] - 8;
    if (BitConverter.ToInt32(input, start) != 1) throw new InvalidDataException("Unexpected actor tail");
    var length = BitConverter.ToInt32(input, start + 4);
    var after = start + 8 + length;
    if (length < 2 || after + 32 != input.Length || input[after - 1] != 0) throw new InvalidDataException("Unexpected actor label");
    var oldLabel = Encoding.UTF8.GetString(input, start + 8, length - 1);
    var label = Encoding.UTF8.GetBytes($"{oldLabel}_CCMod{copy}\0");
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

record Vector(double X, double Y, double Z);
