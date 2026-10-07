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
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
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
    en_blocks = {m.group(1): m.group(0) for m in _DATA_RE.finditer(en_text)}
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

        text, applied = _apply_values(text, translations)
        text, added = _add_missing(text, translations, en_text, seed_all=not args.no_seed)
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

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
