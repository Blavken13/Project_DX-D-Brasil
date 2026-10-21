using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length is < 2 or > 3) throw new ArgumentException("Usage: WorldMapPin <input Assembly-CSharp.dll> <output DLL> [Managed dependency directory]");
if (Path.GetFullPath(args[0]) == Path.GetFullPath(args[1]))
    throw new ArgumentException("Write to a separate output; keep the original for backup.");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
if (args.Length == 3) resolver.AddSearchDirectory(Path.GetFullPath(args[2]));
using var input = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
var originalMethods = AllMethods(input.MainModule).ToDictionary(m => m.FullName, Signature);
var viewer = input.MainModule.GetType("Durango.UI.WorldRoutesViewer");
var set = viewer.Methods.Single(m => m.Name == "Set" && m.Parameters.Count == 1);
const string helperName = "RefreshCurrentRegionCursor";
var helper = viewer.Methods.SingleOrDefault(m => m.Name == helperName);
if (helper is null)
{
    helper = BuildHelper(input.MainModule, viewer);
    viewer.Methods.Add(helper);
    // All four areas have been populated by this point. SetCurrentCursor parents
    // the pin to its node, so the following layout/scroll pass moves both together.
    var layoutCall = set.Body.Instructions.Single(i => i.Operand is MethodReference m &&
        m.DeclaringType.FullName == viewer.FullName && m.Name == "UpdateLayout");
    var anchor = layoutCall.Previous;
    if (anchor.OpCode != OpCodes.Ldarg_0) throw new InvalidOperationException("Unexpected world map Set layout call.");
    var il = set.Body.GetILProcessor();
    il.InsertBefore(anchor, il.Create(OpCodes.Ldarg_0));
    il.InsertBefore(anchor, il.Create(OpCodes.Call, helper));
}
else if (Signature(helper) != Signature(BuildHelper(input.MainModule, viewer)))
    throw new InvalidOperationException("An existing cursor helper has an unexpected implementation.");

VerifyHook(set, helper);
CursorBehaviorChecks.Run(helper);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
input.Write(args[1]);
using var output = AssemblyDefinition.ReadAssembly(args[1]);
int preserved = 0;
foreach (var method in AllMethods(output.MainModule))
{
    if (method.FullName == helper.FullName) continue;
    if (method.FullName == set.FullName) continue;
    if (!originalMethods.TryGetValue(method.FullName, out var signature) || signature != Signature(method))
        throw new InvalidOperationException("Unrelated method changed: " + method.FullName);
    preserved++;
}
var writtenViewer = output.MainModule.GetType(viewer.FullName);
var writtenHelper = writtenViewer.Methods.Single(m => m.Name == helperName);
if (Signature(writtenHelper) != Signature(helper)) throw new InvalidOperationException("Serialized helper differs.");
if (output.FullName != input.FullName || output.MainModule.RuntimeVersion != input.MainModule.RuntimeVersion)
    throw new InvalidOperationException("Unity assembly identity changed.");
VerifyHook(writtenViewer.Methods.Single(m => m.Name == "Set" && m.Parameters.Count == 1), writtenHelper);
CursorBehaviorChecks.Run(writtenHelper);
var report = JsonSerializer.Serialize(new
{
    inputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))),
    changed = set.FullName,
    added = helper.FullName,
    preservedMethods = preserved,
    behaviorChecks = CursorBehaviorChecks.Count,
    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])))
}, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(args[1] + ".manifest.json", report);
Console.WriteLine(report);

