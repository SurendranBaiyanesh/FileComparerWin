using System.Xml;
using System.Xml.Linq;
using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Readers;

/// <summary>Reads XML where each record is an element and each column is an attribute or a leaf child element.</summary>
public sealed class XmlTableReader : ITableReader
{
    #region Properties

    public string FormatName => "XML";

    #endregion

    #region Public methods

    public bool CanRead(string path) =>
        string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase);

    public DataTable Read(string path, ComparisonOptions options)
    {
        XElement root = XDocument.Load(path, LoadOptions.SetLineInfo).Root
                   ?? throw new InvalidDataException($"'{path}' has no root element.");

        List<XElement> rowElements = FindRowElements(root);
        if (rowElements.Count == 0)
            throw new InvalidDataException($"No record elements found in '{path}'.");

        List<string> columns = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<(int LineNumber, Dictionary<string, string> Cells)> cellsPerRow = new List<(int LineNumber, Dictionary<string, string> Cells)>();

        foreach (XElement element in rowElements)
        {
            Dictionary<string, string> cells = ReadCells(element);
            foreach (string name in cells.Keys)
                if (seen.Add(name))
                    columns.Add(name);

            cellsPerRow.Add((((IXmlLineInfo)element).LineNumber, cells));
        }

        List<(int LineNumber, List<string>)> records = cellsPerRow
            .Select(r => (r.LineNumber, columns.Select(c => r.Cells.GetValueOrDefault(c, string.Empty)).ToList()))
            .ToList();

        return TableBuilder.Build(path, FormatName, columns, records);
    }

    /// <summary>Descends through single-element wrappers such as &lt;Root&gt;&lt;Rows&gt;… until it reaches the repeated records.</summary>
    #endregion

    #region Private methods

    private static List<XElement> FindRowElements(XElement root)
    {
        XElement current = root;
        while (true)
        {
            List<XElement> children = current.Elements().ToList();
            if (children.Count != 1 || !children[0].Elements().Any(c => c.HasElements || c.HasAttributes))
                return children;

            current = children[0];
        }
    }

    private static Dictionary<string, string> ReadCells(XElement element)
    {
        Dictionary<string, string> cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            cells[attribute.Name.LocalName] = attribute.Value;

        foreach (XElement child in element.Elements())
            cells[child.Name.LocalName] = child.HasElements ? child.ToString(SaveOptions.DisableFormatting) : child.Value;

        return cells;
    }

    #endregion
}
