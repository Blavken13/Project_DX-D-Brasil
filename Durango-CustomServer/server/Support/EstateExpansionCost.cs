using System;
using System.Collections.Generic;
using Durango.Utils;
using Newtonsoft.Json.Linq;
using Shared.Estate;

namespace Durango.Online;

public static class EstateExpansionCost
{
    public static long For(OwnerType type, int size, int islandLevel)
    {
        if (type != OwnerType.Player || islandLevel <= 10) return 0;
        string formula = (string)Json.ReadFromFile<JObject>("costs")?["estate"]?["expanding_cost"]?[(int)type + ""];
        if (string.IsNullOrEmpty(formula)) return 0;
        if (!StatFormula.TryEval(formula, new Dictionary<string, double> { ["size"] = size }, out double result) ||
            result < 0 || result > long.MaxValue)
            throw new InvalidOperationException("Fórmula inválida de expansão do acampamento.");
        return (long)result;
    }
}
