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

Files can be typed, browsed for, or dropped. The whole **Files** panel takes a drop, not only the two
boxes, and it lights up while a file is over it:

| Dropped | Where it lands |
| --- | --- |
| One file on a box | That box. |
| One file elsewhere on the panel | The empty box, or Input once both are filled. |
| Two files at once, anywhere on the panel | Input takes the first, Output the second. Drop them one at a time to pair them the other way round. |

Folders are ignored, and so is anything that is not a file. Each file is read as soon as its path
settles, so the format, encoding and row count appear under it straight away — a mis-detected encoding
shows up next to the file name rather than inside an error message half a minute later.

Windows will not let one process hand data to a window running with more privilege than itself, so a
drop from Explorer is refused — silently, with no cursor and no message — by a window started from an
elevated Visual Studio or an administrator's prompt. The window asks for the exception the guard keeps
for this, so drops arrive either way, but running it without administrator rights is still the better
habit.

## A single executable

```bash
dotnet publish -p:PublishProfile=SingleFile
```

`bin\publish\FileComparerWindows.exe` is the whole application — the .NET runtime, WPF and the native
graphics libraries are inside it. Copy that one file to a machine with nothing installed on it,
double-click, and the window opens. It is a 64-bit build; Windows on ARM runs it under emulation.

