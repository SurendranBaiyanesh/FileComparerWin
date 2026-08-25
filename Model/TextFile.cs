using System.IO;
using System.Text;

namespace FileComparerWindows.Model;

/// <summary>A file's contents together with the encoding they turned out to be written in.</summary>
public sealed record TextContent(string Text, string EncodingName)
{
	public string[] Lines()
	{
		return this.Text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
	}
}

/// <summary>
/// Reads text files that do not say what encoding they are in. A byte order mark wins; otherwise the
/// bytes are tried as UTF-8 and, when that fails, read as Windows-1252 - the encoding behind most
/// exports from older Windows tools, where an u-umlaut is the single byte 0xFC rather than the UTF-8
/// pair 0xC3 0xBC. Guessing UTF-8 for such a file replaces every accented character with U+FFFD, and
/// a column whose name contains one is then never found.
/// </summary>
public static class TextFile
{
	#region Fields
	private static readonly UTF8Encoding StrictUtf8 = new(false, true);
	private static readonly Encoding BigEndianUtf32 = new UTF32Encoding(true, true);

	/// <summary>
	/// Windows-1252 is Latin-1 except across 0x80-0x9F, which hold the euro sign and the typographic
	/// quotes and dashes. One entry per byte from 0x80 to 0x9F, the five unassigned slots included.
	/// Code points rather than the characters themselves, so that re-saving this file in another
	/// encoding cannot quietly corrupt the very table that repairs mis-encoded input.
	/// </summary>
	private static readonly char[] Windows1252Upper =
	[
		(char) 0x20AC, (char) 0x0081, (char) 0x201A, (char) 0x0192, (char) 0x201E, (char) 0x2026, (char) 0x2020, (char) 0x2021,
		(char) 0x02C6, (char) 0x2030, (char) 0x0160, (char) 0x2039, (char) 0x0152, (char) 0x008D, (char) 0x017D, (char) 0x008F,
		(char) 0x0090, (char) 0x2018, (char) 0x2019, (char) 0x201C, (char) 0x201D, (char) 0x2022, (char) 0x2013, (char) 0x2014,
		(char) 0x02DC, (char) 0x2122, (char) 0x0161, (char) 0x203A, (char) 0x0153, (char) 0x009D, (char) 0x017E, (char) 0x0178
	];
	#endregion

	#region Properties
	/// <summary>The encodings <see cref="Read"/> accepts, in the order the user interface offers them.</summary>
	public static IReadOnlyList<string> SupportedEncodings { get; } = ["utf-8", "utf-16", "utf-16be", "utf-32", "ascii", "latin1", "windows-1252"];
	#endregion

	#region Public methods
	/// <param name="requestedEncoding">Forces an encoding; empty detects one.</param>
	public static TextContent Read(string path, string requestedEncoding = "")
	{
		byte[] bytes = File.ReadAllBytes(path);

		if(!string.IsNullOrWhiteSpace(requestedEncoding))
		{
			if(IsWindows1252(requestedEncoding)) return new TextContent(DecodeWindows1252(bytes), "Windows-1252");

			Encoding requested = Resolve(requestedEncoding);
			return new TextContent(Decode(bytes, requested), Describe(requested));
		}

		Encoding? declared = DetectByteOrderMark(bytes);
		if(declared is not null) return new TextContent(Decode(bytes, declared), Describe(declared));

		try
		{
			return new TextContent(StrictUtf8.GetString(bytes), "UTF-8");
		}
		catch(DecoderFallbackException)
		{
			// Byte sequences no UTF-8 encoder would ever produce, so the file is single-byte: read it as such.
			return new TextContent(DecodeWindows1252(bytes), "Windows-1252");
		}
	}
	#endregion

	#region Private methods
	private static Encoding? DetectByteOrderMark(byte[] bytes)
	{
		return bytes switch
		{
			[0xEF, 0xBB, 0xBF, ..] => Encoding.UTF8,
			[0xFF, 0xFE, 0x00, 0x00, ..] => Encoding.UTF32,
			[0x00, 0x00, 0xFE, 0xFF, ..] => BigEndianUtf32,
			[0xFF, 0xFE, ..] => Encoding.Unicode,
			[0xFE, 0xFF, ..] => Encoding.BigEndianUnicode,
			_ => null
		};
	}

	private static string Decode(byte[] bytes, Encoding encoding)
	{
		byte[] preamble = encoding.GetPreamble();
		int start = bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;

		return encoding.GetString(bytes, start, bytes.Length - start);
	}

	private static string DecodeWindows1252(byte[] bytes)
	{
		char[] characters = new char[bytes.Length];
		for(int i = 0; i < bytes.Length; i++) characters[i] = bytes[i] is >= 0x80 and <= 0x9F ? Windows1252Upper[bytes[i] - 0x80] : (char) bytes[i];

		return new string(characters);
	}

	private static bool IsWindows1252(string name)
	{
		return name.Trim().ToLowerInvariant() is "windows-1252" or "windows1252" or "cp1252" or "1252" or "ansi";
	}

	private static Encoding Resolve(string name)
	{
		return name.Trim().ToLowerInvariant() switch
		{
			"utf8" or "utf-8" => Encoding.UTF8,
			"utf16" or "utf-16" or "utf-16le" or "unicode" => Encoding.Unicode,
			"utf-16be" or "unicodebe" => Encoding.BigEndianUnicode,
			"utf32" or "utf-32" => Encoding.UTF32,
			"ascii" => Encoding.ASCII,
			"latin1" or "latin-1" or "iso-8859-1" => Encoding.Latin1,
			_ => throw new NotSupportedException($"Unknown encoding '{name}'. Supported: utf-8, utf-16, utf-16be, utf-32, ascii, latin1, windows-1252.")
		};
	}

	private static string Describe(Encoding encoding)
	{
		return encoding.WebName.ToUpperInvariant();
	}
	#endregion
}