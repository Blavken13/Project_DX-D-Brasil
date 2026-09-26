#!/usr/bin/env python3
"""
Durango Brasil — Alpha gameplay hotfix (2026-09-26)

Aplica, de forma fail-fast e idempotente, as correcoes acordadas:
  1) evento de XP: x20 -> x3;
  2) descanso: bonus x66 apenas em fatigue -> x3 nos efeitos originais
     de descanso (fatigue, life e health); stamina permanece com sua regen base;
  3) restaura o generator "rock" como o prototype real "stone_big";
  4) recursos naturais passam a usar o level do template da regiao; carcacas
     usam CombatLevel; nivel e clampado pelo min/max do prototype sem mutar cache;
  5) constants.json/skill passa a aceitar valores numericos e formulas-string,
     eliminando o erro de desserializacao de research_time_reduce.

Uso, a partir de qualquer pasta:
    python apply_durango_alpha_hotfix.py --repo /caminho/Project_DX-D-Brasil

O script aborta se um trecho esperado nao for encontrado de forma inequivoca.
Ele nunca tenta adivinhar um local de edicao em uma arvore divergente.
"""
from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path

SERVER_REL = Path("Durango-CustomServer/server")
CORE_REL = SERVER_REL / "Core"


def die(msg: str) -> None:
    raise SystemExit(f"ERRO: {msg}")


def read_text(path: Path) -> tuple[str, str]:
    raw = path.read_bytes()
    newline = "\r\n" if b"\r\n" in raw else "\n"
    text = raw.decode("utf-8-sig").replace("\r\n", "\n")
    return text, newline


def write_text(path: Path, text: str, newline: str) -> None:
    # UTF-8 sem BOM, preservando o estilo de newline do arquivo original.
    path.write_bytes(text.replace("\n", newline).encode("utf-8"))


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count == 0:
        if new in text:
            return text
        die(f"{label}: trecho esperado nao encontrado")
    if count != 1:
        die(f"{label}: trecho apareceu {count} vezes; abortando")
    return text.replace(old, new, 1)


def regex_once(
    text: str,
    pattern: str,
    repl: str,
    label: str,
    *,
    flags: int = re.MULTILINE | re.DOTALL,
    already_marker: str | None = None,
) -> str:
    rx = re.compile(pattern, flags)
    matches = list(rx.finditer(text))
    if not matches:
        if already_marker and already_marker in text:
            return text
        die(f"{label}: padrao esperado nao encontrado")
    if len(matches) != 1:
        die(f"{label}: padrao apareceu {len(matches)} vezes; abortando")
    return rx.sub(repl, text, count=1)


