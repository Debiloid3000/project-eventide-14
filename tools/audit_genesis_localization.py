#!/usr/bin/env python3
"""Temporary read-only audit for Genesis entity localization coverage."""
from collections import defaultdict
from pathlib import Path
import re

ROOT = Path(".")
PROTO = ROOT / "Resources/Prototypes/_Genesis"
LOCALES = ("ru-RU", "en-US")
KEY_RE = re.compile(r"^([A-Za-z][A-Za-z0-9_-]*)\\s*=", re.MULTILINE)
ENTITY_RE = re.compile(r"^- type:\\s*entity\\s*$", re.MULTILINE)
ID_RE = re.compile(r"^  id:\\s*([A-Za-z0-9_-]+)\\s*$", re.MULTILINE)
DIRECT_LOC_RE = re.compile(r"^  (?:name|description):\\s*\\S", re.MULTILINE)


def locale_files(locale):
    return sorted((ROOT / "Resources/Locale" / locale).rglob("*.ftl"))


def read_locale(locale):
    keys = set()
    origins = defaultdict(list)
    genesis_keys = set()
    file_count = 0
    for path in locale_files(locale):
        file_count += 1
        text = path.read_text(encoding="utf-8-sig")
        for match in KEY_RE.finditer(text):
            key = match.group(1)
            keys.add(key)
            origins[key].append(str(path))
            if "/_genesis/" in str(path).replace("\\", "/"):
                genesis_keys.add(key)
    duplicates = {key: paths for key, paths in origins.items() if len(paths) > 1}
    genesis_duplicates = {
        key: paths for key, paths in duplicates.items()
        if any("/_genesis/" in path.replace("\\", "/") for path in paths)
    }
    return keys, genesis_keys, duplicates, genesis_duplicates, file_count


def read_entities():
    result = {}
    for path in sorted(PROTO.rglob("*.yml")) + sorted(PROTO.rglob("*.yaml")):
        text = path.read_text(encoding="utf-8-sig")
        blocks = re.split(r"(?m)(?=^- type:)", text)
        for block in blocks:
            if not ENTITY_RE.search(block):
                continue
            ident = ID_RE.search(block)
            if not ident or not DIRECT_LOC_RE.search(block):
                continue
            result.setdefault("ent-" + ident.group(1), []).append(str(path))
    return result


def main():
    print("Genesis source files:", sum(1 for p in PROTO.rglob("*") if p.is_file()))
    entity_keys = read_entities()
    print("Entity prototypes with a direct top-level name/description:", len(entity_keys))
    locale_data = {loc: read_locale(loc) for loc in LOCALES}
    for loc, (keys, genesis_keys, duplicates, gdups, count) in locale_data.items():
        print(f"\\n{loc}: {count} FTL files; {len(keys)} distinct Fluent IDs; {len(duplicates)} duplicate IDs repo-wide; {len(gdups)} duplicate IDs with at least one Genesis definition")
        print(f"{loc} Genesis-only IDs: {len(genesis_keys)}")
    missing = {}
    for loc in LOCALES:
        keys = locale_data[loc][0]
        miss = sorted(key for key in entity_keys if key not in keys)
        missing[loc] = miss
        print(f"\\nMissing entity localization keys in {loc}: {len(miss)}")
        for key in miss[:180]:
            print(f"  {key} <- {', '.join(entity_keys[key][:2])}")
        if len(miss) > 180:
            print(f"  ... {len(miss)-180} more omitted")
    ru_gen = locale_data["ru-RU"][1]
    en_gen = locale_data["en-US"][1]
    only_ru, only_en = sorted(ru_gen - en_gen), sorted(en_gen - ru_gen)
    print(f"\\nGenesis IDs present only in ru-RU: {len(only_ru)}")
    for key in only_ru[:100]:
        print("  RU only:", key)
    print(f"\\nGenesis IDs present only in en-US: {len(only_en)}")
    for key in only_en[:100]:
        print("  EN only:", key)
    print("\\nGenesis duplicate key samples:")
    for loc in LOCALES:
        gdups = locale_data[loc][3]
        for key, paths in sorted(gdups.items())[:100]:
            print(f"  {loc}: {key}: {' | '.join(paths)}")
    print("\\nAudit completed; output is diagnostic and does not modify any resources.")


if __name__ == "__main__":
    main()
