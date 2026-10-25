#!/usr/bin/env python3
"""JASM translation pipeline -- one self-contained, in-repo localization workflow.

Everything lives under this repo. No temp files. No per-language scripts.

Resource files (per locale) live at::

    src/GIMI-ModManager.WinUI/Strings/<locale>/{Resources,Settings,Startup}.resw

The CSV format is::

    File, Key, English, Context Comment, Reference (<locale>), Translation

``File`` is one of ``Resources`` / ``Settings`` / ``Startup`` (keys never collide
across the three, but the column keeps the mapping explicit). ``Context Comment``
comes from the ``<!-- ... -->`` comment preceding a string's ``<value>`` in
``en-us``. ``Translation`` is what a translator fills in.

Commands
--------
export   Build a template CSV from ``en-us`` (+ a reference locale).
split    Chop a CSV into batches of N rows.
apply    Write the ``Translation`` column back into a locale's ``.resw`` files.
validate Check key parity, XML well-formedness, and placeholder preservation.
stats    Show per-locale translation progress.
audit    Report every localization gap class (non-zero exit on hard gaps).
orphans  List keys no XAML uid / x:Uid / C# literal can reach (dead translations).
missing  List C# key lookups with no key (silent English fallback in every locale).
prune    Delete keys (File,Key list) from every locale's .resw files.
rename   Rename keys (File,Old,New list) in every locale, keeping their values.

Typical use
-----------
New locale (example ``fr``)::

    python translations/pipeline.py export --target fr
    python translations/pipeline.py split translations/batches/fr/template.csv --size 15
    # ... translate the Translation column in each batch ...
    python translations/pipeline.py apply translations/batches/fr --locale fr
    python translations/pipeline.py validate

Update strings for an existing locale::

    python translations/pipeline.py export --target pt-br
    # edit translations/batches/pt-br/template.csv
    python translations/pipeline.py apply translations/batches/pt-br/template.csv --locale pt-br
"""
from __future__ import annotations

import argparse
import csv
import errno
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from functools import cache
from pathlib import Path

# --- Layout -----------------------------------------------------------------

REPO_ROOT = Path(__file__).resolve().parents[1]
STRINGS_DIR = REPO_ROOT / "src" / "GIMI-ModManager.WinUI" / "Strings"
TRANSLATIONS_DIR = REPO_ROOT / "translations"
BATCHES_DIR = TRANSLATIONS_DIR / "batches"
MASTER_TEMPLATE = TRANSLATIONS_DIR / "template.csv"

RESW_FILES = ("Resources", "Settings", "Startup")
DEFAULT_REFERENCE = "es"
EN_LOCALE = "en-us"

TRANSLATION_COLUMN = "Translation"

# --- Parsing ----------------------------------------------------------------

# A <data name="..."> ... </data> block. The schema at the top of the file is
# <xs:element ...>, never <data name="...">, so it is not matched.
_DATA_RE = re.compile(r'<data\s+name="([^"]+)"[^>]*>(.*?)</data>', re.DOTALL)
# <value> ... </value> (also tolerates attributes such as xml:space).
_VALUE_RE = re.compile(r'(<value(?:\s[^>]*)?>)(.*?)(</value>)', re.DOTALL)
# Context comment. This is the <!-- ... --> form, NOT the <comment> element.
_COMMENT_RE = re.compile(r"<!--\s*(.*?)\s*-->", re.DOTALL)
# A <data> block *with* its leading indentation -- used when appending a key to a
# locale so the new entry keeps the file's layout.
_INDENTED_DATA_RE = re.compile(r'([ \t]*)<data\s+name="([^"]+)"[^>]*>.*?</data>', re.DOTALL)
# Placeholders that must survive translation: {0}, {1}, {{TargetPath}}, ...
_PLACEHOLDER_RE = re.compile(r"\{\{.*?\}\}|\{[^{}]*\}")

ENCODING = "utf-8"
CSV_ENCODING = "utf-8-sig"


@dataclass
class ReswEntry:
    key: str
    value: str
    comment: str


# --- resw I/O ---------------------------------------------------------------


def load_resw(path: Path) -> tuple[str, bool, bool]:
    """Read a .resw, returning (lf_text, uses_crlf, has_trailing_newline)."""
    raw = path.read_bytes()
    uses_crlf = b"\r\n" in raw
    text = raw.decode(ENCODING).replace("\r\n", "\n")
    has_trailing_newline = text.endswith("\n")
    return text, uses_crlf, has_trailing_newline


def save_resw(path: Path, text: str, uses_crlf: bool, has_trailing_newline: bool) -> None:
    """Write a .resw, restoring the original newline style and trailing newline."""
    text = text.rstrip("\n")
    if has_trailing_newline:
        text += "\n"
    path.parent.mkdir(parents=True, exist_ok=True)
    newline = "\r\n" if uses_crlf else "\n"
    with open(path, "w", encoding=ENCODING, newline=newline) as handle:
        handle.write(text)


def parse_entries(text: str) -> dict[str, ReswEntry]:
    """Parse <data> blocks into an ordered {key: ReswEntry} mapping."""
    entries: dict[str, ReswEntry] = {}
    for match in _DATA_RE.finditer(text):
        key = match.group(1)
        block = match.group(0)
        value_match = _VALUE_RE.search(block)
        comment_match = _COMMENT_RE.search(block)
        entries[key] = ReswEntry(
            key=key,
            value=value_match.group(2) if value_match else "",
            comment=comment_match.group(1).strip() if comment_match else "",
        )
    return entries


