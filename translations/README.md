# translations/ -- JASM localization pipeline

Everything needed to add or update a language lives here. **No temp files, no
per-language scripts.**

```
translations/
  pipeline.py     <- the whole pipeline (export / split / apply / validate / stats)
  template.csv    <- generated master template (English + es reference, blank Translation)
  batches/        <- working space for per-language CSVs (gitignored)
```

Strings live in `src/GIMI-ModManager.WinUI/Strings/<locale>/` as three files per
locale: `Resources.resw`, `Settings.resw`, `Startup.resw`. English (`en-us`) is
the source of truth; every locale must stay **key-identical** to it.

## Requirements
- Python 3.11+ (no third-party packages).

## CSV format
`File, Key, English, Context Comment, Reference (<locale>), Translation`

| Column | Meaning |
|--------|---------|
| `File` | `Resources`, `Settings`, or `Startup` |
| `Key` | resw `data name` |
| `English` | source text (do not edit) |
| `Context Comment` | the `<!-- ... -->` comment from `en-us` explaining where the string appears |
| `Reference (<locale>)` | translation in the reference language (default `es`), for tone/style |
| `Translation` | **fill this in** |

The CSV is UTF-8 with BOM so Excel opens it correctly.

## Add a new language (example: `fr`)

```bash
# 1. Generate a template with an empty Translation column.
python translations/pipeline.py export --target fr

# 2. (optional but recommended) split it so translators get small files.
python translations/pipeline.py split translations/batches/fr/template.csv --size 15

# 3. Translate the "Translation" column in each batch.
#    Rules:
#      - keep {0}, {1}, {{TargetPath}}, ... placeholders exactly as-is
#      - keep URLs, file paths, and brand names (GameBanana, JASM) unchanged
#      - use the Reference column for tone, not literal wording

# 4. Write the translations back. If the locale is new, its .resw files are
#    created with the full English structure so parity is guaranteed.
python translations/pipeline.py apply translations/batches/fr --locale fr

# 5. Verify.
python translations/pipeline.py validate --locale fr
```

Then register the locale in the app's language list (see `Settings`/language
selector code) and build.

## Update strings for an existing language

```bash
python translations/pipeline.py export --target pt-br   # pre-fills current pt-br values
# edit translations/batches/pt-br/template.csv
python translations/pipeline.py apply translations/batches/pt-br/template.csv --locale pt-br
python translations/pipeline.py validate --locale pt-br
```

New `en-us` keys are exported with an empty `Translation`. `apply` seeds any
still-missing keys from English so a locale never loses key parity
(`--no-seed` opts out if you truly want a partial file).

## Commands

| Command | Purpose |
|---------|---------|
| `export [--reference es] [--target LOCALE] [--out FILE]` | Build a template CSV from `en-us` |
| `split CSV [--size 15] [--name PREFIX] [--out DIR]` | Chop a CSV into batch files |
| `apply PATH --locale LOCALE [--no-seed]` | Write `Translation` values into the locale's `.resw` files |
| `validate [--locale LOCALE] [--strict]` | Check key parity, XML well-formedness, placeholders |
| `stats` | Per-locale translation coverage |
| `orphans [--emit FILE]` | List keys no XAML uid / `x:Uid` / C# literal can reach |
| `prune KEYS_FILE` | Delete keys (`File,Key` per line) from **every** locale |
| `rename MAP_FILE` | Rename keys (`File,Old,New` per line) in every locale, keeping values |

## Orphan (dead) keys

`orphans` answers the opposite question to `validate`: which en-us keys can no
code path ever reach? A key is **reachable** when

1. an XAML element declares `l:Uids.Uid="X"` (or `"/<File>/X"`), so the localizer
   applies every key whose *uid part* (the resw name up to the first `.`) is `X`
   in that file's resw — including keyed elements that carry **no** literal,
   because their text is set at runtime by the uid;
2. an element declares the framework `x:Uid="X"`;
3. the C# source contains the literal `"Key"` or `"/Settings/Key"` (the
   `/Settings/` form is what resolves a key stored in `Settings.resw`);
4. the key is an **enum member name** — `SettingsViewModel` localizes
   `ModUpdateAvailableChecker.Status.ToString()` directly, so `Running`,
   `Waiting`, `Stopped`, `Error` are live without ever being spelled out.

Everything else is an orphan: leftovers from removed features, a key renamed
under a new spelling (its live counterpart is looked up under the bare name, e.g.
a leftover `Foo_Bar.Label` where the code asks for `Foo_Bar` — rename it, see
`missing`), strings that moved to another resw file, and near-miss uid names (the
element's `l:Uids.Uid` differs from the key's base by a suffix).

Keys built at runtime from a literal prefix (`"Category_" + category`) are **not**
orphans: the prefix is reported by `missing`, and any key starting with it is kept.

```bash
python translations/pipeline.py orphans                 # report
python translations/pipeline.py orphans --emit translations/batches/orphans.csv
python translations/pipeline.py prune translations/batches/orphans.csv --dry-run
python translations/pipeline.py prune translations/batches/orphans.csv
python translations/pipeline.py validate --strict
```

`prune` deletes the `<data>` block and the `<!-- ... -->` comment that guarded
only that key, in every locale including `en-us` (a key left in one locale makes
`validate` fail on "extra key"). `rename` moves a key *with* each locale's own
value — do not "rename" by pruning and re-applying, `apply` would seed the new
key from English and drop the translation.

Deliberate exceptions (a dormant feature whose strings we keep wired) belong
outside the orphan list; the check is a report, not a gate.


`apply` accepts either a single CSV or a directory of CSVs.

## Key naming rules (unchanged, important)

The app resolves keys two ways:

- **C#** `localizer.GetLocalizedStringOrDefault("Foo_Content")` -> the resw key
  must use **underscores**: `Foo_Content`.
- **XAML** `l:Uids.Uid="Foo"` -> the key must use **dot + property**:
  `Foo.Content`.

A dotted key silently fails C# lookup, and an underscore key silently fails the
XAML lookup. Keep both forms when a string is used from both places.

## Validation notes

`validate` fails on missing/extra keys, invalid XML, or a missing/duplicated
`</root>`. Placeholder mismatches are reported as `WARN` and only fail with
`--strict` (there are known pre-existing mismatches; see the tracker).
