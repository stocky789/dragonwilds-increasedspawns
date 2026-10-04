using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

// Makes mounts sprint 25% faster. No mount sets its own speed: every Terrorbird variant takes the
// player's MountSprintSpeed attribute, whose starting value comes from the MountSprintSpeed row of
// DT_Attributes_Player (and the DT_Composite_Attributes_Player table built from it). Only the Flying
// Carpet overrides it, with a gameplay effect that sets the value outright. This scales the table
// row (910) and the carpet override (1540). MountRunSpeed, the walking pace, is left alone.
// The input is a directory of legacy assets (retoc to-legacy -f for each file below), written in the
// same layout to the output directory. Linux server assets carry property tags and Windows ones are
// unversioned; both parse here, so the tool can be run on either.
if (args.Length != 3) throw new ArgumentException("Usage: MountSpeed MAPPINGS.usmap INPUT_DIR OUTPUT_DIR");
const float Multiplier = 1.25f;
const string Row = "MountSprintSpeed";
string[] Tables =
[
    "RSDragonwilds/Content/Gameplay/Attributes/DT_Attributes_Player.uasset",
    "RSDragonwilds/Content/Gameplay/Attributes/DT_Composite_Attributes_Player.uasset",
];
const string CarpetEffect = "RSDragonwilds/Plugins/GameFeatures/UmbralSands/Content/Gameplay/Mounts/GE_Mount_FlyingCarpet_UmSSprintSpeed.uasset";
var usmap = new Usmap(args[0]);
var input = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);

foreach (var table in Tables) Edit(table, TableValue);
Edit(CarpetEffect, CarpetValue);

void Edit(string relative, Func<UAsset, PropertyData> find)
{
    var asset = new UAsset(Path.Combine(input, relative), EngineVersion.VER_UE5_6, usmap);
    var value = find(asset);
    var before = Read(value);
    var expected = before * Multiplier;
    Write(value, expected);
    var destination = Path.Combine(output, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    asset.Write(destination);
    var written = Read(find(new UAsset(destination, EngineVersion.VER_UE5_6, usmap)));
    if (written != expected) throw new InvalidDataException($"{relative} holds {written}, expected {expected}");
    Console.WriteLine($"{relative}: {before} -> {written}");
}

// Table rows keep StartingValue as text; the carpet effect keeps its amount as a float.
static float Read(PropertyData value) => value switch
{
    StrPropertyData text => float.Parse(text.Value!.Value!, System.Globalization.CultureInfo.InvariantCulture),
    FloatPropertyData number => number.Value,
    _ => throw new InvalidDataException($"Unexpected {value.GetType().Name}"),
};

static void Write(PropertyData value, float amount)
{
    if (value is StrPropertyData text) text.Value = new FString(amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    else ((FloatPropertyData)value).Value = amount;
}

static PropertyData TableValue(UAsset asset)
{
    var table = asset.Exports.OfType<DataTableExport>().Single();
    var row = table.Table.Data.Single(r => r.Name.ToString() == Row);
    return row.Value.Single(p => p.Name.ToString() == "StartingValue");
}

static PropertyData CarpetValue(UAsset asset)
{
    var effect = asset.Exports.OfType<NormalExport>().Single(e => e.ObjectName.ToString().StartsWith("Default__"));
    var data = (StructPropertyData)effect.Data.Single(p => p.Name.ToString() == "Data");
    if (!((EnumPropertyData)data.Value.Single(p => p.Name.ToString() == "ModifierOperation")).Value.ToString().EndsWith("Override"))
        throw new InvalidDataException("The carpet sprint effect no longer overrides the attribute");
    var amount = (StructPropertyData)data.Value.Single(p => p.Name.ToString() == "AmountToModify");
    return amount.Value.Single(p => p.Name.ToString() == "Value");
}
