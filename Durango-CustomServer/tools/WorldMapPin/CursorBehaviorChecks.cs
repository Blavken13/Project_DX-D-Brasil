using Mono.Cecil;
using System.Reflection;
using System.Reflection.Emit;

// Execute the emitted helper IL with stand-ins for Unity objects. This checks
// the actual patch, including branches, argument order and null handling.
internal static class CursorBehaviorChecks
{
    public static int Count { get; private set; }
    public sealed record Region(int RoleValue, int BiomeValue, int LevelValue)
    {
        public int Role() => RoleValue;
        public int MajorBiome() => BiomeValue;
        public int get_Level() => LevelValue;
    }
    public sealed class Node { public bool Destroyed; }
    public sealed class Viewer
    {
        public bool _isValidCurrentCursor;
        public Node? Cursor;
        public int Lookups;
        public readonly Dictionary<(int, int, int), Node> Stable = new();
        public readonly Dictionary<(int, int, int), Node> Unstable = new();
        public Node? GetIslandTransform(int role, int biome, int level)
        {
            Lookups++;
            return Stable.GetValueOrDefault((role, biome, level));
        }
        public Node? GetUnstableRoutesTransform(int role, int biome, int level)
        {
            Lookups++;
            return Unstable.GetValueOrDefault((role, biome, level));
        }
        public void SetCurrentCursor(Node node)
        {
            Cursor = node;
            _isValidCurrentCursor = true;
        }
    }
    public static Region? Current;
    public static Region? get_Region() => Current;
    public static bool op_Inequality(Node? left, Node? right) =>
        (left is { Destroyed: false } ? left : null) != (right is { Destroyed: false } ? right : null);

    public static void Run(MethodDefinition helper)
    {
        Count = 0;
        var run = Compile(helper);
        foreach (var key in new[] { (9, 0, 10), (6, 1, 40), (4, 2, 20), (4, 3, 60), (7, 1, 60) })
        {
            var viewer = new Viewer();
            Current = new Region(key.Item1, key.Item2, key.Item3);
            var expected = new Node();
            var nodes = key.Item1 is 9 or 6 ? viewer.Stable : viewer.Unstable;
            nodes[key] = expected;
            nodes[(key.Item1 + 1, key.Item2, key.Item3)] = new Node();
            nodes[(key.Item1, key.Item2 + 1, key.Item3)] = new Node();
            nodes[(key.Item1, key.Item2, key.Item3 + 1)] = new Node();
            run(viewer);
            Check(viewer.Cursor == expected && viewer._isValidCurrentCursor, "region tuple " + key);
        }
        var existing = new Viewer { Cursor = new Node(), _isValidCurrentCursor = true };
        var original = existing.Cursor;
        run(existing);
        Check(existing.Cursor == original && existing.Lookups == 0, "preserve native tamed/personal cursor");

        Current = null;
        var empty = new Viewer();
        run(empty);
        Check(empty.Cursor == null && !empty._isValidCurrentCursor && empty.Lookups == 0, "no current region");

        Current = new Region(4, 2, 20);
        run(empty);
        Check(empty.Cursor == null && !empty._isValidCurrentCursor && empty.Lookups == 2, "no matching map node");

        var destroyed = new Viewer();
        destroyed.Stable[(4, 2, 20)] = new Node { Destroyed = true };
        var fallback = new Node();
        destroyed.Unstable[(4, 2, 20)] = fallback;
        run(destroyed);
        Check(destroyed.Cursor == fallback, "Unity destroyed object falls back to unstable area");

        var travel = new Viewer();
        var first = new Node();
        var second = new Node();
        travel.Unstable[(4, 2, 20)] = first;
        travel.Unstable[(4, 3, 60)] = second;
        run(travel);
        Check(travel.Cursor == first, "first map opening");
        Current = new Region(4, 3, 60);
        travel._isValidCurrentCursor = false; // WorldRoutesViewer.Set resets this on every opening.
        run(travel);
        Check(travel.Cursor == second && travel._isValidCurrentCursor, "reopen after travel");
    }

    static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("Cursor regression: " + name);
        Count++;
    }

    static Action<Viewer> Compile(MethodDefinition method)
    {
        Type MapType(TypeReference type) => type.FullName switch
        {
            "Durango.Logic.Explore.Region" => typeof(Region),
            "UnityEngine.Transform" => typeof(Node),
            _ => throw new InvalidOperationException("Unexpected local: " + type)
        };
        var dynamic = new DynamicMethod("PatchedCursor", typeof(void), new[] { typeof(Viewer) }, typeof(CursorBehaviorChecks).Module, true);
        var il = dynamic.GetILGenerator();
        var locals = method.Body.Variables.Select(v => il.DeclareLocal(MapType(v.VariableType))).ToArray();
        var labels = method.Body.Instructions.ToDictionary(i => i, _ => il.DefineLabel());
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
        foreach (var instruction in method.Body.Instructions)
        {
            il.MarkLabel(labels[instruction]);
            var opcode = opcodes[instruction.OpCode.Value];
            switch (instruction.Operand)
            {
                case null: il.Emit(opcode); break;
                case Mono.Cecil.Cil.Instruction target: il.Emit(opcode, labels[target]); break;
                case Mono.Cecil.Cil.VariableDefinition local: il.Emit(opcode, locals[local.Index]); break;
                case FieldReference field:
                    il.Emit(opcode, typeof(Viewer).GetField(field.Name)!); break;
                case MethodReference call:
                    var type = call.DeclaringType.FullName switch
                    {
                        "GameManager" or "UnityEngine.Object" => typeof(CursorBehaviorChecks),
                        "Durango.Logic.Explore.Region" => typeof(Region),
                        "Durango.UI.WorldRoutesViewer" => typeof(Viewer),
                        _ => throw new InvalidOperationException("Unexpected call: " + call)
                    };
                    il.Emit(opcode, type.GetMethod(call.Name)!); break;
                default: throw new InvalidOperationException("Unexpected IL: " + instruction);
            }
        }
        return dynamic.CreateDelegate<Action<Viewer>>();
    }
}