def patch_skills(path: Path) -> None:
    text, nl = read_text(path)

    # 1) XP do Alpha: preservar todo o pipeline de XP, alterando so o multiplicador central.
    if "AlphaTestPlayerExpMultiplier = 3" not in text:
        text, n = re.subn(
            r"(AlphaTestPlayerExpMultiplier\s*=\s*)20(\s*;)",
            r"\g<1>3\g<2>",
            text,
            count=1,
        )
        if n != 1:
            die("Player.Skills.cs: multiplicador Alpha x20 nao localizado exatamente uma vez")

    # Mantem tambem comentarios/etiquetas coerentes com o multiplicador configurado.
    text = text.replace(
        "1 = balance normal. 20 = evento acelerado para desbloquear rapidamente os testes de World/Travel.",
        "1 = balance normal. 3 = evento acelerado para agilizar os testes de Alpha.",
    )
    text = text.replace("ALPHA XP x20", "ALPHA XP x3")

    # 5) constants.json -> skill mistura numeros e formulas (research_time_reduce).
    # Newtonsoft nao consegue desserializar a formula string para Dictionary<string,float>.
    if "using Newtonsoft.Json.Linq;" not in text:
        if "using Newtonsoft.Json;" not in text:
            die("Player.Skills.cs: using Newtonsoft.Json nao localizado")
        text = text.replace("using Newtonsoft.Json;", "using Newtonsoft.Json;\nusing Newtonsoft.Json.Linq;", 1)

    if "Dictionary<string, JToken> Skill" not in text:
        text, n = re.subn(
            r"public\s+Dictionary<string,\s*(?:float|double)>\s+Skill\s*;",
            "public Dictionary<string, JToken> Skill;",
            text,
            count=1,
        )
        if n != 1:
            die("Player.Skills.cs: SkillConstantsJson.Skill numerico nao localizado")

    helper = '''    /// <summary>\n    /// constants.json -> skill contem tanto numeros quanto formulas em texto.\n    /// Os consumidores numericos leem apenas tokens numericos; formulas continuam\n    /// disponiveis no JSON para os sistemas que as avaliam separadamente.\n    /// </summary>\n    private static bool TryReadSkillFloat(string key, out float value)\n    {\n        value = 0f;\n        if (Constants?.Skill == null ||\n            !Constants.Skill.TryGetValue(key, out JToken token) ||\n            token == null)\n        {\n            return false;\n        }\n        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)\n        {\n            return false;\n        }\n        value = token.Value<float>();\n        return true;\n    }\n\n'''
    if "private static bool TryReadSkillFloat(string key, out float value)" not in text:
        m = re.search(r"(?m)^(\s*)public static void EnsureLoaded\(\)", text)
        if not m:
            die("Player.Skills.cs: SkillDataStore.EnsureLoaded nao localizado")
        # A classe usa 4 espacos no projeto atual; helper ja vem com essa indentacao.
        text = text[:m.start()] + helper + text[m.start():]

    # Ler somente os dois campos numericos que o servidor realmente consome deste bloco.
    replacements = [
        (
            r'Constants\.Skill\.TryGetValue\("exp_increase_limit",\s*out float\s+([A-Za-z_]\w*)\)',
            r'TryReadSkillFloat("exp_increase_limit", out float \1)',
            'exp_increase_limit',
        ),
        (
            r'Constants\.Skill\.TryGetValue\("research_reduce_time_limit",\s*out float\s+([A-Za-z_]\w*)\)',
            r'TryReadSkillFloat("research_reduce_time_limit", out float \1)',
            'research_reduce_time_limit',
        ),
    ]
    for pattern, repl, key in replacements:
        if f'TryReadSkillFloat("{key}"' in text:
            continue
        text, n = re.subn(pattern, repl, text, count=1)
        if n != 1:
            die(f"Player.Skills.cs: leitura numerica de {key} nao localizada")

    write_text(path, text, nl)


