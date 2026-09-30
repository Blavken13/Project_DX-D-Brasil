using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class ClientPatcher
{
    internal static void Run(string input, string translationsFile, string output)
    {
        var translations = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(translationsFile))!;
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input))!);
        using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        var module = assembly.MainModule;
        int changed = 0;
        foreach (var type in module.Types.SelectMany(Flatten))
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr))
        {
            var originalText = (string)instruction.Operand;
            int occurrence = method.Body.Instructions.TakeWhile(i => i != instruction)
                .Count(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == originalText);
            var replacement = FindTranslation(translations, type.FullName, method.Name, originalText, occurrence);
            if (replacement is null || replacement == (string)instruction.Operand) continue;
            instruction.Operand = replacement;
            changed++;
        }

        var localize = module.Types.SingleOrDefault(t => t.FullName == "LocalizeSystem");
        bool addLocale = localize is not null && !localize.Methods.Any(m => m.Name == "EnsureBrazilianLocale");
        if (addLocale)
        {
            var references = module.Types.SelectMany(Flatten).SelectMany(t => t.Methods)
                .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand).OfType<MethodReference>().ToArray();
            MethodReference Prefs(string name, int parameters) => references.First(m =>
                m.DeclaringType.FullName == "UnityEngine.PlayerPrefs" && m.Name == name && m.Parameters.Count == parameters);
            var getInt = Prefs("GetInt", 2);
            var setString = Prefs("SetString", 2);
            var setInt = Prefs("SetInt", 2);
            var save = Prefs("Save", 0);
            var ensure = new MethodDefinition("EnsureBrazilianLocale",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
            localize!.Methods.Add(ensure);
            var il = ensure.Body.GetILProcessor();
            var done = il.Create(OpCodes.Ret);
            const string marker = "durango-br:locale-profile-v1";
            il.Append(il.Create(OpCodes.Ldstr, marker));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Call, getInt));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Beq, done));
            il.Append(il.Create(OpCodes.Ldstr, "option:locale"));
            il.Append(il.Create(OpCodes.Ldstr, "pt_BR"));
            il.Append(il.Create(OpCodes.Call, setString));
            il.Append(il.Create(OpCodes.Ldstr, marker));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Call, setInt));
            il.Append(il.Create(OpCodes.Call, save));
            il.Append(done);
            var config = module.Types.Single(t => t.FullName == "Durango.System.Config.ConfigInstance");
            var initialize = config.Methods.Single(m => m.Name == "Initialize");
            initialize.Body.GetILProcessor().InsertBefore(initialize.Body.Instructions[0], Instruction.Create(OpCodes.Call, ensure));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        assembly.Write(output);
        Verify(input, output, translations, addLocale);
        Console.WriteLine($"PASS: {changed} translated client literals; locale profile added={addLocale}; all other instructions preserved.");
    }

    private static string? FindTranslation(Dictionary<string, Dictionary<string, string>> translations, string type, string method, string text, int occurrence = -1)
    {
        foreach (var (scope, mapping) in translations)
        {
            var parts = scope.Split("::", 3);
            if (!(type == parts[0] || type.StartsWith(parts[0] + "/", StringComparison.Ordinal))) continue;
            if (parts.Length >= 2 && method != parts[1]) continue;
            if (parts.Length == 3 && occurrence != int.Parse(parts[2])) continue;
            if (mapping.TryGetValue(text, out var translated)) return translated;
        }
        return null;
    }

    internal static void Verify(string original, string patched,
        Dictionary<string, Dictionary<string, string>> translations, bool addedLocale)
    {
        using var before = AssemblyDefinition.ReadAssembly(original);
        using var after = AssemblyDefinition.ReadAssembly(patched);
        if (before.FullName != after.FullName || before.MainModule.Kind != after.MainModule.Kind ||
            before.MainModule.RuntimeVersion != after.MainModule.RuntimeVersion)
            throw new InvalidOperationException("Assembly identity/runtime changed");
        if (!before.MainModule.AssemblyReferences.Select(r => r.FullName)
            .SequenceEqual(after.MainModule.AssemblyReferences.Select(r => r.FullName)))
            throw new InvalidOperationException("Assembly dependencies changed");
        var patchedTypes = after.MainModule.Types.SelectMany(Flatten).ToDictionary(t => t.FullName);
        foreach (var type in before.MainModule.Types.SelectMany(Flatten))
        {
            var target = patchedTypes[type.FullName];
            if (!type.Fields.Select(f => f.FullName).SequenceEqual(target.Fields.Select(f => f.FullName)))
                throw new InvalidOperationException($"Fields changed: {type.FullName}");
            foreach (var method in type.Methods)
            {
                var modified = target.Methods.Single(m => m.FullName == method.FullName);
                if (!method.HasBody) continue;
                var originals = method.Body.Instructions.ToArray();
                var actual = modified.Body.Instructions.ToArray();
                bool initialize = addedLocale && type.FullName == "Durango.System.Config.ConfigInstance" && method.Name == "Initialize";
                if (initialize)
                {
                    if (actual[0].OpCode != OpCodes.Call || ((MethodReference)actual[0].Operand).Name != "EnsureBrazilianLocale")
                        throw new InvalidOperationException("Locale initialization was not installed correctly");
                    actual = actual.Skip(1).ToArray();
                }
                if (originals.Length != actual.Length) throw new InvalidOperationException($"Instruction count changed: {method.FullName}");
                for (int i = 0; i < originals.Length; i++)
                {
                    var old = originals[i]; var current = actual[i];
                    if (old.OpCode != current.OpCode) throw new InvalidOperationException($"Opcode changed: {method.FullName}:{i}");
                    if (old.OpCode == OpCodes.Ldstr)
                    {
                        int occurrence = originals.Take(i).Count(t => t.OpCode == OpCodes.Ldstr && (string)t.Operand == (string)old.Operand);
                        var expected = FindTranslation(translations, type.FullName, method.Name, (string)old.Operand, occurrence) ?? (string)old.Operand;
                        if ((string)current.Operand != expected) throw new InvalidOperationException($"Unexpected string: {method.FullName}:{i}");
                    }
                    else if (Operand(old.Operand, originals) != Operand(current.Operand, actual))
                        throw new InvalidOperationException($"Non-text instruction changed: {method.FullName}:{i}");
                }
                if (method.Body.ExceptionHandlers.Count != modified.Body.ExceptionHandlers.Count)
                    throw new InvalidOperationException($"Exception handlers changed: {method.FullName}");
            }
        }
    }

    private static string Operand(object? operand, Instruction[] instructions) => operand switch
    {
        Instruction instruction => "target:" + Array.IndexOf(instructions, instruction),
        Instruction[] targets => string.Join(",", targets.Select(t => Array.IndexOf(instructions, t))),
        _ => operand?.ToString() ?? "null"
    };

    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        yield return type;
        foreach (var child in type.NestedTypes.SelectMany(Flatten)) yield return child;
    }
}
