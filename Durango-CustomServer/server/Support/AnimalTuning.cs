using System;
using System.IO;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// Ajustes de densidade da fauna lidos de data/config.json -> Animals.
/// </summary>
public static class AnimalTuning
{
    private static JObject _animals;

    /// <summary>
    /// Teto de animais selvagens mantidos por regiao.
    /// </summary>
    public static int MaxAnimalsPerRegion =>
        Math.Clamp(
            (int)Math.Round(GetDouble("MaxAnimalsPerRegion", 120.0)),
            1,
            500);

    public static int TargetAnimalsPerRegion => Math.Min(MaxAnimalsPerRegion,
        Math.Clamp((int)Math.Round(GetDouble("TargetAnimalsPerRegion", 40)), 1, 500));

    public static void Reload() => _animals = null;

    private static double GetDouble(string key, double fallback)
    {
        Load();

        JToken token = _animals?[key];

        return token != null &&
               double.TryParse(token.ToString(), out double value) &&
               value > 0
            ? value
            : fallback;
    }

    private static void Load()
    {
        if (_animals != null)
        {
            return;
        }

        _animals = new JObject();

        try
        {
            string path = Path.Combine(
                Json.DataDir ?? ".",
                "config.json");

            if (!File.Exists(path))
            {
                return;
            }

            var root = JObject.Parse(
                File.ReadAllText(path));

            if (root["Animals"] is JObject animals)
            {
                _animals = animals;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"[config] Falha ao ler config.json -> Animals " +
                $"({e.Message}); usando valores padrao.");
        }
    }
}