def patch_survival(path: Path) -> None:
    text, nl = read_text(path)

    # Renomeia para refletir que agora o multiplicador vale para todo o efeito de rest.
    if "AlphaTestRestMultiplier = 3f" not in text:
        text, n = re.subn(
            r"public\s+const\s+float\s+AlphaTestRestFatigueMultiplier\s*=\s*66f\s*;",
            "public const float AlphaTestRestMultiplier = 3f;",
            text,
            count=1,
        )
        if n != 1:
            die("SurvivalState.cs: AlphaTestRestFatigueMultiplier = 66f nao localizado")

    # A implementacao atual monta um dictionary de velocidades de rest e, no evento Alpha,
    # multiplicava so KeyFatigue. Mantemos as formulas originais e multiplicamos os tres
    # efeitos definidos pelo status 'rest': fatigue, life e health.
    marker = "ALPHA_REST_X3_ALL_ORIGINAL_EFFECTS"
    if marker not in text:
        rx = re.compile(
            r'''(?P<indent>^[ \t]*)if\s*\(acceleratedFatigue\s*&&\s*(?P<v>[A-Za-z_]\w*)\.TryGetValue\((?:SurvivalState\.)?KeyFatigue,\s*out float fatigue\)\s*&&\s*\n'''
            r'''(?P=indent)[ \t]+fatigue\s*<\s*0f\)\s*\n'''
            r'''(?P=indent)\{\s*\n'''
            r'''(?P=indent)[ \t]+(?P=v)\[(?:SurvivalState\.)?KeyFatigue\]\s*=\s*\n?'''
            r'''(?P=indent)[ \t]*fatigue\s*\*\s*(?:SurvivalTuning\.)?AlphaTestRest(?:Fatigue)?Multiplier\s*;\s*\n'''
            r'''(?P=indent)\}''',
            re.MULTILINE,
        )
        m = rx.search(text)
        if not m:
            die("SurvivalState.cs: bloco atual de boost apenas de fatigue nao localizado")
        indent = m.group("indent")
        v = m.group("v")
        body = (
            f"{indent}if (acceleratedFatigue)\n"
            f"{indent}{{\n"
            f"{indent}    // {marker}: o evento acelera os efeitos originais do descanso em conjunto.\n"
            f"{indent}    // Stamina preserva sua regeneracao base propria; o status rest define fatigue/life/health.\n"
            f"{indent}    if ({v}.TryGetValue(SurvivalState.KeyFatigue, out float fatigue) && fatigue < 0f)\n"
            f"{indent}        {v}[SurvivalState.KeyFatigue] = fatigue * AlphaTestRestMultiplier;\n"
            f"{indent}    if ({v}.TryGetValue(SurvivalState.KeyLife, out float life) && life > 0f)\n"
            f"{indent}        {v}[SurvivalState.KeyLife] = life * AlphaTestRestMultiplier;\n"
            f"{indent}    if ({v}.TryGetValue(SurvivalState.KeyHealth, out float health) && health > 0f)\n"
            f"{indent}        {v}[SurvivalState.KeyHealth] = health * AlphaTestRestMultiplier;\n"
            f"{indent}}}"
        )
        text = text[:m.start()] + body + text[m.end():]

    # Qualquer referencia residual ao nome antigo deve apontar para o novo multiplicador.
    text = text.replace("AlphaTestRestFatigueMultiplier", "AlphaTestRestMultiplier")
    text = text.replace(
        "TEMP ALPHA TEST EVENT — acelera somente a recuperacao de fatigue durante descanso",
        "TEMP ALPHA TEST EVENT — acelera os efeitos normais de descanso em 3x",
    )
    text = text.replace(
        "rest lv1 = -0.1515/s; x66 = -9.999/s, aproximadamente 100 -> 0 em 10 segundos.",
        "O bonus x3 preserva as formulas originais de fatigue, life e health do abrigo.",
    )
    write_text(path, text, nl)


def patch_player(path: Path) -> None:
    text, nl = read_text(path)
    old = "SurvivalTuning.AlphaTestRestFatigueMultiplier"
    new = "SurvivalTuning.AlphaTestRestMultiplier"
    if old in text:
        text = text.replace(old, new)
    # Log deixa de afirmar apenas "boost" e passa a identificar rest x3.
    text = text.replace(
        "ALPHA boost x{SurvivalTuning.AlphaTestRestMultiplier:0.##}",
        "ALPHA rest x{SurvivalTuning.AlphaTestRestMultiplier:0.##}",
    )
    if new not in text:
        die("Player.cs: referencia do multiplicador de rest nao localizada")
    write_text(path, text, nl)


