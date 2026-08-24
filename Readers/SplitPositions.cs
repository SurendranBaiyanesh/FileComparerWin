namespace FileComparerWindows.Readers;

/// <summary>
/// The arithmetic behind cutting a fixed-width line into columns. A position is the position of the
/// last character of its column, counting the first character of the line as 0, so 1;2;5 takes two
/// characters, then one, then three.
///
/// Kept apart from the reader because the window works in the same positions: the Pick Split Index
/// dialog turns separators typed into a line back into positions, and shows what they would cut, and
/// both have to agree with what the reader will do or the preview is a lie.
/// </summary>
public static class SplitPositions
{
    #region Constants
    /// <summary>What the dialog offers to mark cuts with, when the data is unlikely to contain it.</summary>
    public const char DefaultMarker = '|';
    #endregion

    #region Public methods
    /// <summary>
    /// The positions written as a list, in ascending order and without repeats. Separated by ; or , so
    /// that a list can be pasted from wherever it was written down, and anything that is not a position
    /// of its own is left out rather than read as 0, which would silently produce an empty first column.
    /// </summary>
    public static int[] Parse(string configured) =>
        [.. configured
            .Split([';', ',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, System.Globalization.NumberStyles.Integer,
                                         System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : -1)
            .Where(value => value >= 0)
            .Distinct()
            .Order()];

    public static string Describe(IEnumerable<int> positions) => string.Join(";", positions);

    /// <summary>
    /// The positions marked out by a separator typed into a line. Each separator ends the column to its
    /// left, so the position is the count of real characters before it, less one. A separator at the
    /// very front is ignored: it would mark a column of no characters at all.
    /// </summary>
    public static int[] FromMarkedLine(string marked, char marker)
    {
        List<int> positions = new List<int>();
        int seen = 0;

        foreach (char c in marked)
        {
            if (c != marker)
                seen++;
            else if (seen > 0)
                positions.Add(seen - 1);
        }

        return [.. positions.Distinct().Order()];
    }

    /// <summary>The line as it was before the separators were typed into it.</summary>
    public static string Strip(string marked, char marker) => marked.Replace(marker.ToString(), string.Empty);

    /// <summary>
    /// The other direction: puts the separators back into a line at positions already settled on, so
    /// that a list can be opened and adjusted rather than started from nothing.
    ///
    /// A position past the end of the line cannot be marked where it says - there is no character
    /// there to mark after - so it is marked at the end instead. The column it describes is the same
    /// either way, because <see cref="Split"/> clamps to the length of the line; without this the
    /// column would be dropped the moment the dialog was opened and confirmed.
    /// </summary>
    public static string Mark(string line, int[] positions, char marker)
    {
        if (positions.Length == 0)
            return line;

        System.Text.StringBuilder marked = new System.Text.StringBuilder(line.Length + positions.Length);
        HashSet<int> cuts = [.. positions];

        for (int i = 0; i < line.Length; i++)
        {
            marked.Append(line[i]);
            if (cuts.Contains(i))
                marked.Append(marker);
        }

        if (positions.Any(p => p >= line.Length) && (marked.Length == 0 || marked[^1] != marker))
            marked.Append(marker);

        return marked.ToString();
    }

    /// <summary>
    /// Cuts a line at the given positions. A line shorter than the positions expect is not an error:
    /// the columns it does not reach come back empty, which is what a short record in a fixed-width
    /// file means.
    /// </summary>
    public static List<string> Split(string line, int[] positions)
    {
        List<string> values = new List<string>(positions.Length);
        int start = 0;

        foreach (int position in positions)
        {
            int last = Math.Min(position, line.Length - 1);
            values.Add(start <= last ? line.Substring(start, last - start + 1) : string.Empty);
            start = position + 1;
        }

        return values;
    }
    #endregion
}
