using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;
if(args.Length==4 && args[0]=="--server-name") {
    using var nameResolver=new DefaultAssemblyResolver();
    nameResolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    nameResolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    using var nameInput=AssemblyDefinition.ReadAssembly(args[1],new ReaderParameters { InMemory=true,AssemblyResolver=nameResolver });
    using var nameBridge=AssemblyDefinition.ReadAssembly(args[2],new ReaderParameters { AssemblyResolver=nameResolver });
    var nameHelper=nameBridge.MainModule.Types.Single(t=>t.FullName=="LostHorizon.PC.CommunitySessionBridge");
    var nameMethod=nameHelper.Methods.Single(m=>m.Name=="get_ServerName");
    var nameLink=nameInput.MainModule.Types.Single(t=>t.FullName=="Durango.Logic.Clusters.OffServerLink");
    var nameGetter=nameLink.Methods.Single(m=>m.Name=="get_Name");
    nameGetter.Body.Instructions.Clear();
    var nameIl=nameGetter.Body.GetILProcessor();
    nameIl.Append(Instruction.Create(OpCodes.Call,nameInput.MainModule.ImportReference(nameMethod)));
    nameIl.Append(Instruction.Create(OpCodes.Ret));
    nameInput.Write(args[3]);
    Console.WriteLine(JsonSerializer.Serialize(new { patched=nameGetter.FullName, source="CommunitySessionBridge.ServerName" }));
    return;
}
if (args.Length == 3 && args[0] == "--resource-icon-scale")
{
    using var iconResolver = new DefaultAssemblyResolver();
    iconResolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    using var iconInput = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { InMemory = true, AssemblyResolver = iconResolver });
    var iconBefore = iconInput.MainModule.Types.SelectMany(All).SelectMany(t => t.Methods).ToDictionary(m => m.FullName, Signature);
    var iconMethod = SetResourceIconScale(iconInput.MainModule);
    iconInput.Write(args[2]);
    using var iconOutput = AssemblyDefinition.ReadAssembly(args[2]);
    foreach (var method in iconOutput.MainModule.Types.SelectMany(All).SelectMany(t => t.Methods))
        if (method.FullName != iconMethod.FullName && Signature(method) != iconBefore[method.FullName])
            throw new InvalidOperationException("Unrelated method changed: " + method.FullName);
    if (iconOutput.FullName != iconInput.FullName || iconOutput.MainModule.RuntimeVersion != iconInput.MainModule.RuntimeVersion)
        throw new InvalidOperationException("Unity identity changed");
    // Verify the serialized output and ensure running the patch twice is safe.
    var iconAfter = iconOutput.MainModule.Types.Single(t => t.FullName == "Durango.UI.Control.UISpriteLabel").Methods.Single(m => m.Name == "TryTextParse");
    var iconSignature = Signature(iconAfter);
    SetResourceIconScale(iconOutput.MainModule);
    if (Signature(iconAfter) != iconSignature) throw new InvalidOperationException("Icon scale patch is not idempotent");
    var cultures = new[] { "pt-BR", "de-DE", "en-US", "th-TH" }.Select(name =>
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo(name);
        float.TryParse("1.5", System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, culture, out float beforeRatio);
        if (!float.TryParse("1.5", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fixedRatio) || fixedRatio != 1.5f)
            throw new InvalidOperationException("Wrong invariant icon scale");
        return new { culture = name, beforeRatio, fixedRatio };
    }).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { changedMethod = iconMethod.FullName, unchangedMethodsVerified = iconBefore.Count - 1, cultures }));
    return;
}
if(args.Length==3 && args[0]=="--discord-link") {
    using var discordResolver=new DefaultAssemblyResolver();
    discordResolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    using var discordInput=AssemblyDefinition.ReadAssembly(args[1],new ReaderParameters { InMemory=true,AssemblyResolver=discordResolver });
    var discordBefore=discordInput.MainModule.Types.SelectMany(All).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Signature);
    var discordMethod=SetNoticeLink(discordInput.MainModule);
    discordInput.Write(args[2]);
    using var discordOutput=AssemblyDefinition.ReadAssembly(args[2]);
    foreach(var m in discordOutput.MainModule.Types.SelectMany(All).SelectMany(t=>t.Methods))
        if(m.FullName!=discordMethod.FullName && Signature(m)!=discordBefore[m.FullName])throw new InvalidOperationException("Unrelated method changed: "+m.FullName);
    if(discordOutput.FullName!=discordInput.FullName || discordOutput.MainModule.RuntimeVersion!=discordInput.MainModule.RuntimeVersion)throw new InvalidOperationException("Unity identity changed");
    var discordAfter=discordOutput.MainModule.Types.Single(t=>t.FullName=="Durango.System.Platform").Methods.Single(m=>m.Name=="ShowNotice");
    if(!discordAfter.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="https://discord.gg/uHfjEAajq"))throw new InvalidOperationException("Discord link missing");
    Console.WriteLine(JsonSerializer.Serialize(new {changedMethod=discordMethod.FullName,url="https://discord.gg/uHfjEAajq",unchangedMethodsVerified=discordBefore.Count-1}));
    return;
}
if(args.Length!=3) throw new ArgumentException("input bridge output");
using var resolver=new DefaultAssemblyResolver(); resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
using var original=AssemblyDefinition.ReadAssembly(args[0],new ReaderParameters { InMemory=true,AssemblyResolver=resolver });
using var bridge=AssemblyDefinition.ReadAssembly(args[1],new ReaderParameters { AssemblyResolver=resolver });
var module=original.MainModule;
IEnumerable<TypeDefinition> All(TypeDefinition type){yield return type;foreach(var nested in type.NestedTypes.SelectMany(All))yield return nested;}
string Signature(MethodDefinition m){if(!m.HasBody)return "";var code=m.Body.Instructions.ToArray();string Value(object? operand)=>operand switch{Instruction j=>"jump:"+Array.IndexOf(code,j),Instruction[] js=>string.Join(",",js.Select(j=>Array.IndexOf(code,j))),_=>operand?.ToString()??"null"};return string.Join("\n",code.Select(i=>i.OpCode+" "+Value(i.Operand)));}
var before=module.Types.SelectMany(All).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName,Signature);
var changed=new HashSet<string>();
var helper=bridge.MainModule.Types.Single(t=>t.FullName=="LostHorizon.PC.CommunitySessionBridge");
MethodReference Helper(string name)=>module.ImportReference(helper.Methods.Single(m=>m.Name==name));
var link=module.Types.Single(t=>t.FullName=="Durango.Logic.Clusters.OffServerLink");
void Prefix(string name,params Instruction[] body){var method=link.Methods.Single(m=>m.Name==name);var first=method.Body.Instructions[0];var il=method.Body.GetILProcessor();il.InsertBefore(first,Instruction.Create(OpCodes.Call,Helper("get_Active")));il.InsertBefore(first,Instruction.Create(OpCodes.Brfalse,first));foreach(var i in body)il.InsertBefore(first,i);il.InsertBefore(first,Instruction.Create(OpCodes.Ret));changed.Add(method.FullName);}
Prefix("get_Gateway",Instruction.Create(OpCodes.Call,Helper("get_Gateway")));
Prefix("get_MultiServers",Instruction.Create(OpCodes.Call,Helper("get_Servers")));
Prefix("get_Enabled",Instruction.Create(OpCodes.Ldc_I4_1));
Prefix("get_Name",Instruction.Create(OpCodes.Call,Helper("get_ServerName")));
Prefix("get_AccountId",Instruction.Create(OpCodes.Call,Helper("get_AccountId")));
Prefix("FetchServers");
var http=module.Types.Single(t=>t.FullName=="Durango.Utils.Http").Methods.Single(m=>m.Name=="Request"&&m.Parameters.Count==6);
var hil=http.Body.GetILProcessor();var hfirst=http.Body.Instructions[0];
foreach(var i in new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_S,http.Parameters[4]),Instruction.Create(OpCodes.Call,Helper("InjectFields")),Instruction.Create(OpCodes.Starg_S,http.Parameters[4])})hil.InsertBefore(hfirst,i);
changed.Add(http.FullName);
changed.Add(SetNoticeLink(module).FullName);
changed.Add(SetResourceIconScale(module).FullName);
// Brand only displayed strings; keep all gameplay, voice, season pass and asset-loading behavior.
var title=link.Methods.Single(m=>m.Name=="ApplyWindowTitle");foreach(var i in title.Body.Instructions.Where(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="UnityEngine.Application"&&r.Name=="get_productName")){i.OpCode=OpCodes.Ldstr;i.Operand="Lost Horizon";changed.Add(title.FullName);}
foreach(var reference in module.AssemblyReferences.Where(r=>r.Name=="Durango.Localization.Overrides"))reference.Name="LostHorizon.Localization.Overrides";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);original.Write(args[2]);
using var output=AssemblyDefinition.ReadAssembly(args[2]);int preserved=0;
foreach(var m in output.MainModule.Types.SelectMany(All).SelectMany(t=>t.Methods)){if(changed.Contains(m.FullName))continue;if(Signature(m)!=before[m.FullName])throw new InvalidOperationException("Unrelated method changed: "+m.FullName);preserved++;}
if(output.FullName!=original.FullName||output.MainModule.RuntimeVersion!=original.MainModule.RuntimeVersion)throw new InvalidOperationException("Unity ABI changed");
using var localization=AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,"Durango.Localization.Overrides.dll"));localization.Name.Name="LostHorizon.Localization.Overrides";localization.MainModule.Name="LostHorizon.Localization.Overrides.dll";localization.Write(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!,"LostHorizon.Localization.Overrides.dll"));
Console.WriteLine(JsonSerializer.Serialize(new{patched=changed.ToArray(),unchangedMethodsVerified=preserved,unityRuntime=output.MainModule.RuntimeVersion}));