def patch_gathering(path: Path) -> None:
    text, nl = read_text(path)

    # Helper no Player: fonte autoritativa igual a travel/fauna. Nao grava level em cache global.
    if "private int CurrentGatheringLevel()" not in text:
        anchor = re.search(
            r"(?m)^\s*(?:internal|private)\s+Collectible\s+BuildCollectibleFor\(",
            text,
        )
        if not anchor:
            die("Player.Gathering.cs: BuildCollectibleFor nao localizado")
        helper = '''    /// <summary>\n    /// Nivel efetivo dos recursos naturais na regiao atual. Usa o mesmo RegionCatalog\n    /// autoritativo de travel/fauna. O valor e calculado por requisicao para nao vazar\n    /// level entre ilhas atraves dos caches estaticos de generator.\n    /// </summary>\n    private int CurrentGatheringLevel()\n    {\n        if (RegionCatalog.TryGet(_world.TerrainId, out Messages.Region region))\n        {\n            RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(region.TemplateId);\n            if (template != null && template.Level > 0) return template.Level;\n        }\n        return 1;\n    }\n\n'''
        # Insere antes do XML doc do BuildCollectibleFor, preservando a documentacao do metodo.
        doc_start = text.rfind("    /// <summary>", 0, anchor.start())
        insert_at = doc_start if doc_start >= 0 and anchor.start() - doc_start < 1600 else anchor.start()
        text = text[:insert_at] + helper + text[insert_at:]

    # As duas respostas Collectible devem anunciar o mesmo level que sera efetivamente entregue.
    target = "animalLevel, UnlockedCollectibleCategories())"
    replacement = (
        "animalLevel, UnlockedCollectibleCategories(), "
        "animalLevel > 0 ? animalLevel : CurrentGatheringLevel())"
    )
    if replacement not in text:
        count = text.count(target)
        if count != 2:
            die(f"Player.Gathering.cs: esperava 2 chamadas Build com animalLevel; encontrei {count}")
        text = text.replace(target, replacement)

    # Na coleta real, projeta o spec base para o level da ilha/carcaca antes de validar ferramenta,
    # custo, duracao, quantidade e criar o item.
    live_line = (
        "spec = CollectibleTable.AtLevel(spec, carcass != null ? "
        "carcass.CombatLevel : CurrentGatheringLevel());"
    )
    if live_line not in text:
        rx = re.compile(
            r'''(?P<block>CollectibleTable\.GeneratorSpec\s+spec\s*=\s*CollectibleTable\.FindGenerator\(entityType,\s*msg\.GeneratorId\);\s*\n'''
            r'''\s*if\s*\(spec\s*==\s*null\)\s*\n\s*\{.*?\n\s*\})''',
            re.MULTILINE | re.DOTALL,
        )
        m = rx.search(text)
        if not m:
            die("Player.Gathering.cs: FindGenerator + null guard nao localizados")
        # Mantem a indentacao da declaracao de spec.
        line_start = text.rfind("\n", 0, m.start()) + 1
        indent = re.match(r"[ \t]*", text[line_start:m.start()]).group(0)
        text = text[:m.end()] + "\n\n" + indent + live_line + text[m.end():]

    # O total do node precisa usar os mesmos specs nivelados; senao um node de level alto pode
    # ser marcado RanOut antes do numero de coletas anunciado no menu.
    if "CollectibleTable.GeneratorSpec live = CollectibleTable.AtLevel(" not in text:
        rx = re.compile(
            r'''foreach \(CollectibleTable\.GeneratorSpec s in CollectibleTable\.AllSpecs\(entityType\)\)\s*\n'''
            r'''(?P<i>\s*)\{\s*\n'''
            r'''(?P=i)\s*total \+= carcass != null\s*\n'''
            r'''(?P=i)\s*\? CollectibleTable\.AmountForCarcass\(s, carcass\.CombatLevel\)\s*\n'''
            r'''(?P=i)\s*: s\.Amount;\s*\n'''
            r'''(?P=i)\}''',
            re.MULTILINE,
        )
        m = rx.search(text)
        if not m:
            die("Player.Gathering.cs: loop de total/RanOut nao localizado")
        i = m.group("i")
        repl = (
            "foreach (CollectibleTable.GeneratorSpec s in CollectibleTable.AllSpecs(entityType))\n"
            f"{i}{{\n"
            f"{i}    CollectibleTable.GeneratorSpec live = CollectibleTable.AtLevel(\n"
            f"{i}        s, carcass != null ? carcass.CombatLevel : CurrentGatheringLevel());\n"
            f"{i}    total += carcass != null\n"
            f"{i}        ? CollectibleTable.AmountForCarcass(live, carcass.CombatLevel)\n"
            f"{i}        : live.Amount;\n"
            f"{i}}}"
        )
        text = text[:m.start()] + repl + text[m.end():]

    # Build recebe explicitamente o level de recurso natural; animalLevel continua separado.
    if "int resourceLevel = 0" not in text:
        text, n = re.subn(
            r'''public static Collectible Build\(string entityId,\s*ushort entityType,\s*\n'''
            r'''\s*IReadOnlyList<string> harvested = null, int animalLevel = 0,\s*\n'''
            r'''\s*Dictionary<string, int> unlockedCategories = null\)''',
            "public static Collectible Build(string entityId, ushort entityType,\n"
            "                                    IReadOnlyList<string> harvested = null, int animalLevel = 0,\n"
            "                                    Dictionary<string, int> unlockedCategories = null, int resourceLevel = 0)",
            text,
            count=1,
        )
        if n != 1:
            die("Player.Gathering.cs: assinatura de CollectibleTable.Build nao localizada")

    # Antes dos ajustes de carcaca/unlock/harvested, cria uma copia request-local do array
    # usando specs nivelados. `template` e struct, mas seu array e referencia: jamais mutamos o cache.
    level_marker = "GATHER_LEVEL_PROJECTED_PER_REQUEST"
    if level_marker not in text:
        anchor = "        template.EntityId = entityId;\n"
        if anchor not in text:
            die("Player.Gathering.cs: template.EntityId = entityId nao localizado")
        block = '''\n        // GATHER_LEVEL_PROJECTED_PER_REQUEST: nunca mutar SpecsFor/_cache com level de uma ilha.\n        int requestedLevel = animalLevel > 0 ? animalLevel : resourceLevel;\n        if (requestedLevel > 0 && template.Generators != null)\n        {\n            List<GeneratorSpec> baseSpecs = SpecsFor(entityType);\n            var leveled = new Generator[template.Generators.Length];\n            for (int i = 0; i < template.Generators.Length; i++)\n            {\n                GeneratorSpec live = i < baseSpecs.Count ? AtLevel(baseSpecs[i], requestedLevel) : null;\n                leveled[i] = live != null ? ToMessage(live) : template.Generators[i];\n            }\n            template.Generators = leveled;\n        }\n'''
        text = text.replace(anchor, anchor + block, 1)

    # Helpers imutaveis de nivel dentro de CollectibleTable.
    if "public static GeneratorSpec AtLevel(GeneratorSpec spec, int requestedLevel)" not in text:
        anchor = re.search(r"(?m)^\s*internal static int RequiredToolLevel\(GeneratorSpec spec\)", text)
        if not anchor:
            anchor = re.search(r"(?m)^\s*public static int RequiredToolLevel\(GeneratorSpec spec\)", text)
        if not anchor:
            die("Player.Gathering.cs: RequiredToolLevel nao localizado")
        helpers = '''    public static int ClampLevel(GeneratorSpec spec, int requestedLevel)\n    {\n        if (spec == null) return Math.Max(1, requestedLevel);\n        Prototype proto = PrototypeYaml.GetItemPrototype(spec.PrototypeId);\n        if (proto == null) return Math.Max(1, requestedLevel);\n        int min = Math.Max(1, proto.MinLevel);\n        int max = proto.MaxLevel > 0 ? Math.Max(min, proto.MaxLevel) : Math.Max(min, requestedLevel);\n        return Math.Clamp(Math.Max(1, requestedLevel), min, max);\n    }\n\n    /// <summary>\n    /// Projeta um GeneratorSpec para o level da regiao/carcaca sem alterar o spec cacheado.\n    /// Mantem amount/effort/duration/tool requirements coerentes com o level efetivo.\n    /// </summary>\n    public static GeneratorSpec AtLevel(GeneratorSpec spec, int requestedLevel)\n    {\n        if (spec == null) return null;\n        int level = ClampLevel(spec, requestedLevel);\n        if (level == spec.Level) return spec;\n\n        Prototype proto = PrototypeYaml.GetItemPrototype(spec.PrototypeId);\n        float effort = Effort(level);\n        return new GeneratorSpec\n        {\n            Id = spec.Id,\n            CollectibleId = spec.CollectibleId,\n            PrototypeId = spec.PrototypeId,\n            Name = spec.Name,\n            Icon = spec.Icon,\n            Level = level,\n            Amount = GatheringTuning.AmountFor(spec.Order, level),\n            Order = spec.Order,\n            Effort = effort,\n            Duration = Duration(effort),\n            ToolRequirements = proto != null ? ToolsFor(proto, level) : spec.ToolRequirements\n        };\n    }\n\n'''
        # Insere antes do XML doc de RequiredToolLevel para nao roubar a documentacao dele.
        doc_start = text.rfind("    /// <summary>", 0, anchor.start())
        insert_at = doc_start if doc_start >= 0 and anchor.start() - doc_start < 1200 else anchor.start()
        text = text[:insert_at] + helpers + text[insert_at:]

    # Restaura o terceiro generator do spot de pedra: recipes usa id 'rock'; o prototype real
    # com tag chunk_big+stone e icone correspondente e stone_big.
    if 'string.Equals(generatorId, "rock", StringComparison.Ordinal)' not in text:
        date_block = re.compile(
            r'''if\s*\(string\.Equals\(generatorId,\s*"date",\s*StringComparison\.Ordinal\)\s*&&\s*\n'''
            r'''\s*PrototypeYaml\.GetItemPrototype\("fruit_tropical"\)\s*!=\s*null\)\s*\n'''
            r'''\s*\{\s*\n\s*return\s+"fruit_tropical";\s*\n\s*\}''',
            re.MULTILINE,
        )
        m = date_block.search(text)
        if not m:
            die("Player.Gathering.cs: alias date -> fruit_tropical nao localizado")
        alias = '''\n\n        // recipes.json usa generator id "rock" para o slot de pedra grande (chunk_big).\n        // O id de protocolo continua "rock" via _generatorIdByCollectiblePrototype;\n        // o item real entregue ao inventario e o prototype stone_big.\n        if (string.Equals(generatorId, "rock", StringComparison.Ordinal) &&\n            PrototypeYaml.GetItemPrototype("stone_big") != null)\n        {\n            return "stone_big";\n        }'''
        text = text[:m.end()] + alias + text[m.end():]

    write_text(path, text, nl)