The first launch unpacks the native libraries into `%TEMP%\.net\FileComparerWindows\`, which needs
neither an installer nor an administrator, and later launches reuse them.

Settings are read from and written to `appsettings.json` beside the executable. It is not published,
because there is nothing to say before the first run: the application starts on its defaults and
writes the file itself when settings are saved. Put one next to the executable to start somewhere
else.

The published file is compressed, trading about a second of start-up for less than half the size.
Turning `EnableCompressionInSingleFile` off in
[SingleFile.pubxml](Properties/PublishProfiles/SingleFile.pubxml) makes the opposite trade.

## The Excel report

**Report → Export as Excel workbook…** writes the whole run as six sheets:

| | Sheet | Holds |
| --- | --- | --- |
| 1 | Overview | Verdict, both files, the columns used, the counts, the options the run actually used, and any warnings. |
| 2 | Matching Values | Rows whose every compared value was equal outright. |
| 3 | Additional values in the output | Rows on the output side only, with all their columns. |
| 4 | Values missing from the output | Rows on the input side only, with all their columns. |
| 5 | Similar matches (±n) | Rows that agreed *only* because the range allowed it — one line per value, with how far apart the two were. |
| 6 | Difference values | One line per differing column, so sorting on Column shows whether one field is behind every failure. |

**Other column values** shows the rest of the record. Where the file named its columns, each value is
written against its name — `Age=31; Salary=3000` — and where it did not, because the columns were cut
by position and numbered `column1`, `column2`, …, the names are left out and only the values are
written: `EI;0;VPA`. A name the file never had is not worth the room.

Sheets 2, 5 and 6 divide the paired rows between them and never overlap: a row with anything genuinely
wrong is a difference, whatever else it also has. Values written differently but meaning the same
number — `123.00` and `123` — are equal outright and belong to sheet 2, not sheet 5; only values that
needed the range appear there.

Every row is written.

Values are written as text, so `007` stays `007` and `1-2` does not become a date. Only the counts on
the overview are numbers. The workbook is built straight into the Open XML package, the same way the
`.xlsx` reader takes one apart, so no spreadsheet library is needed.

## The window

| | |
| --- | --- |
| **Files** | Input and output paths, with what was read from each, and a line in red when the two files do not carry the same columns. |
| **Columns** | Key and Compare. **Pick…** ticks names off the headers the files actually have, so a column called `Name des Versicherten/Begünstigten` need not be typed. |
| **Options** | Ignore case, trim values, similar match, ± range, delimiter, encoding. |
| **Verdict** | SUCCESS or FAILED, the columns the run used, and the six counts. |
| **Value differences** | One row per differing column, with the key, both values and both line numbers. Select a row to see the two source rows in full. |
| **Missing / Extra** | Rows present on one side only. |
| **Warnings** | Duplicate keys, and options that could not take effect. |
| **Report** | The console tool's report as text — copy it, or export it. |

Grids sort by any column: sorting the differences by *Column* answers "is one field behind every
failure?" at a glance. `Ctrl+C` copies the selected rows with their headers.

Every column also has a filter box under its title. Text is matched anywhere in the value and without
regard to case, and filters narrow one another — a column name in *Column* and an amount in *Output
value* shows the rows that are both. The text is used exactly as typed rather than trimmed, since a
value with a space on the end of it is precisely the sort of thing worth searching for here.

While a filter is narrowing a grid, a strip under it says how much is getting through and offers to
clear them. The counts above the grid, and the number on the tab, deliberately do not move: they
report the comparison, not the current view of it.

## Options

Every option the console tool has, under the name the settings file uses.

| Window | appsettings.json | Meaning |
| --- | --- | --- |
| Input / Output | `InputFilePath` / `OutputFilePath` | The two files. |
| Key | `KeyColumns` | Key column(s), e.g. `PersonNumber` or `Name,PersonNumber`. |
| Compare | `CompareColumns` | Columns compared once rows are paired. Default: every column the two files share. |
| Ignore case | `IgnoreCase` | Compare values case-insensitively. |
| Trim values | `TrimValues` | Trim values before comparing. Default `true`. |
| Similar match | `SimilarMatch` | Compare numbers by value: `123.00` equals `123`. |
| ± range | `SimilarMatchRange` | How far apart two numbers may be and still count as equal. `0` = exactly equal. |
| Delimiter | `Delimiter` | Delimiter for text files. `detect` reads it from the header. |
| Split at | `SplitIndexes` | Positions to cut fixed-width lines at, e.g. `1;2;5;13`. Empty reads by delimiter. |
| No header row | `NoHeaderRow` | Read the first line or row of both files as a record; name columns `column1`, `column2`, … |
| Encoding | `Encoding` | Encoding of the text files. `detect` by default. |

**File > Save settings** writes them back under the same `FileComparer` section the console tool
reads, so one settings file serves both.

## Command line

Anything on the command line fills the window in before it opens, so a shortcut or a batch file can
point the application straight at a pair of files. `--run` compares immediately.

```bash
FileComparerWindows -i input.csv -o output.csv -c PersonNumber --run
```

The switches: `-i`, `-o`, `-c`, `--compare-columns`, `--ignore-case`, `--trim`, `--similar-match`,
`--similar-range`, `-d`, `-e`, `--config`, `--run`. **Help > Command line** lists them. The console
tool still has `-x`, `-s`, `--max-rows` and `--sheet`; this window no longer does.

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
padded code must stay distinct, leave the option off or leave that column out of Compare. And because it applies
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

## Columns

The two files have to carry the same columns. A column on one side only stops the comparison with an
error naming it, rather than being left out of the run: there is nothing to compare it against, and a
comparison that quietly ignored it would report that every row matches while the output is missing a
field.

Order and casing do not count, since columns are matched by name — the error is about which names are
there, not the shape of the header. The window says so as soon as both files have been read, on a red
line under the file boxes with both headers on its tooltip, rather than waiting for Compare to be
pressed.

The commonest reason for a column to be on one side only is the encoding: a Windows-1252 header read
as UTF-8 turns `Begünstigten` into `Beg�nstigten`, which is a different name. The error says so when
it finds a replacement character in a header, and [Encoding](#encoding) is the setting that fixes it.

## Splitting lines that have no delimiter

Some exports have no separator at all — every field sits at a fixed position:

```
Row 1: EI0VPA20260131000102991000000000050  004260212049166977000000000000081285Aeschbacher…
Row 2: …
```

Set **Delimiter** to `Dynamic` — the entry that means *there is no delimiter, cut at fixed positions*.
That is what makes **Split at** live; it stays greyed out under any other delimiter, since positions
mean nothing to a file being cut on a separator. Then fill in the positions, semicolon or comma
separated:

```
1;2;5;13;22;34;36;39;43;51;55;73;103;114;116;119;122;131;132;140
```

**Each number is the position of the last character of its column, counting the first character of the
line as 0.** So `1;2;5` takes two characters, then one, then three: `EI`, `0`, `VPA`. One column comes
out per position given, and **both files are cut the same way** — every line of each, not just the first.

Two things follow from a file laid out this way. There is no header line to read names from, so the
columns are named `column1`, `column2` and so on, and every line — including the first — is a record.
And the delimiter has nothing to cut on, so that box greys out while positions are filled in.

### Marking the cuts up instead of counting them

Counting characters to write `1;2;5;13;22;34…` by hand is miserable and easy to get wrong by one.
**Pick Split Index…** beside the box does it the other way round: it shows the first row of the file
and lets the cuts be typed onto it.

```
EI|0|VPA|20260131|000102991|
```

Every separator ends the column to its left. The separators are counted, turned into positions and
thrown away — they never reach the data. Underneath, a grid shows the row cut at exactly those
positions, by the same code the reader will use, so the preview cannot disagree with the result.

Choose a separator the data does not contain — the dialog says so if it does, since a `0` used as a
separator in a row full of zeros would cut it to pieces. **Start again** puts the row back as read, and
opening the dialog on positions already in the box shows them marked, so a list can be adjusted rather
than restarted.

A position past the end of the row is marked at the end of it, which is the same cut: a row of 140
characters cut at 140 and at 139 gives the same last column.

Press **Convert** to cut both files there and then. Until a file has been read nobody — the window
included — knows what its columns are called, so Convert is what brings `column1`, `column2` and the
rest into existence: it fills the **Compare** box with them and offers them to both **Pick…** dialogs.

**Key** is left empty on purpose. Which column pairs the rows is a decision about the data, and filling
it with every column would pair on the whole record and report every difference as a row missing from
one side. Name one — `column5`, say — and press **Compare**.

Values keep their padding, so leave **Trim values** on unless the spaces are meant to count. A line
shorter than the positions expect is not an error — the columns it does not reach come back empty.
`Dynamic` with no positions filled in is refused with a message rather than read as a separator.

### When neither file has a header

A `.xlsx` normally gives its column names away in row 1, and a delimited file its first line. A file
that begins at its first record has none to give, and read the usual way it loses that record and names
the columns after its contents — so the two sides never agree on what to call anything and the run
stops on mismatched columns.

Tick **No header row** and both files are read as records throughout, both naming their columns
`column1`, `column2` and so on. That is what lets a fixed-width text file be compared against a
headerless spreadsheet:

| | Read normally | With **No header row** |
| --- | --- | --- |
| Columns | `EI`, `0`, `VPA`, … | `column1`, `column2`, … |
| Rows | first record lost to the header | every row a record |

A file being split by position is read this way whatever the box says — it has no header by
definition. The box is what brings the *other* file into line with it.

## Leaving columns out

Name the columns to compare and everything else stops counting — the export timestamp or running
number that differs on every row and means nothing simply goes unnamed. Rows are still paired on the
key columns and still counted as missing or extra; only value differences in the unnamed columns stop
mattering.

## Formats

| Format | Extensions | Notes |
| --- | --- | --- |
| Delimited text | `.csv` `.txt` `.tsv` `.psv` `.dat` | Delimiter auto-detected from `; , \t \|`. RFC 4180 quoting. |
| XML | `.xml` | Each record is an element; columns are its attributes and leaf child elements. |
| JSON | `.json` | An array of objects, or an object whose first array property holds the records. |
| Excel | `.xlsx` `.xlsm` | Read straight from the Open XML package — no third-party library. First row is the header. |

The two files do not have to be in the same format: comparing a `.txt` against an `.xlsx` works, as
long as both carry the same columns.

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
