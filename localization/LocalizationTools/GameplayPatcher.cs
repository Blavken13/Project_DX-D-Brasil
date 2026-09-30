using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class GameplayPatcher
{
    internal static void Run(string input, string output)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input))!);
        using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        var module = assembly.MainModule;
        var methods = module.Types.SelectMany(Flatten).SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
        var signatures = methods.ToDictionary(m => m.FullName, Signature);
        var references = methods.SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>().ToArray();
        MethodReference Ref(string type, string name) => references.First(r => r.DeclaringType.FullName == type && r.Name == name);

        var worldMap = module.Types.Single(t => t.FullName == "Durango.UI.WorldMapGroup");
        var finder = worldMap.Methods.Single(m => m.Name == "ContextActionFinder");
        var switchInstruction = finder.Body.Instructions.Single(i => i.OpCode == OpCodes.Switch);
        var targets = (Instruction[])switchInstruction.Operand;
        int offset = switchInstruction.Previous.OpCode == OpCodes.Sub ? Integer(switchInstruction.Previous.Previous) : 0;
        if (3 - offset < 0 || 7 - offset >= targets.Length) throw new InvalidOperationException("Unexpected role switch layout");
        targets[4 - offset] = targets[3 - offset]; // Risky -> mesmo fluxo do porto em Rural
        targets[7 - offset] = targets[3 - offset]; // Safehouse -> mesmo fluxo do porto em Rural

        var grid = module.Types.Single(t => t.FullName == "Durango.UI.EstateGridGroup");
        var estateField = grid.Fields.Single(f => f.Name == "_expandEstate");
        var estateInfo = module.Types.Single(t => t.FullName == "Durango.Logic.Estate.EstateInfo");
        var getLicense = estateInfo.Methods.Single(m => m.Name == "get_License");
        var licenseType = module.Types.Single(t => t.FullName == "Messages.EstateLicense");
        var ownerType = licenseType.Fields.Single(f => f.Name == "Type");
        var expand = Ref("EstateSystem", "ExpandEstate");
        var divide = references.First(r => r.DeclaringType.FullName == "Point2" && r.Name == "op_Division" &&
            r.Parameters.Count == 2 && r.Parameters[1].ParameterType.FullName == "System.Int32");
        var actionCtor = references.First(r => r.Name == ".ctor" && r.DeclaringType.FullName == expand.Parameters[2].ParameterType.FullName);
        var onSuccess = grid.Methods.Single(m => m.Name == "OnSuccess");
        var click = grid.Methods.Single(m => m.Name == "OnExpandEstateClick");
        var originalStart = click.Body.Instructions[0];
        var licenseLocal = new VariableDefinition(licenseType);
        click.Body.Variables.Add(licenseLocal);
        click.Body.InitLocals = true;
        var clickPrefix = new[]
        {
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, estateField),
            Instruction.Create(OpCodes.Callvirt, getLicense), Instruction.Create(OpCodes.Stloc, licenseLocal),
            Instruction.Create(OpCodes.Ldloca, licenseLocal), Instruction.Create(OpCodes.Ldfld, ownerType),
            Instruction.Create(OpCodes.Brtrue, originalStart), // OwnerType.Player = 0
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, estateField),
            Instruction.Create(OpCodes.Ldfld, estateInfo.Fields.Single(f => f.Name == "Id")),
            Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldc_I4_4), Instruction.Create(OpCodes.Call, divide),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldftn, onSuccess), Instruction.Create(OpCodes.Newobj, actionCtor),
            Instruction.Create(OpCodes.Call, expand), Instruction.Create(OpCodes.Ret)
        };
        foreach (var instruction in clickPrefix) click.Body.GetILProcessor().InsertBefore(originalStart, instruction);

        var buttons = grid.Methods.Single(m => m.Name == "AddExpandButtons");
        var freeLabel = buttons.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "확장 {0}무료!");
        var store = freeLabel;
        while (store is not null && !IsStore(store)) store = store.Next;
        if (store is null) throw new InvalidOperationException("Button label variable not found");
        var buttonLocal = Local(store, buttons);
        if (buttonLocal.VariableType.FullName != "System.String") throw new InvalidOperationException("Wrong button variable");
        var next = store.Next;
        var labelLicense = new VariableDefinition(licenseType);
        buttons.Body.Variables.Add(labelLicense);
        buttons.Body.InitLocals = true;
        var labelPrefix = new[]
        {
            Instruction.Create(OpCodes.Call, Ref("GameManager", "get_Region")),
            Instruction.Create(OpCodes.Callvirt, Ref("Durango.Logic.Explore.Region", "get_Level")),
            Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)10), Instruction.Create(OpCodes.Bgt, next),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, estateField),
            Instruction.Create(OpCodes.Callvirt, getLicense), Instruction.Create(OpCodes.Stloc, labelLicense),
            Instruction.Create(OpCodes.Ldloca, labelLicense), Instruction.Create(OpCodes.Ldfld, ownerType), Instruction.Create(OpCodes.Brtrue, next),
            Instruction.Create(OpCodes.Ldstr, "Expandir grátis"), Instruction.Create(OpCodes.Stloc, buttonLocal)
        };
        foreach (var instruction in labelPrefix) buttons.Body.GetILProcessor().InsertBefore(next, instruction);

        var changed = new HashSet<string> { finder.FullName, click.FullName, buttons.FullName };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        assembly.Write(output);
        using var patched = AssemblyDefinition.ReadAssembly(output);
        foreach (var method in patched.MainModule.Types.SelectMany(Flatten).SelectMany(t => t.Methods).Where(m => m.HasBody))
            if (!changed.Contains(method.FullName) && signatures[method.FullName] != Signature(method))
                throw new InvalidOperationException("Unrelated method changed: " + method.FullName);
        Verify(output);
        Console.WriteLine("PASS: anchor enabled for Risky/Safehouse; estate expansion uses authoritative server cost; free level-10 label; all other methods preserved.");
    }

    private static string Signature(MethodDefinition m) => string.Join("\n", m.Body.Instructions.Select(i => i.ToString()));

    internal static void Verify(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var map = assembly.MainModule.Types.Single(t => t.FullName == "Durango.UI.WorldMapGroup");
        var estate = assembly.MainModule.Types.Single(t => t.FullName == "Durango.UI.EstateGridGroup");
        foreach (var method in new[] { map.Methods.Single(m => m.Name == "ContextActionFinder"),
            estate.Methods.Single(m => m.Name == "OnExpandEstateClick"), estate.Methods.Single(m => m.Name == "AddExpandButtons") })
            VerifyStack(method);
        Console.WriteLine("PASS: stack and branch targets are valid in all three patched gameplay methods.");
    }

    private static void VerifyStack(MethodDefinition method)
    {
        var instructions = method.Body.Instructions.ToHashSet();
        var depths = new Dictionary<Instruction, int>();
        var pending = new Queue<(Instruction Instruction, int Depth)>();
        pending.Enqueue((method.Body.Instructions[0], 0));
        foreach (var handler in method.Body.ExceptionHandlers)
            pending.Enqueue((handler.HandlerStart, handler.HandlerType == ExceptionHandlerType.Catch ? 1 : 0));
        while (pending.TryDequeue(out var current))
        {
            var (instruction, depth) = current;
            if (!instructions.Contains(instruction)) throw new InvalidOperationException("Branch outside method: " + method.FullName);
            if (depths.TryGetValue(instruction, out int previous))
            {
                if (previous != depth) throw new InvalidOperationException("Inconsistent stack depth: " + method.FullName + ":" + instruction);
                continue;
            }
            depths[instruction] = depth;
            int pop, push;
            if (instruction.OpCode == OpCodes.Ret)
            { pop = method.ReturnType.FullName == "System.Void" ? 0 : 1; push = 0; }
            else if (instruction.Operand is MethodReference called && instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
            {
                pop = called.Parameters.Count + (called.HasThis && instruction.OpCode != OpCodes.Newobj ? 1 : 0);
                push = instruction.OpCode == OpCodes.Newobj || called.ReturnType.FullName != "System.Void" ? 1 : 0;
            }
            else
            {
                pop = Count(instruction.OpCode.StackBehaviourPop);
                push = Count(instruction.OpCode.StackBehaviourPush);
            }
            if (depth < pop) throw new InvalidOperationException("Stack underflow: " + method.FullName + ":" + instruction);
            depth = instruction.OpCode.Code is Code.Leave or Code.Leave_S ? 0 : depth - pop + push;
            if (depth > method.Body.MaxStackSize) throw new InvalidOperationException("Stack exceeds declared maximum: " + method.FullName);
            if (instruction.Operand is Instruction target) pending.Enqueue((target, depth));
            else if (instruction.Operand is Instruction[] targets) foreach (var jump in targets) pending.Enqueue((jump, depth));
            if (instruction.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw) && instruction.Next is not null)
                pending.Enqueue((instruction.Next, depth));
        }
    }

    private static int Count(StackBehaviour behaviour) => behaviour switch
    {
        StackBehaviour.Pop0 or StackBehaviour.Push0 or StackBehaviour.PopAll => 0,
        StackBehaviour.Varpop or StackBehaviour.Varpush => throw new InvalidOperationException("Unsupported variable stack operation"),
        _ => behaviour.ToString().Split('_').Length
    };
    private static bool IsStore(Instruction i) => i.OpCode.Code is Code.Stloc or Code.Stloc_S or Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3;
    private static VariableDefinition Local(Instruction i, MethodDefinition m) => i.Operand as VariableDefinition ??
        m.Body.Variables[i.OpCode.Code switch { Code.Stloc_0 => 0, Code.Stloc_1 => 1, Code.Stloc_2 => 2, Code.Stloc_3 => 3, _ => throw new InvalidOperationException() }];
    private static int Integer(Instruction i) => i.OpCode.Code switch
    {
        Code.Ldc_I4_0 => 0, Code.Ldc_I4_1 => 1, Code.Ldc_I4_2 => 2, Code.Ldc_I4_3 => 3,
        Code.Ldc_I4_4 => 4, Code.Ldc_I4_5 => 5, Code.Ldc_I4_6 => 6, Code.Ldc_I4_7 => 7, Code.Ldc_I4_8 => 8,
        Code.Ldc_I4_S => (sbyte)i.Operand, Code.Ldc_I4 => (int)i.Operand,
        _ => throw new InvalidOperationException("Unexpected switch offset")
    };
    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        yield return type;
        foreach (var child in type.NestedTypes.SelectMany(Flatten)) yield return child;
    }
}