def validate_text(root: Path) -> None:
    core = root / CORE_REL
    checks = {
        core / "Player.Skills.cs": [
            "AlphaTestPlayerExpMultiplier = 3",
            "Dictionary<string, JToken> Skill",
            "TryReadSkillFloat",
        ],
        core / "SurvivalState.cs": [
            "AlphaTestRestMultiplier = 3f",
            "ALPHA_REST_X3_ALL_ORIGINAL_EFFECTS",
            "SurvivalState.KeyLife] = life * AlphaTestRestMultiplier",
            "SurvivalState.KeyHealth] = health * AlphaTestRestMultiplier",
        ],
        core / "Player.cs": ["SurvivalTuning.AlphaTestRestMultiplier"],
        core / "Player.Gathering.cs": [
            'GetItemPrototype("stone_big")',
            "CurrentGatheringLevel()",
            "AtLevel(GeneratorSpec spec, int requestedLevel)",
            "GATHER_LEVEL_PROJECTED_PER_REQUEST",
        ],
    }
    for path, needles in checks.items():
        text, _ = read_text(path)
        for needle in needles:
            if needle not in text:
                die(f"validacao textual falhou: {path.name} nao contem {needle!r}")

    # Regressions obvias que nao devem sobrar.
    skills, _ = read_text(core / "Player.Skills.cs")
    surv, _ = read_text(core / "SurvivalState.cs")
    if re.search(r"AlphaTestPlayerExpMultiplier\s*=\s*20", skills) or "ALPHA XP x20" in skills:
        die("validacao: XP x20 ainda presente")
    if "AlphaTestRestFatigueMultiplier" in surv:
        die("validacao: nome antigo AlphaTestRestFatigueMultiplier ainda presente")


