using Mono.Cecil;
using Mono.Cecil.Cil;

bool estateOnly = args.Length == 4 && args[3] == "--estate-only";
if (args.Length != 3 && !estateOnly) throw new ArgumentException("input-assembly bridge-assembly output-assembly [--estate-only]");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
using var original = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
using var bridge = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { AssemblyResolver = resolver });
var module = original.MainModule;
var type = module.Types.Single(t => t.FullName == "Durango.Logic.Clusters.OffServerLink");
var helper = bridge.MainModule.Types.Single(t => t.FullName == "LostHorizon.PC.LauncherSessionBridge");
var active = module.ImportReference(helper.Methods.Single(m => m.Name == "get_Active"));
var gateway = module.ImportReference(helper.Methods.Single(m => m.Name == "get_Gateway"));
var servers = module.ImportReference(helper.Methods.Single(m => m.Name == "get_Servers"));
var allBefore = module.Types.SelectMany(Flatten).SelectMany(t => t.Methods).Where(m => m.HasBody)
    .ToDictionary(m => m.FullName, Signature);
var allowed = new HashSet<string>();

void Prefix(string name, params Instruction[] body)
{
    var method = type.Methods.Single(m => m.Name == name);
    allowed.Add(method.FullName);
    if (method.Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.FullName == helper.FullName)) return;
    var start = method.Body.Instructions[0];
    var il = method.Body.GetILProcessor();
    il.InsertBefore(start, Instruction.Create(OpCodes.Call, active));
    il.InsertBefore(start, Instruction.Create(OpCodes.Brfalse, start));
    foreach (var instruction in body) il.InsertBefore(start, instruction);
    il.InsertBefore(start, Instruction.Create(OpCodes.Ret));
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 2);
}
if (!estateOnly)
{
    Prefix("get_Gateway", Instruction.Create(OpCodes.Call, gateway));
    Prefix("get_MultiServers", Instruction.Create(OpCodes.Call, servers));
    Prefix("get_Enabled", Instruction.Create(OpCodes.Ldc_I4_1));
    Prefix("get_Name", Instruction.Create(OpCodes.Ldstr, "Lost Horizon Brasil"));
    Prefix("FetchServers");
    var title = type.Methods.Single(m => m.Name == "ApplyWindowTitle");
    allowed.Add(title.FullName);
    foreach (var instruction in title.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr))
        if (((string)instruction.Operand).StartsWith("Durango Brasil")) instruction.Operand = "Lost Horizon — Cliente ";
}
// The Brazilian server makes purchase and maintenance free. The legacy paid
// dialog returns silently on zero maintenance. Reuse the existing native free
// request/callback path, retaining its size guard and tile-to-cell conversion.
var estate = module.Types.Single(t => t.FullName == "Durango.UI.EstateGridGroup");
var click = estate.Methods.Single(m => m.Name == "OnExpandEstateClick");
var free = estate.Methods.Single(m => m.Name == "ExpandPersonalEstate");
if (free.ReturnType.FullName != "System.Void" || free.Parameters.Count != 1 ||
    !free.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(click.Parameters.Select(p => p.ParameterType.FullName)))
    throw new InvalidOperationException("Estate request ABI changed");
allowed.Add(click.FullName);
var paidCalls = click.Body.Instructions.Where(i => i.Operand is MethodReference r &&
    r.DeclaringType.FullName == estate.FullName && r.Name == "ExpandEstate").ToArray();
if (paidCalls.Length > 1) throw new InvalidOperationException("Ambiguous estate expansion click");
if (paidCalls.Length == 1) paidCalls[0].Operand = free;
else if (!click.Body.Instructions.Any(i => i.Operand is MethodReference r && r.FullName == free.FullName))
    throw new InvalidOperationException("Estate request path missing");
module.Assembly.Write(args[2]);
using var after = AssemblyDefinition.ReadAssembly(args[2]);
foreach (var method in after.MainModule.Types.SelectMany(Flatten).SelectMany(t => t.Methods).Where(m => m.HasBody))
    if (!allowed.Contains(method.FullName) && allBefore[method.FullName] != Signature(method))
        throw new InvalidOperationException("Unrelated method changed: " + method.FullName);
if (original.FullName != after.FullName || original.MainModule.RuntimeVersion != after.MainModule.RuntimeVersion)
    throw new InvalidOperationException("Unity assembly identity/runtime changed");
var patchedClick = after.MainModule.Types.Single(t => t.FullName == estate.FullName).Methods.Single(m => m.Name == click.Name);
if (patchedClick.Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.FullName == estate.FullName && r.Name == "ExpandEstate") ||
    !patchedClick.Body.Instructions.Any(i => i.Operand is MethodReference r && r.FullName == free.FullName))
    throw new InvalidOperationException("Free estate expansion routing missing");
foreach (var method in after.MainModule.Types.Single(t => t.FullName == type.FullName).Methods.Where(m => allowed.Contains(m.FullName)))
{
    if (method.Name == "ApplyWindowTitle") continue;
    if (method.Body.Instructions[0].OpCode != OpCodes.Call ||
        ((MethodReference)method.Body.Instructions[0].Operand).FullName != active.FullName ||
        method.Body.Instructions[1].OpCode != OpCodes.Brfalse ||
        !method.Body.Instructions.Contains((Instruction)method.Body.Instructions[1].Operand))
        throw new InvalidOperationException("Invalid launcher import/fallback: " + method.Name);
}
Console.WriteLine($"PASS: {allowed.Count} launcher/title/estate methods checked; all other Unity methods preserved.");

static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
{
    yield return type;
    foreach (var child in type.NestedTypes.SelectMany(Flatten)) yield return child;
}
static string Signature(MethodDefinition method)
{
    var code = method.Body.Instructions.ToArray();
    string Operand(object? value) => value switch {
        Instruction jump => "jump:" + Array.IndexOf(code, jump),
        Instruction[] jumps => String.Join(",", jumps.Select(j => Array.IndexOf(code, j))),
        _ => value?.ToString() ?? "null" };
    return String.Join("\n", code.Select(i => i.OpCode.ToString() + " " + Operand(i.Operand)));
}
