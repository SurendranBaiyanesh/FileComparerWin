# FileComparerWindows

Windows desktop application that compares an input file and an output file row by row, using one or
more columns as the key. Row order does not matter — rows are paired by their key values and then
every other shared column is compared.

It is the [FileComparer](../FileComparer) console tool with a window in front of it: the same reader,
comparison and reporting code, the same `appsettings.json`, and the same exit codes.

## Running it

```bash
dotnet run --project FileComparerWindows.csproj
```

Or build once and launch the executable:

```bash
dotnet build -c Release
```

The window opens with whatever `appsettings.json` and the command line supply. Choose two files, name
the key column(s), press **Compare**.

Files can be typed, browsed for, or dropped onto either box. Each file is read as soon as its path
settles, so the format, encoding and row count appear under it straight away — a mis-detected encoding
shows up next to the file name rather than inside an error message half a minute later.

## The window

| | |
| --- | --- |
| **Files** | Input and output paths, with what was read from each. |
| **Columns** | Key, Compare and Skip. **Pick…** ticks names off the headers the files actually have, so a column called `Name des Versicherten/Begünstigten` need not be typed. |
| **Options** | Ignore case, trim values, similar match, list non-matching rows, max rows, delimiter, encoding, worksheet. |
| **Verdict** | SUCCESS or FAILED, the columns the run used, and the six counts. |
| **Value differences** | One row per differing column, with the key, both values and both line numbers. Select a row to see the two source rows in full. |
| **Missing / Extra** | Rows present on one side only. |
| **Warnings** | Columns present in only one file, skipped names that changed nothing, duplicate keys. |
| **Report** | The console tool's report as text — copy it, or export it. |

Grids sort by any column: sorting the differences by *Column* answers "is one field behind every
failure?" at a glance. `Ctrl+C` copies the selected rows with their headers.

## Options

Every option the console tool has, under the name the settings file uses.

| Window | appsettings.json | Meaning |
| --- | --- | --- |
| Input / Output | `InputFilePath` / `OutputFilePath` | The two files. |
| Key | `KeyColumns` | Key column(s), e.g. `PersonNumber` or `Name,PersonNumber`. |
| Compare | `CompareColumns` | Columns compared once rows are paired. Default: every column the two files share. |
| Skip | `SkipColumns` | Column(s) left out of the comparison. |
| List non-matching rows | `ShowNonMatchingRows` | List the non-matching rows. |
| Max rows | `MaxNonMatchingRowsToShow` | Cap on rows listed per category. `0` = all. |
| Ignore case | `IgnoreCase` | Compare values case-insensitively. |
| Trim values | `TrimValues` | Trim values before comparing. Default `true`. |
| Similar match | `SimilarMatch` | Compare numbers by value: `123.00` equals `123`. |
| ± range | `SimilarMatchRange` | How far apart two numbers may be and still count as equal. `0` = exactly equal. |
| Delimiter | `Delimiter` | Delimiter for text files. `detect` reads it from the header. |
| Encoding | `Encoding` | Encoding of the text files. `detect` by default. |
| Sheet | `SheetName` | Worksheet for `.xlsx` files. Default: the first sheet. |

**File > Save settings** writes them back under the same `FileComparer` section the console tool
reads, so one settings file serves both.

## Command line

Anything on the command line fills the window in before it opens, so a shortcut or a batch file can
point the application straight at a pair of files. `--run` compares immediately.

```bash
FileComparerWindows -i input.csv -o output.csv -c PersonNumber --run
```

The switches are the console tool's: `-i`, `-o`, `-c`, `--compare-columns`, `-x`, `-s`, `--max-rows`,
`--ignore-case`, `--trim`, `--similar-match`, `--similar-range`, `-d`, `-e`, `--sheet`, `--config`.
**Help > Command line** lists them.

Exit codes, once the window is closed: `0` files match, `1` differences found, `2` error. A script can
therefore launch the window and still learn the outcome.

## SimilarMatch

Off by default, values are compared exactly as they are written, so an amount re-saved by another
program counts as a difference:

```
Betrag Auszahlung/Inkasso: input='57.30'  output='57.3'
```

Turn **Similar match** on and anything that is plainly a number is compared by value instead:

| Input | Output | SimilarMatch off | on |
| --- | --- | --- | --- |
| `123.00` | `123` | differs | equal |
| `110.60` | `110.6` | differs | equal |
| `0.50` | `.5` | differs | equal |
| `-0.00` | `0` | differs | equal |
| `123.00` | `123.01` | differs | differs |
| `ABC` | `abc` | differs | differs |

Numbers are read at full `decimal` precision, so no rounding is introduced: `0.1` and
`0.10000000000000000000000001` stay different.

What counts as a number is deliberately narrow — an optional sign, digits, a single `.`, and nothing
else. Digit grouping is **not** accepted, because `110,60` is one hundred and ten in half the world
and eleven thousand in the other half; anything ambiguous is compared as the text it is, rather than
guessed at.

Two consequences worth knowing. Leading zeros stop counting, so `007` and `7` become equal — if a
padded code must stay distinct, leave the option off or skip that column. And because it applies
wherever values are matched, it also affects **which rows pair up**: a key column holding `1` in one
file and `01` in the other will pair under SimilarMatch where it previously did not.

## Range

Similar match settles how a number is *written*; the range settles how close two numbers have to be.
Set **± range** to 1 and a value of `100.00` matches anything from `99` to `101`; set it to 2 and it
matches `98` to `102`. Both ends count, so at a range of 1 exactly `99` and exactly `101` match while
`98.99` and `101.01` do not.