static MethodDefinition SetNoticeLink(ModuleDefinition target) {
    var platform=target.Types.Single(t=>t.FullName=="Durango.System.Platform");
    if(target.Types.Any(t=>t.BaseType?.FullName==platform.FullName&&t.Methods.Any(m=>m.Name=="ShowNotice")))throw new InvalidOperationException("Platform override must be inspected");
    var notice=platform.Methods.Single(m=>m.Name=="ShowNotice"&&m.Parameters.Count==0);
    var urls=notice.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldstr&&i.Operand is string str&&(str.StartsWith("https://m.nexon.com/notice?")||str=="https://discord.gg/uHfjEAajq")).ToArray();
    if(urls.Length!=1 || urls[0].Next.Operand is not MethodReference open || open.DeclaringType.FullName!="UnityEngine.Application" || open.Name!="OpenURL")throw new InvalidOperationException("Unexpected notice link binding");
    urls[0].Operand="https://discord.gg/uHfjEAajq";
    return notice;
}

static MethodDefinition SetResourceIconScale(ModuleDefinition target)
{
    var label = target.Types.Single(t => t.FullName == "Durango.UI.Control.UISpriteLabel");
    var method = label.Methods.Single(m => m.Name == "TryTextParse");
    var calls = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference r &&
        r.DeclaringType.FullName == "System.Single" && r.Name == "TryParse").ToArray();
    if (calls.Length != 1) throw new InvalidOperationException("Unexpected icon ratio parser");
    var call = calls[0];
    var parse = (MethodReference)call.Operand;
    if (parse.Parameters.Count == 4)
    {
        if (parse.Parameters[1].ParameterType.FullName != "System.Globalization.NumberStyles" ||
            parse.Parameters[2].ParameterType.FullName != "System.IFormatProvider" ||
            call.Previous?.Previous?.Operand is not MethodReference getter || getter.FullName != "System.Globalization.CultureInfo System.Globalization.CultureInfo::get_InvariantCulture()" ||
            call.Previous.Previous.Previous?.OpCode != OpCodes.Ldc_I4 ||
            (int)call.Previous.Previous.Previous.Operand != (int)System.Globalization.NumberStyles.Float)
            throw new InvalidOperationException("Unexpected patched icon parser");
        return method;
    }
    if (parse.Parameters.Count != 2 || parse.Parameters[0].ParameterType.FullName != "System.String" ||
        parse.Parameters[1].ParameterType.FullName != "System.Single&" ||
        (call.Previous.OpCode != OpCodes.Ldloca_S && call.Previous.OpCode != OpCodes.Ldloca))
        throw new InvalidOperationException("Unexpected icon ratio arguments");
    // Use Unity's existing core-library scope; importing host .NET 9 types would break the Unity ABI.
    var scope = parse.DeclaringType.Scope;
    var cultureInfo = new TypeReference("System.Globalization", "CultureInfo", target, scope);
    var numberStyles = new TypeReference("System.Globalization", "NumberStyles", target, scope, true);
    var formatProvider = new TypeReference("System", "IFormatProvider", target, scope);
    var invariant = new MethodReference("get_InvariantCulture", cultureInfo, cultureInfo) { HasThis = false };
    var replacement = new MethodReference("TryParse", target.TypeSystem.Boolean, parse.DeclaringType) { HasThis = false };
    replacement.Parameters.Add(new ParameterDefinition(target.TypeSystem.String));
    replacement.Parameters.Add(new ParameterDefinition(numberStyles));
    replacement.Parameters.Add(new ParameterDefinition(formatProvider));
    replacement.Parameters.Add(new ParameterDefinition(new ByReferenceType(target.TypeSystem.Single)));
    var il = method.Body.GetILProcessor();
    var outputArgument = call.Previous;
    il.InsertBefore(outputArgument, Instruction.Create(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.Float));
    il.InsertBefore(outputArgument, Instruction.Create(OpCodes.Call, invariant));
    call.Operand = replacement;
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 8);
    return method;
}
