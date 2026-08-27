using System.IO;
using System.Text;

namespace FileComparerWindows.Model;

/// <summary>A file's contents together with the encoding they turned out to be written in.</summary>
public sealed record TextContent(string Text, string EncodingName)
{
	#region Public methods
	public string[] Lines()
	{
		return this.Text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
	}
	#endregion
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
	private static readonly UTF8Encoding STRICT_UTF8 = new(false, true);
	private static readonly Encoding BIG_ENDIAN_UTF32 = new UTF32Encoding(true, true);

	/// <summary>
	/// Windows-1252 is Latin-1 except across 0x80-0x9F, which hold the euro sign and the typographic
	/// quotes and dashes. One entry per byte from 0x80 to 0x9F, the five unassigned slots included.
	/// Code points rather than the characters themselves, so that re-saving this file in another
	/// encoding cannot quietly corrupt the very table that repairs mis-encoded input.
	/// </summary>
	private static readonly char[] WINDOWS1252_UPPER =
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
		byte[] liBytes = File.ReadAllBytes(path);

		if(!string.IsNullOrWhiteSpace(requestedEncoding))
		{
			if(IsWindows1252(requestedEncoding)) return new TextContent(DecodeWindows1252(liBytes), "Windows-1252");

			Encoding requested = Resolve(requestedEncoding);
			return new TextContent(Decode(liBytes, requested), Describe(requested));
		}

		Encoding? declared = DetectByteOrderMark(liBytes);
		if(declared is not null) return new TextContent(Decode(liBytes, declared), Describe(declared));

		try
		{
			return new TextContent(STRICT_UTF8.GetString(liBytes), "UTF-8");
		}
		catch(DecoderFallbackException)
		{
			// Byte sequences no UTF-8 encoder would ever produce, so the file is single-byte: read it as such.
			return new TextContent(DecodeWindows1252(liBytes), "Windows-1252");
		}
	}
	#endregion

	#region Private methods
	private static Encoding? DetectByteOrderMark(byte[] liBytes)
	{
		return liBytes switch
		{
			[0xEF, 0xBB, 0xBF, ..] => Encoding.UTF8,
			[0xFF, 0xFE, 0x00, 0x00, ..] => Encoding.UTF32,
			[0x00, 0x00, 0xFE, 0xFF, ..] => BIG_ENDIAN_UTF32,
			[0xFF, 0xFE, ..] => Encoding.Unicode,
			[0xFE, 0xFF, ..] => Encoding.BigEndianUnicode,
			_ => null
		};
	}

	private static string Decode(byte[] liBytes, Encoding encoding)
	{
		byte[] liPreamble = encoding.GetPreamble();
		int nStart = liBytes.AsSpan().StartsWith(liPreamble) ? liPreamble.Length : 0;

		return encoding.GetString(liBytes, nStart, liBytes.Length - nStart);
	}

	private static string DecodeWindows1252(byte[] liBytes)
	{
		char[] liCharacters = new char[liBytes.Length];
		for(int i = 0; i < liBytes.Length; i++) liCharacters[i] = liBytes[i] is >= 0x80 and <= 0x9F ? WINDOWS1252_UPPER[liBytes[i] - 0x80] : (char) liBytes[i];

		return new string(liCharacters);
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