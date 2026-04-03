using IO_Adapters.Interfaces;
using IO_Adapters.Mapping;
using StartList_Core.Models;
using System.Globalization;
using System.Text;

namespace IO_Adapters
{
    /// <summary>
    /// Čte přihlášky ze středníkově odděleného souboru (export online přihlašovacího systému).
    /// Formát: středníkový oddělovač, UTF-8 s BOM, hlavička v prvním řádku.
    /// Povinné sloupce: Jméno, Příjmení, Datum narození, Kategorie, Organizace.
    /// </summary>
    public sealed class CommonRegistrationReader : IEntryReader
    {
        private readonly CategoryCatalog _categoryCatalog;
        private readonly CategoryTextMapper _categoryMapper;
        private readonly IInteractiveResolver _resolver;

        public CommonRegistrationReader(CategoryCatalog categoryCatalog, CategoryTextMapper categoryMapper, IInteractiveResolver resolver)
        {
            _categoryCatalog = categoryCatalog;
            _categoryMapper = categoryMapper;
            _resolver = resolver;
        }

        public IReadOnlyList<Competitor> Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Soubor nenalezen.", filePath);

            return ReadFromLines(File.ReadAllLines(filePath, Encoding.UTF8));
        }

        public IReadOnlyList<Competitor> ReadFromLines(string[] lines)
        {
            if (lines.Length < 2)
                return Array.Empty<Competitor>();

            // Odstraň případný UTF-8 BOM ze záhlaví
            var headers = lines[0].TrimStart('\uFEFF').Split(';');

            int? jmenoCol      = FindCol(headers, "jmeno", "jméno");
            int? prijmeniCol   = FindCol(headers, "prijmeni", "příjmení");
            int? datumCol      = FindCol(headers, "datum");
            int? kategorieCol  = FindCol(headers, "kategorie");
            int? organizaceCol = FindCol(headers, "organizace");

            if (jmenoCol is null || prijmeniCol is null || datumCol is null || kategorieCol is null || organizaceCol is null)
            {
                var missing = new List<string>();
                if (jmenoCol is null) missing.Add("Jméno");
                if (prijmeniCol is null) missing.Add("Příjmení");
                if (datumCol is null) missing.Add("Datum narození");
                if (kategorieCol is null) missing.Add("Kategorie");
                if (organizaceCol is null) missing.Add("Organizace");
                throw new InvalidOperationException(
                    $"Nenalezeny povinné sloupce: {string.Join(", ", missing)}. " +
                    $"Nalezené hlavičky: {string.Join(", ", headers)}");
            }

            var result = new List<Competitor>();

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = line.Split(';');

                string firstName = GetCol(cols, jmenoCol.Value).Trim();
                string lastName  = GetCol(cols, prijmeniCol.Value).Trim();

                if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
                    continue;

                string datumRaw = GetCol(cols, datumCol.Value).Trim();
                int? birthYear  = ParseBirthYear(datumRaw);

                string catRaw = GetCol(cols, kategorieCol.Value).Trim();
                string club   = GetCol(cols, organizaceCol.Value).Trim();

                if (string.IsNullOrWhiteSpace(catRaw))
                    throw new InvalidOperationException($"Řádek {i + 1}: Chybí kategorie.");

                var draft = new CompetitorDraft
                {
                    RowNumber   = i + 1,
                    FirstName   = firstName,
                    LastName    = lastName,
                    Club        = club,
                    BirthYear   = birthYear ?? 0,
                    CategoryRaw = catRaw,
                    SexRaw      = ""
                };

                if (!birthYear.HasValue)
                    birthYear = _resolver.ResolveBirthYear(draft, datumRaw);

                var draftWithYear = new CompetitorDraft
                {
                    RowNumber   = draft.RowNumber,
                    FirstName   = draft.FirstName,
                    LastName    = draft.LastName,
                    Club        = draft.Club,
                    BirthYear   = birthYear.Value,
                    CategoryRaw = draft.CategoryRaw,
                    SexRaw      = draft.SexRaw
                };

                var (ageGroup, sex, sub) = _categoryMapper.Map(catRaw, birthYear, draftWithYear, _resolver);
                var categoryCode = CategoryCodeBuilder.Build(ageGroup, sex, sub);

                result.Add(new Competitor
                {
                    FirstName    = firstName,
                    LastName     = lastName,
                    Club         = club,
                    Category     = _categoryCatalog.GetByCodeOrThrow(categoryCode),
                    BirthYear    = birthYear.Value,
                    PersonalBest = null
                });
            }

            return result;
        }

        private static int? FindCol(string[] headers, params string[] keywords)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                var h = Normalize(headers[i]);
                if (h != null && keywords.Any(k => h.Contains(k)))
                    return i;
            }
            return null;
        }

        private static string GetCol(string[] cols, int idx)
            => idx < cols.Length ? cols[idx] : "";

        private static int? ParseBirthYear(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var formats = new[] { "d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "dd.MM.yy" };
            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.Year;
            return null;
        }

        private static string? Normalize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().ToLowerInvariant();
            var normalized = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
