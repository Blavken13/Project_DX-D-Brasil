"""Build the Brazilian catalogs from the original Portuguese catalog in the client.

Only message translations are changed. Original gettext IDs, contexts and game JSON
identifiers remain unchanged. No external translation service is used.
"""
from __future__ import annotations

import argparse
import collections
import gettext
import io
import json
from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parent.parent
HERE = Path(__file__).resolve().parent
MAGIC = b"\xde\x12\x04\x95"


def read_mo(data: bytes, start: int = 0) -> tuple[dict[str, str], int]:
    magic, revision, count, originals, translations, _, _ = struct.unpack_from("<7I", data, start)
    if magic != 0x950412DE or revision > 1 or not 0 < count < 100_000:
        raise ValueError("Invalid gettext header")
    result = {}
    end = 28
    for index in range(count):
        pair = []
        for table in (originals, translations):
            length, offset = struct.unpack_from("<2I", data, start + table + index * 8)
            if offset + length >= len(data) - start:
                raise ValueError("Invalid gettext string offset")
            raw = data[start + offset:start + offset + length]
            # The original metadata contains a legacy invalid byte; displayed text
            # must always be strict UTF-8. Metadata is replaced in the output.
            pair.append(raw.decode("utf-8", errors="replace" if index == 0 else "strict"))
            end = max(end, offset + length + 1)
        result[pair[0]] = pair[1]
    return result, end


def embedded_catalogs() -> dict[str, dict[str, str]]:
    data = (ROOT / "Durango-OffServer/DurangoV2_Data/resources.assets").read_bytes()
    catalogs = {}
    offset = 0
    while (offset := data.find(MAGIC, offset)) >= 0:
        try:
            messages, _ = read_mo(data, offset)
            language = re.search(r"(?m)^Language: ([^\n]+)", messages.get("", ""))
            if language:
                catalogs[language[1].strip()] = messages
        except (ValueError, UnicodeError, struct.error):
            pass
        offset += 4
    if "pt_BR" not in catalogs or "en_US" not in catalogs:
        raise ValueError("Original Portuguese and English catalogs were not found")
    return catalogs


def markers(text: str) -> collections.Counter:
    # Preserve named/positional placeholders, markup and icon/resource identifiers.
    return collections.Counter(re.findall(r"\{[^{}]*\}|<[^>]*>|\[[^\]]*\]", text))


def compile_mo(messages: dict[str, str]) -> bytes:
    entries = sorted(((k.encode("utf-8"), v.encode("utf-8")) for k, v in messages.items()), key=lambda p: p[0])
    count = len(entries)
    originals = 28
    translations = originals + count * 8
    offset = translations + count * 8
    original_table = bytearray()
    translated_table = bytearray()
    payload = bytearray()
    for key, _ in entries:
        original_table += struct.pack("<2I", len(key), offset + len(payload))
        payload += key + b"\0"
    for _, value in entries:
        translated_table += struct.pack("<2I", len(value), offset + len(payload))
        payload += value + b"\0"
    return struct.pack("<7I", 0x950412DE, 0, count, originals, translations, 0, 0) + original_table + translated_table + payload


def export_po(messages: dict[str, str]) -> str:
    lines = ["# Durango Brasil - portugues brasileiro; IDs originais preservados."]
    quote = lambda value: json.dumps(value, ensure_ascii=False)
    for original, translated in sorted(messages.items()):
        if "\x04" in original:
            context, original = original.split("\x04", 1)
            lines.append("msgctxt " + quote(context))
        if "\0" in original:
            raise ValueError("Plural entry requires a plural-aware PO export")
        lines.extend(("msgid " + quote(original), "msgstr " + quote(translated), ""))
    return "\n".join(lines)


def build() -> tuple[dict[str, str], dict]:
    catalogs = embedded_catalogs()
    messages = dict(catalogs["pt_BR"])
    messages[""] = (
        "Project-Id-Version: Durango Brasil\nLanguage: pt_BR\n"
        "MIME-Version: 1.0\nContent-Type: text/plain; charset=UTF-8\n"
        "Content-Transfer-Encoding: 8bit\nPlural-Forms: nplurals=2; plural=(n > 1);\n"
    )
    overrides = json.loads((HERE / "catalog-pt_BR.json").read_text(encoding="utf-8"))
    replaced = 0
    for key, english in catalogs["en_US"].items():
        if key and messages.get(key) == english and english in overrides:
            translated = overrides[english]
            if markers(english) != markers(translated):
                raise ValueError(f"Formatting changed for {key!r}")
            messages[key] = translated
            replaced += 1
    # Supplement community texts and locally added UI labels. These are message
    # keys for lookup, never replacements of IDs in game data or protocol fields.
    for mapping in json.loads((HERE / "client-pt_BR.json").read_text(encoding="utf-8")).values():
        for original, translated in mapping.items():
            if markers(original) != markers(translated):
                raise ValueError(f"Client formatting changed for {original!r}")
            messages.setdefault(original, translated)
    messages["#config_leave"] = "Excluir conta"
    messages["#config_account"] = "Conta"
    for original, translated in json.loads((HERE / "additional-pt_BR.json").read_text(encoding="utf-8")).items():
        if markers(original) != markers(translated):
            raise ValueError(f"Additional formatting changed for {original!r}")
        messages[original] = translated
    return messages, {"native_entries": len(catalogs["pt_BR"]) - 1, "completed_english": replaced,
                      "entries": len(messages) - 1, "language": "pt_BR"}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="Validate without writing catalogs")
    options = parser.parse_args()
    messages, report = build()
    compiled = compile_mo(messages)
    decoded, _ = read_mo(compiled)
    if decoded != messages:
        raise ValueError("MO round-trip mismatch")
    catalog = gettext.GNUTranslations(io.BytesIO(compiled))
    if catalog.info().get("language") != "pt_BR":
        raise ValueError("Invalid language metadata")
    outputs = [ROOT / "Durango-CustomServer/server/data/locales/pt_BR/LC_MESSAGES/messages.mo",
               ROOT / "Durango-OffServer/Languages/pt_BR/messages.mo"]
    po_output = ROOT / "Durango-OffServer/Languages/pt_BR/messages.po"
    po_text = export_po(messages)
    if options.check:
        for output in outputs:
            if output.read_bytes() != compiled:
                raise ValueError(f"Outdated catalog: {output}")
        if po_output.read_text(encoding="utf-8") != po_text:
            raise ValueError(f"Outdated catalog: {po_output}")
    if not options.check:
        for output in outputs:
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(compiled)
        po_output.write_text(po_text, encoding="utf-8", newline="\n")
        (HERE / "coverage-pt_BR.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
