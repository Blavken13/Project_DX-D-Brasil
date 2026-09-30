using System.Text.RegularExpressions;
using Durango.Online;
using Durango.Utils;
using Newtonsoft.Json.Linq;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class LocalizationCheck
{
    private static int _passed;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _passed++;
    }

    internal static int Run(string dataDir)
    {
        try
        {
            MoCatalog.Load(dataDir);
            Check(MoCatalog.Count > 33000, "O catálogo brasileiro completo foi carregado.");
            Check(MoCatalog.Translate("취소") == "Cancelar", "Botão Cancelar em português.");
            Check(MoCatalog.Translate("나뭇가지") == "Galho", "Nome do recurso em português.");
            Check(MoCatalog.Translate("#config_ui_mode_Mobile") == "Celular", "Rótulo traduzido sem alterar o valor Mobile.");
            Check(MoCatalog.Translate("악기\u0004기타") == "Violão", "Contexto de instrumento preservado.");
            Check(MoCatalog.Translate("#config_leave") == "Excluir conta", "Rótulo adicional da comunidade traduzido.");
            Check(new Gettext("취소", new() { ["th_TH"] = "ไทย", ["en_US"] = "Cancel" }).ToString() == "Cancelar",
                "O catálogo português tem prioridade sobre traduções antigas.");
            Check(new Gettext("test", new() { ["pt_BR"] = "Português", ["th_TH"] = "ไทย" }).ToString() == "Português",
                "Tradução explícita pt_BR tem prioridade.");
            Check(new Gettext("test", new() { ["pt-BR"] = "Português" }).ToString() == "Português", "Alias pt-BR aceito.");
            Check(new Gettext("test", new() { ["pt"] = "Português" }).ToString() == "Português", "Alias pt aceito.");
            Check(MoCatalog.Translate("unknown_protocol_id") == "unknown_protocol_id", "Identificador desconhecido preservado.");

            DataStore.Load(dataDir);
            foreach (var pair in PrototypeYaml.Instance)
            foreach (var prototype in pair.Value)
            {
                if (prototype.Name != null) ValidateText(prototype.Name.ToString(), $"Nome de {pair.Key}");
                if (prototype.Description != null) ValidateText(prototype.Description.ToString(), $"Descrição de {pair.Key}");
                if (prototype.Help != null) ValidateText(prototype.Help.ToString(), $"Ajuda de {pair.Key}");
            }
            foreach (var file in Directory.EnumerateFiles(Path.Combine(dataDir, "assets"), "*.json", SearchOption.AllDirectories))
            {
                // Derived metadata documents confidence/source information; those
                // keys are not gettext messages and must retain their identities.
                if (file.Replace('\\', '/').Contains("/derived/")) continue;
                var root = JToken.Parse(File.ReadAllText(file));
                foreach (var property in Properties(root))
                    if (Regex.IsMatch(property.Name, "[\\uac00-\\ud7a3\\u0e00-\\u0e7f]"))
                        ValidateText(MoCatalog.Translate(property.Name), $"Mensagem em {Path.GetFileName(file)}");
            }
            Console.WriteLine($"[localization-check] PASS {_passed} verificações. Nenhuma porta aberta; nenhum save alterado.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine("[localization-check] FAIL " + exception.Message);
            return 1;
        }
    }

    private static void ValidateText(string text, string description)
    {
        // Korean grammatical selectors inside SmartFormat placeholders are
        // formatting instructions, not displayed words; preserve those selectors.
        string visible = Regex.Replace(text ?? "", "\\{[^{}]*\\}|<[^>]*>|\\[[^\\]]*\\]", "");
        Check(!Regex.IsMatch(visible, "[\\uac00-\\ud7a3\\u0e00-\\u0e7f]"), description + ": " + text);
    }

    private static IEnumerable<JProperty> Properties(JToken token)
    {
        if (token is JProperty property) yield return property;
        if (token is JContainer container)
            foreach (var child in container.Children())
                foreach (var descendant in Properties(child)) yield return descendant;
    }
}