def run(cmd: list[str], cwd: Path, *, optional: bool = False) -> bool:
    print("+", " ".join(cmd))
    try:
        subprocess.run(cmd, cwd=cwd, check=True)
        return True
    except FileNotFoundError:
        if optional:
            print(f"AVISO: {cmd[0]} nao disponivel; etapa ignorada")
            return False
        raise


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=".", help="raiz do Project_DX-D-Brasil")
    ap.add_argument("--no-build", action="store_true", help="nao rodar dotnet build")
    ap.add_argument("--backup", action="store_true", help="criar .pre-alpha-hotfix.bak (opcional; Git ja serve como rollback)")
    args = ap.parse_args()

    root = Path(args.repo).resolve()
    core = root / CORE_REL
    required = [
        core / "Player.Skills.cs",
        core / "SurvivalState.cs",
        core / "Player.cs",
        core / "Player.Gathering.cs",
    ]
    missing = [str(p) for p in required if not p.is_file()]
    if missing:
        die("arquivos nao encontrados; confirme a raiz do repo:\n  " + "\n  ".join(missing))

    # Guarda contra aplicar sobre uma versao diferente da main revisada em 2026-09-26.
    expected_blobs = {
        (CORE_REL / "Player.Skills.cs").as_posix(): "265a31a69c8aba8420ca2b2753537863ef266ee7",
        (CORE_REL / "SurvivalState.cs").as_posix(): "4d3433d2c5d5b09ee7f53b900845fdf76968436d",
        (CORE_REL / "Player.Gathering.cs").as_posix(): "6d8ee4f9a0d2f5b8712b89c83b8ed55fe35f1db1",
        (CORE_REL / "Player.cs").as_posix(): "5c65b091cde28c7c9b75166fa0f08a6b04db81dc",
    }
    if (root / ".git").exists():
        for rel, expected in expected_blobs.items():
            dirty = subprocess.run(["git", "diff", "--quiet", "--", rel], cwd=root).returncode
            if dirty != 0:
                die(f"{rel}: ha alteracoes locais; salve/stash antes de aplicar")
            actual = subprocess.check_output(["git", "rev-parse", f"HEAD:{rel}"], cwd=root, text=True).strip()
            if actual != expected:
                die(f"{rel}: blob HEAD {actual} difere da main revisada {expected}; reaudite antes de aplicar")

    if args.backup:
        for p in required:
            bak = p.with_name(p.name + ".pre-alpha-hotfix.bak")
            if not bak.exists():
                shutil.copy2(p, bak)

    patch_skills(core / "Player.Skills.cs")
    patch_survival(core / "SurvivalState.cs")
    patch_player(core / "Player.cs")
    patch_gathering(core / "Player.Gathering.cs")
    validate_text(root)
    print("OK: hotfix aplicado; validacoes textuais passaram.")

    server = root / SERVER_REL
    if not args.no_build:
        run(["dotnet", "build", "DurangoServer.csproj", "-c", "Release"], server, optional=True)

    # Se houver git, valida whitespace e mostra somente o diff dos quatro arquivos.
    if (root / ".git").exists():
        run(["git", "-c", "core.whitespace=cr-at-eol", "diff", "--check"], root, optional=True)
        run([
            "git", "diff", "--",
            str(CORE_REL / "Player.Skills.cs"),
            str(CORE_REL / "SurvivalState.cs"),
            str(CORE_REL / "Player.cs"),
            str(CORE_REL / "Player.Gathering.cs"),
        ], root, optional=True)


if __name__ == "__main__":
    main()
