using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length > 0 && args[0] == "verify-gameplay")
{
    GameplayPatcher.Verify(args[1]);
    return;
}
if (args.Length > 0 && args[0] == "patch-gameplay")
{
    GameplayPatcher.Run(args[1], args[2]);
    return;
}
if (args.Length > 0 && args[0] == "verify-client")
{
    var translations = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(args[3]))!;
    using var original = AssemblyDefinition.ReadAssembly(args[1]);
    var locale = original.MainModule.Types.SingleOrDefault(t => t.FullName == "LocalizeSystem");
    ClientPatcher.Verify(args[1], args[2], translations, locale is not null && !locale.Methods.Any(m => m.Name == "EnsureBrazilianLocale"));
    Console.WriteLine("PASS: final assembly matches the original except for authorized translations and locale initialization.");
    return;
}
if (args.Length > 0 && args[0] == "patch-client")
{
    ClientPatcher.Run(args[1], args[2], args[3]);
    return;
}
if (args.Length > 0 && args[0] is "apply-source" or "apply-client-source")
{
    bool client = args[0] == "apply-client-source";
    var translations = client ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[2]))!;
    var clientTranslations = client ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(args[2]))! : null;
    string? Translate(SyntaxToken token)
    {
        if (!client) return translations.GetValueOrDefault(token.ValueText);
        var types = token.Parent!.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Reverse().ToArray();
        if (types.Length == 0) return null;
        var ns = token.Parent.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().LastOrDefault()?.Name.ToString();
        var typeName = (ns is null ? "" : ns + ".") + string.Join("/", types.Select(t => t.Identifier.ValueText));
        var method = token.Parent.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        method ??= "get_" + token.Parent.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        foreach (var (scope, values) in clientTranslations!)
        {
            var parts = scope.Split("::", 2);
            if (!(typeName == parts[0] || typeName.StartsWith(parts[0] + "/", StringComparison.Ordinal))) continue;
            if (parts.Length == 2 && parts[1] != method) continue;
            if (values.TryGetValue(token.ValueText, out var value)) return value;
        }
        return null;
    }
    int changed = 0;
    foreach (var folder in client ? new[] { "" } : new[] { "Core", "Support" })
    foreach (var file in Directory.EnumerateFiles(Path.Combine(args[1], folder), "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Contains("/bin/") && !p.Contains("/obj/") && !p.Contains("\\bin\\") && !p.Contains("\\obj\\")))
    {
        var original = File.ReadAllText(file);
        var root = CSharpSyntaxTree.ParseText(original).GetRoot();
        var tokens = root.DescendantTokens().Where(t =>
            (t.IsKind(SyntaxKind.StringLiteralToken) || t.IsKind(SyntaxKind.InterpolatedStringTextToken)) &&
            Translate(t) is { } translated && translated != t.ValueText && !IsLog(t)).ToArray();
        if (tokens.Length == 0) continue;
        var edited = root.ReplaceTokens(tokens, (old, _) =>
        {
            var value = Translate(old)!;
            if (old.IsKind(SyntaxKind.StringLiteralToken))
                return SyntaxFactory.Literal(Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true), value).WithTriviaFrom(old);
            var escaped = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true)[1..^1]
                .Replace("{", "{{").Replace("}", "}}");
            return SyntaxFactory.Token(old.LeadingTrivia, SyntaxKind.InterpolatedStringTextToken, escaped, value, old.TrailingTrivia);
        }).ToFullString();
        var parsed = CSharpSyntaxTree.ParseText(edited);
        if (parsed.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException($"Invalid syntax after translation: {file}");
        var beforeTokens = root.DescendantTokens().ToArray();
        var afterTokens = parsed.GetRoot().DescendantTokens().ToArray();
        if (beforeTokens.Length != afterTokens.Length) throw new InvalidOperationException($"Token count changed: {file}");
        for (int i = 0; i < beforeTokens.Length; i++)
        {
            var before = beforeTokens[i]; var after = afterTokens[i];
            if (before.Kind() != after.Kind() || before.ValueText != after.ValueText &&
                after.ValueText != Translate(before))
                throw new InvalidOperationException($"Non-text token changed: {file}:{before.SpanStart}");
        }
        File.WriteAllText(file, edited, new UTF8Encoding(false));
        changed += tokens.Length;
        Console.WriteLine($"Translated {tokens.Length} tokens: {Path.GetFileName(file)}");
    }
    Console.WriteLine($"PASS: {changed} translated tokens; all identifiers, expressions and comments preserved.");
    return;
}
if (args.Length > 0 && args[0] == "scan-source")
{
    var sourceRows = new List<object>();
    foreach (var file in Directory.EnumerateFiles(args[1], "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Contains("/bin/") && !p.Contains("/obj/") && !p.Contains("\\bin\\") && !p.Contains("\\obj\\")))
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
        foreach (var token in tree.GetRoot().DescendantTokens().Where(t =>
            t.IsKind(SyntaxKind.StringLiteralToken) || t.IsKind(SyntaxKind.InterpolatedStringTextToken)))
        {
            if (!Regex.IsMatch(token.ValueText, "[\\u0e00-\\u0e7f\\uac00-\\ud7a3]")) continue;
            bool log = IsLog(token);
            sourceRows.Add(new { File = file, Line = tree.GetLineSpan(token.Span).StartLinePosition.Line + 1,
                Start = token.SpanStart, Kind = token.Kind().ToString(), Text = token.ValueText, IsLog = log });
        }
    }
    File.WriteAllText(args[2], JsonSerializer.Serialize(sourceRows, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    Console.WriteLine($"Inspected {sourceRows.Count} foreign source text tokens.");
    return;
}
if (args.Length < 2) throw new ArgumentException("inspect <assembly> [output.json]");
using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
var rows = new List<object>();
foreach (var type in assembly.MainModule.Types.SelectMany(Flatten))
foreach (var method in type.Methods.Where(m => m.HasBody))
{
    var strings = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr)
        .Select(i => new { i.Offset, Text = (string)i.Operand }).ToArray();
    if (strings.Length > 0) rows.Add(new { Type = type.FullName, Method = method.Name, Strings = strings });
    if (type.FullName == "LocalizeSystem" && method.Name is "NormalizeLocale" or "SetLocale" ||
        type.FullName == "Durango.System.Config.ConfigInstance" && method.Name is ".cctor" or "LoadValue" ||
        type.FullName.Contains("LanguageOverride"))
    {
        Console.WriteLine($"{type.FullName}::{method.Name}");
        foreach (var instruction in method.Body.Instructions) Console.WriteLine(instruction);
    }
}
if (args.Length > 2) File.WriteAllText(args[2], JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
Console.WriteLine($"Inspected {rows.Count} methods with string literals.");
static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
{
    yield return type;
    foreach (var nested in type.NestedTypes.SelectMany(Flatten)) yield return nested;
}
static bool IsLog(SyntaxToken token) => token.Parent!.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
    .Any(i => i.Expression.ToString() is "Console.WriteLine" or "Console.Write" or "Debug.Log" or "Debug.LogWarning" or "Debug.LogError");