| Range | `100.00` matches | does not match |
| --- | --- | --- |
| `0` (default) | `100`, `100.0`, `100.000` | `99.99`, `100.01` |
| `1` | `99` … `101` | `98.99`, `101.01` |
| `2` | `98` … `102` | `97.99`, `102.01` |
| `0.5` | `99.5` … `100.5` | `99.49`, `100.51` |

The range is a decimal, so `0.5` and `0.005` are as valid as `1`. A negative range is an error rather
than a comparison that quietly does something else — a range is a distance.

Three things to know about when it applies:

- **Only to values that are numbers on both sides.** A range is a distance, and there is no distance
  between a number and a word, so `BSC` against `MSC` stays the text comparison it always was, however
  large the range.
- **Only while Similar match is on**, since that is what makes a value a number rather than the text
  it is written as. A range set without it is reported in Warnings rather than passing silently — a run
  that found no differences would otherwise look like agreement it had never tested for.
- **Not to row pairing.** Similar match does affect which rows pair up; the range deliberately does
  not. "Within 1 of each other" is not an equivalence relation — `100` matches `101` and `101` matches
  `102`, but `100` and `102` do not — so there is no such thing as the group a row belongs to. Rows
  therefore pair on keys that are equal, and the range applies afterwards, to the values being
  compared. A key column of `100` will not pair with `101` at any range.

`samples/output-range.txt` is `samples/input.txt` with two ages moved by one, so it fails at range `0`
and passes at range `1`:

```bash
FileComparerWindows -i samples/input.txt -o samples/output-range.txt -c PersonNumber --similar-match true --similar-range 1 --run
```

## Skipping columns

Name a column and it stops counting towards the result — useful for the export timestamp or running
number that differs on every row and means nothing. Several at once, comma separated, and matched
without regard to case.

Skipping subtracts from whatever was going to be compared, so it also narrows an explicit Compare
list. Rows are still paired on the key columns and still counted as missing or extra; only value
differences in the skipped columns stop mattering. The Warnings tab says when a skipped name changed
nothing — an unknown column, or a key column, which pairs rows and is never among the compared ones.
Skipping every comparable column is an error rather than a comparison that trivially succeeds.

## Formats

| Format | Extensions | Notes |
| --- | --- | --- |
| Delimited text | `.csv` `.txt` `.tsv` `.psv` `.dat` | Delimiter auto-detected from `; , \t \|`. RFC 4180 quoting. |
| XML | `.xml` | Each record is an element; columns are its attributes and leaf child elements. |
| JSON | `.json` | An array of objects, or an object whose first array property holds the records. |
| Excel | `.xlsx` `.xlsm` | Read straight from the Open XML package — no third-party library. First row is the header. |

The two files do not have to be in the same format: comparing a `.txt` against an `.xlsx` works, as
long as the key columns exist on both sides.

Columns are matched by name, not position, so files with columns in a different order compare fine.
A header ending in a trailing separator (`PersonNumber;Name;LastName;Age;Education;`) is handled —
the empty trailing column is dropped.

Dates in `.xlsx` files are read as their underlying serial number, since cell number formats are not
interpreted. Two spreadsheets still compare correctly against each other; a spreadsheet compared
against a text file needs the dates written as text.

## Encoding

Text files rarely say what encoding they are in, and guessing wrong is silent: read a Windows-1252
export as UTF-8 and the `ü` in `Begünstigten` turns into a replacement character, so a key column of
that name is never found. Detection therefore goes: byte order mark if there is one, otherwise UTF-8
if the bytes are valid UTF-8, otherwise Windows-1252. The encoding used is shown under each file, and
the Encoding option forces one (`utf-8`, `utf-16`, `utf-16be`, `utf-32`, `ascii`, `latin1`,
`windows-1252`) for the rare file that is accidentally valid UTF-8 while meaning something else.
Changing it re-reads both files at once.

The two files need not share an encoding — a Windows-1252 input compares fine against a UTF-8 output.

Text is also normalised (NFC) before anything is matched. An accented letter can be stored either as
one character (`ü` = U+00FC) or as the plain letter plus a combining accent (`u` + U+0308); the two
are identical on screen and would otherwise never match. This applies to column names and to the
values being compared.

## Adding another format

Implement `ITableReader` (`CanRead` + `Read`) and register it in `TableReaderFactory`. Use
`TableBuilder.Build` to produce the `DataTable` so the header and ragged-row rules stay consistent
with the other readers.

## Layout

```
App.xaml                       application resources: palette, control styles
MainWindow.xaml                the window; its code-behind is dialogs and drag-and-drop only
ColumnPickerWindow.xaml        tick column names off the files' headers
TextWindow.xaml                a block of text worth reading in full
ViewModels/MainViewModel.cs    everything the window does
ViewModels/LoadedFile.cs       one side of the comparison: path, table, what was read
Infrastructure/                change notification and ICommand
Configuration/                 appsettings.json binding and command-line parsing
Model/DataTable.cs             format-independent table of string values
Readers/                       one reader per format + the shared TableBuilder
Comparison/                    key-based row pairing and value comparison
Reporting/TextReport.cs        the console tool's report, as text
samples/                       sample input/output files for the success and fail cases
```

Note the project restores `System.IO` to the implicit usings and drops `System.Windows.Shapes`: a WPF
project leaves `System.IO` out so that `System.Windows.Shapes.Path` does not collide with
`System.IO.Path`, and this application reads files far more often than it draws shapes.
