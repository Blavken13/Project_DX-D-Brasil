using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2) throw new ArgumentException("EstatePatcher <current Assembly-CSharp.dll> <patched Assembly-CSharp.dll>");
string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (input == output) throw new ArgumentException("Write to a separate file; retain the original client as a backup.");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(input)!);
using var assembly = AssemblyDefinition.ReadAssembly(input,
    new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
var module = assembly.MainModule;
var grid = module.Types.Single(t => t.FullName == "Durango.UI.EstateGridGroup");
var click = grid.Methods.Single(m => m.Name == "OnExpandEstateClick");
var estateField = grid.Fields.Single(f => f.Name == "_expandEstate");
var estateInfo = module.Types.Single(t => t.FullName == "Durango.Logic.Estate.EstateInfo");
var license = module.Types.Single(t => t.FullName == "Messages.EstateLicense");
var owner = module.Types.Single(t => t.FullName == "Shared.Estate.OwnerType");
if (Convert.ToInt32(owner.Fields.Single(f => f.Name == "Player").Constant) != 0)
    throw new InvalidOperationException("Unexpected player owner type.");
var expand = module.Types.Single(t => t.FullName == "EstateSystem").Methods.Single(m => m.Name == "ExpandEstate");
var divide = module.Types.Single(t => t.FullName == "Point2").Methods.Single(m => m.Name == "op_Division" &&
    m.Parameters.Count == 2 && m.Parameters[1].ParameterType.FullName == "System.Int32");
var success = grid.Methods.Single(m => m.Name == "OnSuccess");
var actionCtor = grid.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
    .Select(i => i.Operand).OfType<MethodReference>().First(m => m.Name == ".ctor" &&
        m.DeclaringType.FullName == expand.Parameters[2].ParameterType.FullName);
if (click.Parameters.Count != 1 || click.Parameters[0].ParameterType.FullName != "Point2" ||
    !expand.IsStatic || expand.Parameters.Count != 3 || success.Parameters.Count != 1)
    throw new InvalidOperationException("Unexpected expansion request ABI.");
var before = AllMethods(module).ToDictionary(m => m.FullName, Signature);
bool alreadyPatched = click.Body.Instructions.Any(i => i.Operand is MethodReference m && m.FullName == expand.FullName);
if (!alreadyPatched)
{
    // Extracted from the working Unity 2017 DLL embedded in AplicarPatch.exe:
    // Player estates send the authoritative request directly. Keep all other
    // owner types on their existing paths, including private-island expansion.
    var start = click.Body.Instructions[0];
    var local = new VariableDefinition(license);
    click.Body.Variables.Add(local);
    click.Body.InitLocals = true;
    var prefix = new[]
    {
        Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, estateField),
        Instruction.Create(OpCodes.Callvirt, estateInfo.Methods.Single(m => m.Name == "get_License")),
        Instruction.Create(OpCodes.Stloc, local), Instruction.Create(OpCodes.Ldloca, local),
        Instruction.Create(OpCodes.Ldfld, license.Fields.Single(f => f.Name == "Type")),
        Instruction.Create(OpCodes.Brtrue, start),
        Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, estateField),
        Instruction.Create(OpCodes.Ldfld, estateInfo.Fields.Single(f => f.Name == "Id")),
        Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldc_I4_4),
        Instruction.Create(OpCodes.Call, divide), Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Ldftn, success), Instruction.Create(OpCodes.Newobj, actionCtor),
        Instruction.Create(OpCodes.Call, expand), Instruction.Create(OpCodes.Ret)
    };
    foreach (var instruction in prefix) click.Body.GetILProcessor().InsertBefore(start, instruction);
    click.Body.MaxStackSize = Math.Max(click.Body.MaxStackSize, 5);
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
assembly.Write(output);
using var after = AssemblyDefinition.ReadAssembly(output);
var changed = AllMethods(after.MainModule).Where(m => before[m.FullName] != Signature(m))
    .Select(m => m.FullName).ToArray();
if (changed.Any(m => m != click.FullName) || changed.Length != (alreadyPatched ? 0 : 1) ||
    after.FullName != assembly.FullName || after.MainModule.RuntimeVersion != module.RuntimeVersion ||
    !after.MainModule.AssemblyReferences.Select(r => r.FullName).SequenceEqual(module.AssemblyReferences.Select(r => r.FullName)))
    throw new InvalidOperationException("Unrelated client code or assembly identity changed.");
var patchedClick = after.MainModule.GetType(grid.FullName).Methods.Single(m => m.Name == click.Name);
var code = patchedClick.Body.Instructions;
int sendAt = code.ToList().FindIndex(i => i.Operand is MethodReference m && m.FullName == expand.FullName);
if (sendAt < 0 || code[sendAt + 1].OpCode != OpCodes.Ret ||
    !code.Take(sendAt).Any(i => i.OpCode == OpCodes.Brtrue) ||
    !code.Take(sendAt).Any(i => i.OpCode == OpCodes.Ldc_I4_4) ||
    !code.Take(sendAt).Any(i => i.Operand is MethodReference m && m.FullName == divide.FullName) ||
    !code.Take(sendAt).Any(i => i.OpCode == OpCodes.Ldftn && i.Operand is MethodReference m && m.FullName == success.FullName))
    throw new InvalidOperationException("Direct request, owner guard, tile conversion or callback missing.");
var report = new
{
    input, output, input_sha256 = Sha(input), output_sha256 = Sha(output),
    already_patched = alreadyPatched, changed_methods = changed,
    preserved_methods = before.Count - changed.Length,
    request = expand.FullName, owner_type = "Player", tile_to_cell_divisor = 4,
    callback = success.FullName, other_owner_paths_preserved = true,
    assembly_identity_preserved = true, server_validates_owner_adjacency_and_limit = true
};
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(output + ".validation.json", json + "\n");
Console.WriteLine(json);

static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; }
}
static IEnumerable<MethodDefinition> AllMethods(ModuleDefinition module) => Types(module.Types).SelectMany(t => t.Methods).Where(m => m.HasBody);
static string Signature(MethodDefinition method)
{
    var code = method.Body.Instructions.ToArray();
    string Operand(object? value) => value switch
    {
        Instruction target => "@" + Array.IndexOf(code, target),
        Instruction[] targets => string.Join(",", targets.Select(t => "@" + Array.IndexOf(code, t))),
        _ => value?.ToString() ?? ""
    };
    return string.Join("\n", code.Select(i => i.OpCode + " " + Operand(i.Operand))) +
        "\nlocals:" + string.Join(",", method.Body.Variables.Select(v => v.VariableType.FullName));
}
