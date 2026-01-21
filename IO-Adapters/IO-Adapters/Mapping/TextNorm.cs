using System.Globalization;
using System.Text;

namespace IO_Adapters.Mapping
{
    public static class TextNorm
    {
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            // trim + upper
            input = input.Trim().ToUpperInvariant();

            // remove diacritics
            var formD = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);

            foreach (var ch in formD)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            var noDia = sb.ToString().Normalize(NormalizationForm.FormC);

            // unify separators to space
            noDia = noDia
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Replace('/', ' ')
                .Replace('\\', ' ')
                .Replace('.', ' ')
                .Replace(',', ' ')
                .Replace(';', ' ')
                .Replace(':', ' ')
                .Replace("  ", " ");

            return noDia.Trim();
        }

        public static IReadOnlyList<string> Tokens(string input)
        {
            var n = Normalize(input);
            if (n.Length == 0) return Array.Empty<string>();
            return n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