def read_locale(locale: str, resw_file: str) -> dict[str, ReswEntry]:
    path = STRINGS_DIR / locale / f"{resw_file}.resw"
    if not path.exists():
        return {}
    return parse_entries(load_resw(path)[0])


def placeholders(value: str) -> list[str]:
    return sorted(_PLACEHOLDER_RE.findall(value))


# --- CSV I/O ----------------------------------------------------------------


def write_csv(path: Path, rows: list[dict[str, str]], fieldnames: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding=CSV_ENCODING, newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def rel(path: Path) -> str:
    """Best-effort repo-relative path for display."""
    try:
        return str(path.resolve().relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def read_csv(path: Path) -> tuple[list[dict[str, str]], list[str]]:
    with open(path, "r", encoding=CSV_ENCODING, newline="") as handle:
        reader = csv.DictReader(handle)
        return list(reader), list(reader.fieldnames or [])


# --- Commands ---------------------------------------------------------------


def cmd_export(args: argparse.Namespace) -> int:
    reference = args.reference
    target = args.target

    if args.out:
        out_path = Path(args.out)
    elif target:
        out_path = BATCHES_DIR / target / "template.csv"
    else:
        out_path = MASTER_TEMPLATE

    reference_col = f"Reference ({reference})"
    fieldnames = [
        "File",
        "Key",
        "English",
        "Context Comment",
        reference_col,
        TRANSLATION_COLUMN,
    ]

    rows: list[dict[str, str]] = []
    for resw_file in RESW_FILES:
        en_entries = read_locale(EN_LOCALE, resw_file)
        ref_entries = read_locale(reference, resw_file)
        tgt_entries = read_locale(target, resw_file) if target else {}
        for key, entry in en_entries.items():
            rows.append(
                {
                    "File": resw_file,
                    "Key": key,
                    "English": entry.value,
                    "Context Comment": entry.comment,
                    reference_col: ref_entries.get(key, ReswEntry(key, "", "")).value,
                    TRANSLATION_COLUMN: tgt_entries.get(key, ReswEntry(key, "", "")).value,
                }
            )

    write_csv(out_path, rows, fieldnames)
    print(f"Exported {len(rows)} strings to {rel(out_path)}")
    print(f"  reference: {reference}" + (f"   target: {target}" if target else ""))
    return 0


def cmd_split(args: argparse.Namespace) -> int:
    csv_path = Path(args.csv)
    rows, fieldnames = read_csv(csv_path)
    size = args.size
    if size < 1:
        print("--size must be >= 1", file=sys.stderr)
        return 2

    name = args.name or csv_path.stem
    out_dir = Path(args.out) if args.out else csv_path.parent / "batches"
    out_dir.mkdir(parents=True, exist_ok=True)

    total = 0
    for index, start in enumerate(range(0, len(rows), size), start=1):
        chunk = rows[start : start + size]
        batch_path = out_dir / f"{name}_batch_{index:02d}.csv"
        write_csv(batch_path, chunk, fieldnames)
        total += 1
    print(f"Wrote {total} batch(es) of up to {size} rows to {rel(out_dir)}")
    return 0


def _apply_values(text: str, translations: dict[str, str]) -> tuple[str, int]:
    """Replace <value> contents for translated keys. Returns (text, count)."""
    applied = 0

    def repl(match: re.Match[str]) -> str:
        nonlocal applied
        key = match.group(1)
        if key not in translations:
            return match.group(0)
        block = match.group(0)
        value_match = _VALUE_RE.search(block)
        if not value_match:
            return block
        new_value = translations[key]
        applied += 1
        return block[: value_match.start(2)] + new_value + block[value_match.end(2) :]

    return _DATA_RE.sub(repl, text), applied


def _add_missing(
    text: str,
    translations: dict[str, str],
    en_text: str,
    seed_all: bool = False,
) -> tuple[str, int]:
    """Append entries that exist in en-us but are absent from the target file.

    With ``seed_all`` every missing en-us key is added (untranslated ones fall
    back to English) so the locale always reaches key parity. Otherwise only
    keys that have a translation are added.
    """
    present = {m.group(1) for m in _DATA_RE.finditer(text)}
    en_blocks = {m.group(2): m.group(0) for m in _INDENTED_DATA_RE.finditer(en_text)}
    en_entries = parse_entries(en_text)
    keys = en_entries if seed_all else [k for k in translations if k in en_entries]
    added = 0
    for key in keys:
        if key in present or key not in en_blocks:
            continue
        value = translations.get(key, en_entries[key].value)
        block = en_blocks[key]
        value_match = _VALUE_RE.search(block)
        if not value_match:
            continue
        block = block[: value_match.start(2)] + value + block[value_match.end(2) :]
        text = text.replace("</root>", block + "\n</root>", 1)
        present.add(key)
        added += 1
    return text, added


def cmd_apply(args: argparse.Namespace) -> int:
    sources = [Path(args.path)]
    csv_paths: list[Path] = []
    if sources[0].is_dir():
        csv_paths = sorted(sources[0].glob("*.csv"))
        if not csv_paths:
            print(f"No CSV files found in {sources[0]}", file=sys.stderr)
            return 2
    elif sources[0].exists():
        csv_paths = sources
    else:
        print(f"Not found: {sources[0]}", file=sys.stderr)
        return 2

    # Group translations by resource file.
    by_file: dict[str, dict[str, str]] = {}
    for csv_path in csv_paths:
        rows, _ = read_csv(csv_path)
        for row in rows:
            resw_file = (row.get("File") or "").strip()
            key = (row.get("Key") or "").strip()
            value = (row.get(TRANSLATION_COLUMN) or "").strip()
            if not resw_file or not key or not value:
                continue
            by_file.setdefault(resw_file, {})[key] = row[TRANSLATION_COLUMN]

    if not by_file:
        print("No non-empty translations found.", file=sys.stderr)
        return 2

    locale = args.locale
    total_applied = 0
    total_added = 0
    for resw_file in RESW_FILES:
        translations = by_file.get(resw_file, {})
        if not translations:
            continue

        en_text, _, _ = load_resw(STRINGS_DIR / EN_LOCALE / f"{resw_file}.resw")
        target_path = STRINGS_DIR / locale / f"{resw_file}.resw"

        if target_path.exists():
            text, uses_crlf, trailing = load_resw(target_path)
        else:
            # Seed a brand-new locale with the full English structure so the
            # file reaches key parity immediately (untranslated = English).
            text, uses_crlf, trailing = load_resw(STRINGS_DIR / EN_LOCALE / f"{resw_file}.resw")
            print(f"  creating {rel(target_path)} from en-us seed")

        # Add missing keys *before* writing values: a CSV that introduces a key and
        # carries its translation in one pass would otherwise be seeded from English
        # and the translation silently dropped (the key did not exist yet when the
        # value pass ran).
        text, added = _add_missing(text, translations, en_text, seed_all=not args.no_seed)
        text, applied = _apply_values(text, translations)
        save_resw(target_path, text, uses_crlf, trailing)
        total_applied += applied
        total_added += added
        print(
            f"  {locale}/{resw_file}.resw: {applied} updated"
            + (f", {added} added" if added else "")
        )

    print(f"Applied {total_applied} translation(s)" + (f" (+{total_added} added)" if total_added else ""))
    return 0


def cmd_validate(args: argparse.Namespace) -> int:
    problems: list[str] = []
    warnings: list[str] = []
    en_entries = {f: read_locale(EN_LOCALE, f) for f in RESW_FILES}

    locales = sorted(
        p.name for p in STRINGS_DIR.iterdir() if p.is_dir() and p.name != EN_LOCALE
    )
    if args.locale:
        locales = [args.locale]

    for locale in locales:
        for resw_file in RESW_FILES:
            path = STRINGS_DIR / locale / f"{resw_file}.resw"
            if not path.exists():
                problems.append(f"{locale}/{resw_file}.resw: missing file")
                continue

            text, _, _ = load_resw(path)
            try:
                ET.fromstring(text)
            except ET.ParseError as exc:
                problems.append(f"{locale}/{resw_file}.resw: XML invalid -- {exc}")

            if text.count("</root>") != 1:
                problems.append(
                    f"{locale}/{resw_file}.resw: expected exactly one </root> "
                    f"(found {text.count('</root>')})"
                )

            entries = parse_entries(text)
            en = en_entries[resw_file]
            missing = sorted(set(en) - set(entries))
            extra = sorted(set(entries) - set(en))
            if missing:
                problems.append(
                    f"{locale}/{resw_file}.resw: {len(missing)} key(s) missing vs en-us "
                    f"(e.g. {', '.join(missing[:3])})"
                )
            if extra:
                problems.append(
                    f"{locale}/{resw_file}.resw: {len(extra)} extra key(s) vs en-us "
                    f"(e.g. {', '.join(extra[:3])})"
                )

            for key in sorted(set(en) & set(entries)):
                en_ph = placeholders(en[key].value)
                tr_ph = placeholders(entries[key].value)
                if en_ph != tr_ph:
                    warnings.append(
                        f"{locale}/{resw_file}.resw: {key}: placeholder mismatch "
                        f"en={en_ph} translated={tr_ph}"
                    )

    for warning in warnings:
        print(f"WARN: {warning}")

    if problems:
        print(f"FAILED -- {len(problems)} problem(s):")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    if warnings and args.strict:
        print(f"FAILED (--strict) -- {len(warnings)} placeholder warning(s).")
        return 1

    scope = args.locale or f"all {len(locales)} non-English locale(s)"
    suffix = f" ({len(warnings)} placeholder warning(s))" if warnings else ""
    print(f"OK -- {scope}: key parity and XML good{suffix}.")
    return 0


def cmd_stats(args: argparse.Namespace) -> int:
    en_entries = {f: read_locale(EN_LOCALE, f) for f in RESW_FILES}
    gran_total = sum(len(v) for v in en_entries.values())

    print(f"{'Locale':<8} {'Present':>8} {'Translated':>11} {'Coverage':>9}")
    print("-" * 40)
    for locale_dir in sorted(p for p in STRINGS_DIR.iterdir() if p.is_dir()):
        locale = locale_dir.name
        if locale == EN_LOCALE:
            continue
        present = translated = 0
        for resw_file in RESW_FILES:
            entries = read_locale(locale, resw_file)
            present += len(entries)
            translated += sum(
                1
                for key, entry in entries.items()
                if key in en_entries[resw_file]
                and entry.value.strip()
                and entry.value != en_entries[resw_file][key].value
            )
        coverage = (translated / gran_total * 100) if gran_total else 0.0
        print(f"{locale:<8} {present:>8} {translated:>11} {coverage:>8.1f}%")
    print(f"\nEnglish baseline: {gran_total} strings "
          f"({', '.join(f'{f}={len(en_entries[f])}' for f in RESW_FILES)})")
    return 0


# --- Orphans -----------------------------------------------------------------
#
# A key is *reachable* when any of these holds; otherwise it is an orphan (dead
# translation). Keep this list in sync with translations/README.md:
#   1. an XAML element declares l:Uids.Uid="X" (or "/<File>/X"): every key whose
#      uid part (the resw name up to the first '.') is X, in that file's resw;
#   2. XAML declares the framework x:Uid="X";
#   3. the literal appears in C# as "Key" or "/Settings/Key" (the /Settings/ form
#      is what resolves a key stored in Settings.resw) -- including literals that
#      reach a localizer through a helper (Format/Notify/Localize/GetLocalized)
#      or the "Key".GetLocalized() extension;
#   4. the key is an enum member name -- SettingsViewModel localizes
#      ModUpdateAvailableChecker.Status.ToString() directly, so Running/Waiting/
#      Stopped/Error are live without being spelled out as an argument anywhere;
#   5. the key starts with a literal used as a *prefix* at a call site
#      ("Category_" + category.DisplayNamePlural...), which is how ten category
#      keys are looked up at runtime (see literal_lookups).
# A key whose base name a C# lookup asks for under another spelling is not an
# orphan either: it is misnamed, and `missing` prints the rename to make.

UI_DIR = REPO_ROOT / "src" / "GIMI-ModManager.WinUI"
SRC_DIR = REPO_ROOT / "src"
SKIP_DIRS = {"obj", "bin", ".vs", ".git"}

_UID_ATTR_RE = re.compile(r'l:Uids\.Uid\s*=\s*"([^"]+)"')
_XUID_ATTR_RE = re.compile(r'\bx:Uid\s*=\s*"([^"]+)"')
_ENUM_RE = re.compile(r"\benum\s+\w+\s*(?::[^{]+)?\{(.*?)\}", re.DOTALL)


def _wanted(path: Path) -> bool:
    return not any(part in SKIP_DIRS for part in path.parts)


def split_uid(uid: str) -> tuple[str, str]:
    """"/Settings/Foo" -> ("Settings", "Foo");  "Foo" -> ("Resources", "Foo")."""
    if uid.startswith("/"):
        parts = [part for part in uid.split("/") if part]
        if len(parts) >= 2:
            return parts[0], parts[1]
    return "Resources", uid


def uid_part(key: str) -> str:
    """The uid a key is reachable through: the resw name up to the first '.'."""
    index = key.find(".")
    return key[:index] if index > 1 else key


@cache
def _xaml_uid_targets() -> tuple[set[tuple[str, str]], set[str]]:
    """(uid targets, x:Uid keys) -- cached: `orphans` composes several scans."""
    uid_targets: set[tuple[str, str]] = set()
    xuid_keys: set[str] = set()
    for xaml in sorted(UI_DIR.rglob("*.xaml")):
        if not _wanted(xaml):
            continue
        text = xaml.read_text(encoding="utf-8-sig", errors="replace")
        uid_targets |= {split_uid(m.group(1)) for m in _UID_ATTR_RE.finditer(text)}
        xuid_keys |= set(_XUID_ATTR_RE.findall(text))
    return uid_targets, xuid_keys


@cache
def _cs_blob() -> str:
    """Every C# file under src/ as one blob (object/generated trees excluded).

    Quoted-token containment (`"Key" in blob`) is used instead of a string-literal
    regex: interpolated strings ($"...{GetLocalizedStringOrDefault("Key")}...")
    make naive quote pairing unreliable.
    """
    return "\n".join(
        path.read_text(encoding="utf-8-sig", errors="replace")
        for path in sorted(SRC_DIR.rglob("*.cs"))
        if _wanted(path)
    )


def reachability() -> tuple[set[tuple[str, str]], set[str], str, set[str]]:
    """(XAML uid targets, x:Uid keys, C# blob, enum member names)."""
    uid_targets, xuid_keys = _xaml_uid_targets()
    cs_blob = _cs_blob()
    enum_names: set[str] = set()
    for match in _ENUM_RE.finditer(cs_blob):
        for part in match.group(1).split(","):
            name = re.split(r"[=\n]", part.strip())[0].strip()
            if re.fullmatch(r"[A-Za-z_]\w*", name):
                enum_names.add(name)
    return uid_targets, xuid_keys, cs_blob, enum_names


# The first argument of a localizer call: an optional '$' (interpolated string) and
# the literal up to its closing quote. What follows that quote says whether the
# literal is a whole key ("Key", ...) or a prefix ("Category_" + name).
_LOCALIZER_ARG_RE = re.compile(r'GetLocalizedString(?:OrDefault)?\(\s*(\$?)"([^"\n]*)')


def literal_lookups(cs_blob: str) -> tuple[set[str], set[str]]:
    """(exact keys looked up from C#, dynamic key prefixes built at runtime).

    `"/Settings/X"` targets Settings.resw; a bare literal targets Resources.resw.
    A literal followed by `+`, or one opened with `$"` and cut by `{`, is a prefix:
    any key starting with it may be looked up (e.g. `"Category_" + name`).
    """
    exact: set[str] = set()
    prefixes: set[str] = set()
    for match in _LOCALIZER_ARG_RE.finditer(cs_blob):
        literal = match.group(2)
        if match.group(1) == "$" or "{" in literal:
            prefixes.add(literal.split("{", 1)[0])
            continue
        # match.end() sits on the literal's closing quote: `"Category_" + x` continues
        # with a '+', a whole key continues with ',' or ')'.
        after = cs_blob[match.end() :]
        tail = after[1:].lstrip() if after.startswith('"') else after.lstrip()
        if tail.startswith("+"):
            prefixes.add(literal)
        elif literal.startswith("/Settings/"):
            exact.add(f"Settings\t{literal.split('/', 2)[2]}")
        elif not literal.startswith("/"):
            exact.add(f"Resources\t{literal}")
    return exact, prefixes


def find_orphans(locale: str = EN_LOCALE) -> dict[str, list[str]]:
    """{resw file: unreferenced keys} for one locale (default en-us).

    Keys whose base name a C# lookup asks for under a different spelling are *not*
    orphans -- they are misnamed and must be renamed (see ``missing_keys``), so they
    are skipped here.
    """
    uid_targets, xuid_keys, cs_blob, enum_names = reachability()
    _, prefixes = literal_lookups(cs_blob)
    misnamed = set(missing_keys(locale))

    orphans: dict[str, list[str]] = {}
    for resw_file in RESW_FILES:
        for key in sorted(read_locale(locale, resw_file)):
            if f'"{key}"' in cs_blob or f'"/Settings/{key}"' in cs_blob:
                continue
            if key in xuid_keys or (resw_file, uid_part(key)) in uid_targets or key in enum_names:
                continue
            if any(key.startswith(prefix) for prefix in prefixes if prefix):
                continue
            if (resw_file, uid_part(key)) in misnamed:
                continue
            orphans.setdefault(resw_file, []).append(key)
    return orphans


def missing_keys(locale: str = EN_LOCALE) -> list[tuple[str, str]]:
    """Keys C# looks up by exact literal that the locale has no entry for.

    A miss is silent: `GetLocalizedStringOrDefault` returns the English fallback, so
    the string stays English in every language until the key is added. If a key with
    the same base name exists under a dotted/undersuffixed spelling, that key is the
    one to *rename* (never delete).
    """
    _, _, cs_blob, _ = reachability()
    exact, _ = literal_lookups(cs_blob)
    missing: list[tuple[str, str]] = []
    for wanted in sorted(exact):
        resw_file, key = wanted.split("\t", 1)
        if key not in read_locale(locale, resw_file):
            missing.append((resw_file, key))
    return missing


def cmd_missing(args: argparse.Namespace) -> int:
    _, _, cs_blob, _ = reachability()
    exact, prefixes = literal_lookups(cs_blob)
    missing = missing_keys(args.locale)
    print(f"C# lookups with no {args.locale} key: {len(missing)}")
    for resw_file, key in missing:
        near = sorted(
            candidate
            for candidate in read_locale(args.locale, resw_file)
            if uid_part(candidate) == key and candidate != key
        )
        hint = f"    rename {near[0]} -> {key}" if near else "    no candidate in resw"
        print(f"  {resw_file}: {key}\n{hint}")
    if prefixes:
        print(f"\nDynamic key prefixes built at runtime ({len(prefixes)}): "
              f"{', '.join(sorted(prefixes))}")
    print(f"\nExact literals checked: {len(exact)}")
    return 0


def cmd_orphans(args: argparse.Namespace) -> int:
    orphans = find_orphans(args.locale)
    en_entries = {f: read_locale(EN_LOCALE, f) for f in RESW_FILES}
    total = sum(len(keys) for keys in orphans.values())
    print(f"Orphan keys in {args.locale}: {total}\n")
    for resw_file in RESW_FILES:
        keys = orphans.get(resw_file, [])
        if not keys:
            continue
        print(f"## {resw_file}.resw  ({len(keys)})")
        for key in keys:
            value = " ".join(en_entries[resw_file].get(key, ReswEntry(key, "", "")).value.split())
            print(f"  {key}" + (f"  -- {value[:90]}" if value else ""))
        print()
    if args.emit:
        fieldnames = ["File", "Key"]
        rows = [
            {"File": resw_file, "Key": key}
            for resw_file in RESW_FILES
            for key in orphans.get(resw_file, [])
        ]
        write_csv(Path(args.emit), rows, fieldnames)
        print(f"Wrote {total} key(s) to {rel(Path(args.emit))} -- "
              f"remove them with: python translations/pipeline.py prune {rel(Path(args.emit))}")
    return 0


# --- Audit -------------------------------------------------------------------
#
# One report for every gap class this repo can detect. `--fail-on-gaps` makes the
# hard classes (anything that leaves a string English or silently drops a runtime
# value) fail CI; the untranslated count is informational so it can burn down.

# Properties WinUI3Localizer can set on the element that declares the uid.
LOCALIZABLE_PROPS = (
    "Text", "Header", "Content", "Title", "Message", "PlaceholderText", "Description",
    "CloseButtonText", "PrimaryButtonText", "SecondaryButtonText", "ToolTipService.ToolTip",
    "ToolTip", "Watermark", "AutomationProperties.Name",
)
_XAML_TAG_RE = re.compile(r"<([A-Za-z][\w.:]*)([^<>]*?)/?>", re.S)
_XAML_ATTR_RE = re.compile(r'([\w.:]+)\s*=\s*"([^"]*)"', re.S)


def xaml_gaps(locale: str = EN_LOCALE) -> tuple[list[tuple], list[tuple]]:
    """(missing uid+literal keys, clobbered uid+literal pairs) for one locale.

    An element that declares `l:Uids.Uid="X"` with a design-time literal on a
    localizable property needs `X.<Property>` in that file's resw. A literal that
    the parser sees *after* the uid overwrites whatever the localizer applied, so
    the key can never show (pitfall 22) -- reported separately as clobbered.
    """
    keys = {resw_file: read_locale(locale, resw_file) for resw_file in RESW_FILES}
    missing: list[tuple] = []
    clobbered: list[tuple] = []
    for xaml in sorted(UI_DIR.rglob("*.xaml")):
        if not _wanted(xaml):
            continue
        text = xaml.read_text(encoding="utf-8-sig", errors="replace")
        for match in _XAML_TAG_RE.finditer(text):
            ordered = _XAML_ATTR_RE.findall(match.group(2))
            uid = dict(ordered).get("l:Uids.Uid")
            if not uid:
                continue
            resw_file, key_base = split_uid(uid)
            line = text.count("\n", 0, match.start()) + 1
            uid_index = next(i for i, (name, _) in enumerate(ordered) if name == "l:Uids.Uid")
            for index, (prop, value) in enumerate(ordered):
                if prop not in LOCALIZABLE_PROPS or not value or value.startswith("{"):
                    continue
                key = f"{key_base}.{prop}"
                row = (f"{rel(xaml)}:{line}", uid, key, " ".join(value.split())[:80])
                if index > uid_index:
                    clobbered.append(row)
                elif key not in keys[resw_file]:
                    missing.append(row)
    return missing, clobbered


def placeholder_gaps(locale: str | None = None) -> list[tuple[str, str, str, list[str], list[str]]]:
    """Translations whose placeholders differ from en-us (the value vanishes at runtime)."""
    en = {resw_file: read_locale(EN_LOCALE, resw_file) for resw_file in RESW_FILES}
    gaps: list[tuple[str, str, str, list[str], list[str]]] = []
    for loc in target_locales(locale):
        if loc == EN_LOCALE:
            continue
        for resw_file in RESW_FILES:
            for key, entry in read_locale(loc, resw_file).items():
                source = en[resw_file].get(key)
                if source is None:
                    continue
                expected, actual = placeholders(source.value), placeholders(entry.value)
                if expected != actual:
                    gaps.append((loc, resw_file, key, expected, actual))
    return gaps


def untranslated(locale: str = EN_LOCALE) -> dict[str, list[str]]:
    """{resw file: keys whose value still equals en-us} for one locale."""
    en = {resw_file: read_locale(EN_LOCALE, resw_file) for resw_file in RESW_FILES}
    same: dict[str, list[str]] = {}
    for resw_file in RESW_FILES:
        for key, entry in read_locale(locale, resw_file).items():
            source = en[resw_file].get(key)
            if source is not None and entry.value.strip() and entry.value == source.value:
                same.setdefault(resw_file, []).append(key)
    return same


def cmd_audit(args: argparse.Namespace) -> int:
    xaml_missing, clobbered = xaml_gaps(args.locale)
    csharp_missing = missing_keys(args.locale)
    orphans = find_orphans(args.locale)
    placeholders_ = placeholder_gaps(args.locale)
    orphan_count = sum(len(keys) for keys in orphans.values())

    hard = {
        "missing keys (XAML uid + literal)": len(xaml_missing),
        "clobbered uid order (literal parsed after the uid)": len(clobbered),
        "missing keys (C# lookups)": len(csharp_missing),
        "orphan keys (nothing references them)": orphan_count,
        "placeholder mismatches (value would vanish)": len(placeholders_),
    }
    total = sum(hard.values())

    print(f"Localization gaps ({args.locale} source, all locales for placeholders)\n")
    width = max(len(label) for label in hard)
    for label, count in hard.items():
        print(f"  {label:<{width}}  {count:>4}")
    print(f"  {'-' * width}  {'-' * 4}")
    print(f"  {'hard gaps (CI gate)':<{width}}  {total:>4}\n")

    for label, rows in (
        ("missing XAML keys (add <uid>.<Property>, or move the uid last)", xaml_missing),
        ("missing C# keys (add the key, or point the call site at the existing spelling)", csharp_missing),
        ("orphan keys (delete with prune, or rename with rename)", [
            (resw_file, key) for resw_file, keys in orphans.items() for key in keys
        ]),
        ("placeholder mismatches", placeholders_),
    ):
        if not rows:
            continue
        print(f"## {label}  ({len(rows)})")
        if args.details:
            for row in rows:
                print("  " + "  ".join(str(part) for part in row))
        else:
            print(f"  {len(rows)} row(s) hidden -- re-run with --details")
        print()

    untranslated_counts = {}
    for loc in target_locales(None):
        if loc == EN_LOCALE:
            continue
        untranslated_counts[loc] = sum(len(keys) for keys in untranslated(loc).values())
    print("Untranslated (value still equals en-us; informational, not gated)")
    for loc, count in untranslated_counts.items():
        print(f"  {loc:<7} {count:>4}")
    if args.details and any(untranslated_counts.values()):
        for loc in untranslated_counts:
            keys = [key for keys in untranslated(loc).values() for key in keys]
            if keys:
                print(f"  {loc}: {', '.join(sorted(keys))}")

    if total:
        print(f"\nFAILED -- {total} hard gap(s).")
        return 1
    print("\nOK -- no hard gaps.")
    return 0


# --- Maintenance ------------------------------------------------------------
#
# Deleting or renaming a key must happen in *every* locale at once: ``validate``
# fails on keys that exist in a locale but not in en-us, and ``apply`` seeds new
# keys from English (so a prune+apply "rename" would silently drop the locale's
# own translation). These two commands move the locale's own value across.


def target_locales(locale: str | None) -> list[str]:
    if locale:
        return [locale]
    return sorted(p.name for p in STRINGS_DIR.iterdir() if p.is_dir())


def read_key_list(path: Path) -> dict[str, set[str]]:
    """Read a ``File,Key`` list (or bare keys) into {resw file: keys}.

    A line without a resource-file prefix is matched against every file.
    """
    per_file: dict[str, set[str]] = {resw_file: set() for resw_file in RESW_FILES}
    loose: set[str] = set()
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [part.strip() for part in line.split(",")]
        if len(parts) >= 2 and parts[0] in RESW_FILES:
            if parts[1]:
                per_file[parts[0]].add(parts[1])
        elif len(parts) == 1:
            loose.add(parts[0])
    if loose:
        for resw_file in RESW_FILES:
            per_file[resw_file] |= loose
    return per_file


def read_rename_list(path: Path) -> list[tuple[str, str, str]]:
    """Read a ``File,Old,New`` list (or ``Old,New``) into [(file, old, new)]."""
    rows: list[tuple[str, str, str]] = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [part.strip() for part in line.split(",")]
        if len(parts) == 3 and parts[0] in RESW_FILES:
            rows.append((parts[0], parts[1], parts[2]))
        elif len(parts) == 2:
            for resw_file in RESW_FILES:
                rows.append((resw_file, parts[0], parts[1]))
    return rows


def _drop_entries(text: str, keys: set[str]) -> tuple[str, int, list[str]]:
    """Delete the ``<data>`` blocks for ``keys``.

    Also deletes a standalone ``<!-- ... -->`` comment that documents *only*
    deleted keys (the per-key comments in these files sit directly above their
    block); a section comment is kept unless every key it guards is gone.
    Returns (text, removed blocks, removed comment first lines).
    """
    lines = text.split("\n")

    def line_of(position: int) -> int:
        return text.count("\n", 0, position)

    blocks = [
        (line_of(match.start()), line_of(match.end()), match.group(1))
        for match in _DATA_RE.finditer(text)
    ]
    removed = {key for _, _, key in blocks} & keys
    if not removed:
        return text, 0, []

    inside: set[int] = set()
    for start, end, _ in blocks:
        inside.update(range(start, end + 1))

    comments = [
        (index, lines[index])
        for index in range(len(lines))
        if "<!--" in lines[index] and index not in inside
    ]

    drop: set[int] = set()
    for start, end, key in blocks:
        if key not in removed:
            continue
        # Safety: a <data> block never nests and never shares a line with other
        # markup, so it must own its lines completely before we delete them.
        if not lines[start].lstrip().startswith('<data name="') or not lines[end].rstrip().endswith("</data>"):
            raise ValueError(
                f"refusing to prune {key!r}: its block does not occupy whole lines "
                f"(L{start + 1}: {lines[start].strip()[:60]!r} .. L{end + 1}: {lines[end].strip()[:60]!r})"
            )
        drop.update(range(start, end + 1))

    for index, (comment_line, comment_text) in enumerate(comments):
        next_line = comments[index + 1][0] if index + 1 < len(comments) else len(lines)
        guarded = {key for start, _, key in blocks if comment_line < start < next_line}
        if not guarded or not guarded <= removed:
            continue
        cursor = comment_line
        while cursor < len(lines) and "-->" not in lines[cursor]:
            cursor += 1
        drop.update(range(comment_line, min(cursor, len(lines) - 1) + 1))

    kept = [line for index, line in enumerate(lines) if index not in drop]
    return "\n".join(kept), len(removed), sorted({lines[i] for i in drop if "<!--" in lines[i]})


def cmd_prune(args: argparse.Namespace) -> int:
    keys_by_file = read_key_list(Path(args.keys))
    planned = sum(len(v) for v in keys_by_file.values())
    if not planned:
        print("No keys listed.", file=sys.stderr)
        return 2

    print(f"Pruning {planned} key(s) from {', '.join(f for f in RESW_FILES if keys_by_file[f])}")
    total_blocks = 0
    comments: list[str] = []
    for locale in target_locales(args.locale):
        for resw_file in RESW_FILES:
            keys = keys_by_file[resw_file]
            if not keys:
                continue
            path = STRINGS_DIR / locale / f"{resw_file}.resw"
            if not path.exists():
                print(f"  {locale}/{resw_file}.resw: missing, skipped")
                continue
            text, uses_crlf, trailing = load_resw(path)
            new_text, blocks, dropped_comments = _drop_entries(text, keys)
            missing = sorted(keys - set(parse_entries(text)))
            if blocks == 0:
                print(f"  {locale}/{resw_file}.resw: no listed key present")
                continue
            if not args.dry_run:
                save_resw(path, new_text, uses_crlf, trailing)
            total_blocks += blocks
            comments.extend(dropped_comments)
            note = f"  ({len(missing)} not present: {', '.join(missing[:3])})" if missing else ""
            print(f"  {locale}/{resw_file}.resw: removed {blocks}{note}")

    if comments:
        print(f"Removed {len(comments)} documentation comment(s) that guarded only deleted keys.")
    print(
        f"{'Would remove' if args.dry_run else 'Removed'} {total_blocks} block(s) across "
        f"{len(target_locales(args.locale))} locale(s)."
        + ("" if args.dry_run else "  Re-run: python translations/pipeline.py validate")
    )
    return 0


def cmd_rename(args: argparse.Namespace) -> int:
    rows = read_rename_list(Path(args.map))
    if not rows:
        print("No rename rows found.", file=sys.stderr)
        return 2

    by_file: dict[str, list[tuple[str, str]]] = {}
    for resw_file, old, new in rows:
        by_file.setdefault(resw_file, []).append((old, new))

    total = 0
    for locale in target_locales(args.locale):
        for resw_file, pairs in by_file.items():
            path = STRINGS_DIR / locale / f"{resw_file}.resw"
            if not path.exists():
                print(f"  {locale}/{resw_file}.resw: missing, skipped")
                continue
            text, uses_crlf, trailing = load_resw(path)
            entries = parse_entries(text)
            done = 0
            for old, new in pairs:
                if old not in entries:
                    continue
                if new in entries:
                    print(f"  {locale}/{resw_file}.resw: {new} already exists, skipped", file=sys.stderr)
                    continue
                text = text.replace(f'<data name="{old}"', f'<data name="{new}"', 1)
                done += 1
            if done and not args.dry_run:
                save_resw(path, text, uses_crlf, trailing)
            total += done
            print(f"  {locale}/{resw_file}.resw: renamed {done}")
    action = "would rename" if args.dry_run else "renamed"
    print(f"{action.capitalize()} {total} entry(ies)."
          + ("  Re-run: python translations/pipeline.py validate" if not args.dry_run else ""))
    return 0


# --- CLI --------------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="pipeline.py",
        description="JASM in-repo translation pipeline.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__.split("Commands\n--------", 1)[1] if "Commands\n--------" in __doc__ else None,
    )
    sub = parser.add_subparsers(dest="command", required=True)

    export = sub.add_parser("export", help="build a template CSV from en-us")
    export.add_argument("--reference", default=DEFAULT_REFERENCE,
                        help=f"reference locale for tone/style (default: {DEFAULT_REFERENCE})")
    export.add_argument("--target", help="target locale; pre-fills existing translations")
    export.add_argument("--out", help="output CSV path")
    export.set_defaults(func=cmd_export)

    split = sub.add_parser("split", help="split a CSV into batches")
    split.add_argument("csv", help="CSV to split")
    split.add_argument("--size", type=int, default=15, help="rows per batch (default: 15)")
    split.add_argument("--name", help="batch filename prefix (default: CSV stem)")
    split.add_argument("--out", help="output directory (default: <csv dir>/batches)")
    split.set_defaults(func=cmd_split)

    apply = sub.add_parser("apply", help="write translations back into .resw files")
    apply.add_argument("path", help="CSV file or directory of CSVs")
    apply.add_argument("--locale", required=True, help="target locale code (e.g. pt-br)")
    apply.add_argument(
        "--no-seed",
        action="store_true",
        help="do not add untranslated new en-us keys (default: add them for parity)",
    )
    apply.set_defaults(func=cmd_apply)

    validate = sub.add_parser("validate", help="check parity, XML, and placeholders")
    validate.add_argument("--locale", help="limit to one locale")
    validate.add_argument(
        "--strict",
        action="store_true",
        help="treat placeholder mismatches as failures (default: warnings)",
    )
    validate.set_defaults(func=cmd_validate)

    stats = sub.add_parser("stats", help="show per-locale translation progress")
    stats.set_defaults(func=cmd_stats)

    orphans = sub.add_parser("orphans", help="list keys no XAML uid / x:Uid / C# literal reaches")
    orphans.add_argument("--locale", default=EN_LOCALE, help=f"locale to inspect (default: {EN_LOCALE})")
    orphans.add_argument("--emit", help="also write 'File,Key' CSV (feed it to prune)")
    orphans.set_defaults(func=cmd_orphans)

    missing = sub.add_parser("missing", help="list C# key lookups that have no key (silent English fallback)")
    missing.add_argument("--locale", default=EN_LOCALE, help=f"locale to inspect (default: {EN_LOCALE})")
    missing.set_defaults(func=cmd_missing)

    audit = sub.add_parser("audit", help="report every localization gap class (non-zero exit on hard gaps)")
    audit.add_argument("--locale", default=EN_LOCALE, help=f"source locale to inspect (default: {EN_LOCALE})")
    audit.add_argument("--details", action="store_true", help="print every offending row")
    audit.set_defaults(func=cmd_audit)

    prune = sub.add_parser("prune", help="delete keys from every locale's .resw files")
    prune.add_argument("keys", help="file listing keys to delete ('File,Key' per line, or bare keys)")
    prune.add_argument("--locale", help="limit to one locale (default: every locale, incl. en-us)")
    prune.add_argument("--dry-run", action="store_true", help="report what would be removed")
    prune.set_defaults(func=cmd_prune)

    rename = sub.add_parser("rename", help="rename keys in every locale, keeping their values")
    rename.add_argument("map", help="file listing renames ('File,Old,New' per line, or 'Old,New')")
    rename.add_argument("--locale", help="limit to one locale (default: every locale, incl. en-us)")
    rename.add_argument("--dry-run", action="store_true", help="report what would be renamed")
    rename.set_defaults(func=cmd_rename)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except BrokenPipeError:
        return 0
    except OSError as exc:
        # `python translations/pipeline.py orphans | head`: on Windows a closed stdout
        # pipe raises EINVAL rather than EPIPE. Narrow guard -- a stream failure carries
        # no filename, a file write failure does.
        if exc.errno == errno.EINVAL and exc.filename is None:
            return 0
        raise


if __name__ == "__main__":
    raise SystemExit(main())
