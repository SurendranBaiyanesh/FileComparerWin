using System.Globalization;

namespace FileComparerWindows.Configuration;

/// <summary>
/// Applies command-line switches on top of the options loaded from appsettings.json. The window opens
/// with the fields already filled in, so a shortcut or a batch file can hand the application a pair of
/// files to look at.
/// </summary>
public static class CommandLine
{
    public static string GetConfigPath(string[] args, string defaultPath)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (Matches(args[i], "--config"))
                return args[i + 1];

        return defaultPath;
    }

    /// <summary>True when the arguments ask for the comparison to run as soon as the window opens.</summary>
    public static bool IsAutoRunRequested(string[] args) =>
        args.Any(a => Matches(a, "--run", "--compare"));

    public static void ApplyTo(ComparisonOptions options, string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            if (Matches(arg, "--input", "-i")) options.InputFilePath = Next() ?? options.InputFilePath;
            else if (Matches(arg, "--output", "-o")) options.OutputFilePath = Next() ?? options.OutputFilePath;
            else if (Matches(arg, "--columns", "-c")) options.KeyColumns = SplitList(Next());
            else if (Matches(arg, "--compare-columns")) options.CompareColumns = SplitList(Next());
            else if (Matches(arg, "--show-non-matching", "-s")) options.ShowNonMatchingRows = ParseBool(Next());
            else if (Matches(arg, "--max-rows")) options.MaxNonMatchingRowsToShow = ParseInt(Next(), options.MaxNonMatchingRowsToShow);
            else if (Matches(arg, "--ignore-case")) options.IgnoreCase = ParseBool(Next());
            else if (Matches(arg, "--trim")) options.TrimValues = ParseBool(Next());
            else if (Matches(arg, "--similar-match")) options.SimilarMatch = ParseBool(Next());
            else if (Matches(arg, "--similar-range", "--range")) options.SimilarMatchRange = ParseDecimal(Next(), options.SimilarMatchRange);
            else if (Matches(arg, "--delimiter", "-d")) options.Delimiter = Next() ?? options.Delimiter;
            else if (Matches(arg, "--sheet")) options.SheetName = Next() ?? options.SheetName;
            else if (Matches(arg, "--encoding", "-e")) options.Encoding = Next() ?? options.Encoding;
            else if (Matches(arg, "--config")) Next();
            else if (Matches(arg, "--run", "--compare")) { }
            else if (arg.StartsWith('-')) throw new ArgumentException($"Unknown option '{arg}'. See Help > Command line for the supported options.");
        }
    }

    private static bool Matches(string arg, params string[] names) =>
        names.Any(n => string.Equals(arg, n, StringComparison.OrdinalIgnoreCase));

    private static List<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>A bare flag (no value following) counts as "true".</summary>
    private static bool ParseBool(string? value) =>
        value is null || value.StartsWith('-') || !bool.TryParse(value, out bool parsed) || parsed;

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out int parsed) ? parsed : fallback;

    /// <summary>Invariant culture, so that --similar-range 0.5 means a half wherever the machine is set up.</summary>
    private static decimal ParseDecimal(string? value, decimal fallback) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : fallback;

    public static string HelpText =>
        """
        FileComparerWindows - compares two data files row by row using one or more key columns.

        The window opens with whatever the arguments and appsettings.json supply, so a shortcut can
        point the application straight at a pair of files.

        Usage:
          FileComparerWindows [options]

        Options:
          -i, --input <path>            Input file path.
          -o, --output <path>           Output file path.
          -c, --columns <list>          Key column(s), comma separated. e.g. PersonNumber
                                        or "Name,PersonNumber".
              --compare-columns <list>  Columns to compare once rows are paired.
                                        Default: every column the two files share.
          -s, --show-non-matching <b>   Show the non-matching rows (true/false).
              --max-rows <n>            Max rows listed per category. 0 = all.
              --ignore-case <bool>      Compare values case-insensitively.
              --trim <bool>             Trim values before comparing. Default true.
              --similar-match <bool>    Compare numbers by value, so 123.00 equals 123
                                        and 110.60 equals 110.6.
              --similar-range <n>       How far apart two numbers may be and still count
                                        as equal. 0 (default) requires them to be exactly
                                        equal; 1 lets 100 match 99 to 101, 2 lets it match
                                        98 to 102. Needs --similar-match.
          -d, --delimiter <text>        Delimiter for text files, of any length. Default:
                                        auto-detect, which tries ; , tab and |. A delimiter
                                        holding a quote, such as |", turns quoting off: the
                                        line is cut on the delimiter and read as written.
          -e, --encoding <name>         Encoding of the text files: utf-8, utf-16, utf-16be,
                                        utf-32, ascii, latin1 or windows-1252.
                                        Default: detect (BOM, else UTF-8, else Windows-1252).
              --sheet <name>            Worksheet name for .xlsx files. Default: first sheet.
              --config <path>           Settings file. Default: appsettings.json.
              --run                     Compare as soon as the window opens.

        Supported formats: .csv .txt .tsv .psv (delimited), .xml, .json, .xlsx

        Exit code, once the window is closed: 0 = files match, 1 = differences found, 2 = error.
        """;
}
