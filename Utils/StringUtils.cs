using System.Globalization;
using System.Text;

namespace GameInventoryApi.Utils;

public static class StringUtils
{
    /// <summary>
    /// Removes diacritics and converts special characters (like Vietnamese 'đ', 'Đ')
    /// into standard base Latin characters, lowercased for search indexing/comparison.
    /// E.g. "Tuấn" -> "tuan", "Đạt" -> "dat", "Hương" -> "huong"
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Custom replacements for characters that FormD does not decompose (such as Vietnamese đ/Đ)
        var sbPre = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == 'đ' || c == 'Đ')
                sbPre.Append('d');
            else
                sbPre.Append(c);
        }

        string normalizedString = sbPre.ToString().Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        foreach (char c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
