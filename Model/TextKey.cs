using System.Text;

namespace FileComparerWindows.Model;

/// <summary>
/// Canonical form for text that is matched rather than displayed. An accented letter can arrive as
/// one character (u-umlaut is U+00FC) or as the plain letter followed by a combining accent (U+0075
/// U+0308). The two are indistinguishable on screen but are different strings, so a column name
/// typed one way never finds a header written the other way. Everything that is compared - column
/// names, key values, cell values - goes through here first.
/// </summary>
public static class TextKey
{
	#region Public methods
	public static string Canonical(string value)
	{
		// The overwhelmingly common case, and one that no normalisation could change.
		if(value.Length == 0 || Ascii.IsValid(value)) return value;

		try
		{
			return value.IsNormalized(NormalizationForm.FormC) ? value : value.Normalize(NormalizationForm.FormC);
		}
		catch(ArgumentException)
		{
			// Unpaired surrogates cannot be normalised; match them exactly as they came in.
			return value;
		}
	}
	#endregion
}