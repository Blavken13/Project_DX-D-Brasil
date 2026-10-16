# Escala dos ícones na visualização de recursos do client PC

O `InteractionHelperLabel.UpdateContents` formata os ícones dos recursos, animais e estruturas como `[icon=nome:1.5]`. O fator `1.5` representa uma altura de uma vez e meia o tamanho da fonte.

O parser `Durango.UI.Control.UISpriteLabel.TryTextParse` usava `float.TryParse(string, out float)`, que depende da cultura atual e permite separadores de milhares. Em `pt-BR`, a string `1.5` era interpretada como `15`, causando ícones dez vezes maiores e espaçamento incorreto enquanto o texto continuava com tamanho normal.

O patch usa `Single.TryParse(string, NumberStyles.Float, CultureInfo.InvariantCulture, out float)`. Os fatores do formato interno agora usam ponto decimal independentemente da região do Windows, sem aceitar separadores de milhares. A montagem completa aplica a correção através do `CommunityPatcher`. Para reaplicar somente esta correção ao client instalado:

```powershell
python pc-client/fix_resource_icons.py
```

O script preserva uma cópia da DLL anterior e instala somente a DLL corrigida em `Lost-Horizon-PC/Lost Horizon Game_Data/Managed`. O executável Unity e os assets permanecem intactos.

A validação compara as instruções dos demais 37.023 métodos e verifica que o patch pode ser reaplicado sem duplicar instruções. A reprodução numérica confirmou `15 → 1.5` em `pt-BR` e `de-DE`, e `1.5 → 1.5` em `en-US` e `th-TH`. Evidência: `pc-client/work/resource-icon-audit/resource-icons-validation.json`. Não foi realizada validação visual autenticada dentro do mundo; é preciso reabrir o jogo para carregar a DLL corrigida.