static MethodDefinition BuildHelper(ModuleDefinition module, TypeDefinition viewer)
{
    var regionType = module.GetType("Durango.Logic.Explore.Region");
    var gameManager = module.GetType("GameManager");
    var findStable = viewer.Methods.Single(m => m.Name == "GetIslandTransform");
    var findUnstable = viewer.Methods.Single(m => m.Name == "GetUnstableRoutesTransform");
    var setCursor = viewer.Methods.Single(m => m.Name == "SetCurrentCursor");
    var getRegion = gameManager.Methods.Single(m => m.Name == "get_Region");
    var role = regionType.Methods.Single(m => m.Name == "Role");
    var biome = regionType.Methods.Single(m => m.Name == "MajorBiome");
    var level = regionType.Methods.Single(m => m.Name == "get_Level");
    var notEqual = findStable.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>()
        .First(m => m.DeclaringType.FullName == "UnityEngine.Object" && m.Name == "op_Inequality");
    var valid = viewer.Fields.Single(f => f.Name == "_isValidCurrentCursor");
    var helper = new MethodDefinition("RefreshCurrentRegionCursor", MethodAttributes.Private | MethodAttributes.HideBySig, module.TypeSystem.Void);
    helper.Body.InitLocals = true;
    var current = new VariableDefinition(regionType);
    var node = new VariableDefinition(findStable.ReturnType);
    helper.Body.Variables.Add(current);
    helper.Body.Variables.Add(node);
    var il = helper.Body.GetILProcessor();
    var done = il.Create(OpCodes.Ret);
    var found = il.Create(OpCodes.Ldloc, node);
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Ldfld, valid);
    il.Emit(OpCodes.Brtrue, done);
    il.Emit(OpCodes.Call, getRegion);
    il.Emit(OpCodes.Stloc, current);
    il.Emit(OpCodes.Ldloc, current);
    il.Emit(OpCodes.Brfalse, done);
    EmitLookup(findStable);
    il.Emit(OpCodes.Ldloc, node);
    il.Emit(OpCodes.Ldnull);
    il.Emit(OpCodes.Call, notEqual);
    il.Emit(OpCodes.Brtrue, found);
    EmitLookup(findUnstable);
    il.Append(found);
    il.Emit(OpCodes.Ldnull);
    il.Emit(OpCodes.Call, notEqual);
    il.Emit(OpCodes.Brfalse, done);
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Ldloc, node);
    il.Emit(OpCodes.Call, setCursor);
    il.Append(done);
    return helper;

    void EmitLookup(MethodReference lookup)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Callvirt, role);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Callvirt, biome);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Callvirt, level);
        il.Emit(OpCodes.Call, lookup);
        il.Emit(OpCodes.Stloc, node);
    }
}

static void VerifyHook(MethodDefinition set, MethodDefinition helper)
{
    var instructions = set.Body.Instructions;
    var call = instructions.Single(i => i.Operand is MethodReference m && m.FullName == helper.FullName);
    var layout = instructions.Single(i => i.Operand is MethodReference m && m.Name == "UpdateLayout");
    var areaSets = instructions.Where(i => i.Operand is MethodReference m && m.Name == "Set" &&
        m.DeclaringType.Name.StartsWith("WorldRoutes", StringComparison.Ordinal)).ToArray();
    if (areaSets.Length != 4 || instructions.IndexOf(call) <= instructions.IndexOf(areaSets[^1]) ||
        instructions.IndexOf(call) >= instructions.IndexOf(layout))
        throw new InvalidOperationException("Cursor must update after areas are populated and before layout/auto-scroll.");
}

static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
{
    yield return type;
    foreach (var nested in type.NestedTypes.SelectMany(AllTypes)) yield return nested;
}
static IEnumerable<MethodDefinition> AllMethods(ModuleDefinition module) => module.Types.SelectMany(AllTypes).SelectMany(t => t.Methods);
static string Signature(MethodDefinition method)
{
    if (!method.HasBody) return "";
    var code = method.Body.Instructions.ToArray();
    string Operand(object? operand) => operand switch
    {
        Instruction target => "jump:" + Array.IndexOf(code, target),
        Instruction[] targets => string.Join(",", targets.Select(t => Array.IndexOf(code, t))),
        _ => operand?.ToString() ?? "null"
    };
    return string.Join("\n", code.Select(i => i.OpCode + " " + Operand(i.Operand)));
}
