using ClosedXML.Excel;
using IO_Adapters.Interfaces;
using IO_Adapters.Mapping;
using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IO_Adapters.Excel
{
    public sealed class ExcelRegistrationReader : IEntryReader
    {
        private readonly CategoryCatalog _categoryCatalog;
        private readonly CategoryTextMapper _categoryMapper;
        private readonly IInteractiveResolver _resolver;
        private readonly string? _sheetName;

        // okres -> OSH
        private static readonly Dictionary<string, string> OkresToOsh = new(StringComparer.OrdinalIgnoreCase)
    {
        { "usti nad orlici", "UO" },
        { "rychnov nad kneznou", "RK" },
        { "svitavy", "SY" },
        { "chrudim", "CR" },
    };

        public ExcelRegistrationReader(CategoryCatalog categoryCatalog, CategoryTextMapper categoryMapper, IInteractiveResolver resolver, string? sheetName = null)
        {
            _categoryCatalog = categoryCatalog;
            _categoryMapper = categoryMapper;
            _sheetName = sheetName;
            _resolver = resolver;
        }

        public IReadOnlyList<Competitor> Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Excel file not found.", filePath);
            using var stream = File.OpenRead(filePath);
            return ReadFromStream(stream);
        }

        public IReadOnlyList<Competitor> ReadFromStream(Stream stream)
        {
            {
                using var wb = new XLWorkbook(stream);
                var ws = _sheetName is null ? wb.Worksheets.First() : wb.Worksheet(_sheetName);

                // 1) hlavičkové údaje
                var club = FindLabelValue(ws, "sdh") ?? "";
                var okresHeader = FindLabelValue(ws, "okres"); // např. "Ústí nad Orlicí"

                // 2) najdi header row tabulky
                var headerRow = FindHeaderRow(ws)
                    ?? throw new InvalidOperationException("Nelze najít hlavičku tabulky (poř. č., jméno, kategorie, ...).");

                var map = BuildColumnMap(ws, headerRow);

                // 3) konec tabulky: borders + fallback 3 prázdné
                int firstDataRow = headerRow + 1;
                int lastRowByBorders = FindTableLastRowByBorders(ws, firstDataRow, map.LeftCol, map.RightCol, 250);
                int lastRow = lastRowByBorders > 0 ? lastRowByBorders : (ws.LastRowUsed()?.RowNumber() ?? firstDataRow);

                var result = new List<Competitor>();
                int emptyStreak = 0;

                for (int r = firstDataRow; r <= lastRow; r++)
                {
                    var orderCell = ws.Cell(r, map.OrderCol);
                    var nameCell = ws.Cell(r, map.NameCol);

                    bool isEmpty = string.IsNullOrWhiteSpace(orderCell.GetString()) && string.IsNullOrWhiteSpace(nameCell.GetString());
                    if (isEmpty)
                    {
                        emptyStreak++;
                        if (emptyStreak >= 3) break;
                        continue;
                    }
                    emptyStreak = 0;

                    // ignoruj proškrtnuté
                    if (orderCell.Style.Font.Strikethrough || nameCell.Style.Font.Strikethrough)
                        continue;

                    var fullName = nameCell.GetString().Trim();
                    if (string.IsNullOrWhiteSpace(fullName))
                        continue;

                    string firstName, lastName;
                    if (map.LastNameCol.HasValue)
                    {
                        firstName = fullName;
                        lastName = ws.Cell(r, map.LastNameCol.Value).GetString().Trim();
                    }
                    else
                    {
                        (firstName, lastName) = SplitName(fullName);
                    }

                    // Klub: preferuj sloupec Organizace, fallback na hlavičku SDH
                    var clubEffective = map.OrganizaceCol.HasValue
                        ? ws.Cell(r, map.OrganizaceCol.Value).GetString().Trim()
                        : club ?? "";

                    // birth year
                    int? birthYear = null;
                    string? birthRaw = null;
                    if (map.BirthCol.HasValue)
                    {
                        var dt = TryGetDate(ws.Cell(r, map.BirthCol.Value), out birthRaw);
                        if (dt.HasValue) birthYear = dt.Value.Year;
                        else birthYear = TryExtractYear(birthRaw);
                    }



                    var catRaw = map.CategoryCol.HasValue ? ws.Cell(r, map.CategoryCol.Value).GetString().Trim() : "";
                    if (string.IsNullOrWhiteSpace(catRaw))
                        throw new InvalidOperationException($"Row {r}: Chybí kategorie.");

                    // OSH – teď ho neukládáš do Competitor, ale můžeš ho potřebovat pro bodování.
                    // Zatím jen dopočítáme a případně později přidáme do modelu / metadata.
                    string? oshCell = map.OshCol.HasValue ? ws.Cell(r, map.OshCol.Value).GetString().Trim() : null;
                    var (oshNorm, _, _) = NormalizeOsh(oshCell);
                    var oshEffective = oshNorm ?? MapOkresHeaderToOsh(okresHeader);

                    var draft_temp = new CompetitorDraft
                    {
                        RowNumber = r,
                        FirstName = firstName,
                        LastName = lastName,
                        Club = clubEffective,
                        BirthYear = 0,
                        CategoryRaw = catRaw,
                        SexRaw = ""
                    };

                    if (!birthYear.HasValue)
                    {
                        birthYear = _resolver.ResolveBirthYear(draft_temp, birthRaw);
                    }
                    var draft = new CompetitorDraft
                    {
                        RowNumber = r,
                        FirstName = firstName,
                        LastName = lastName,
                        Club = clubEffective,
                        BirthYear = birthYear.Value,
                        CategoryRaw = catRaw,
                        SexRaw = ""
                    };

                    // category mapping + sex fallback (stejně jako v ExcelEntryReader)
                    var (ageGroup, sex, sub) = _categoryMapper.Map(catRaw, birthYear, draft, _resolver);

                    var categoryCode = CategoryCodeBuilder.Build(ageGroup, sex, sub);

                    result.Add(new Competitor
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Club = clubEffective,
                        Category = _categoryCatalog.GetByCodeOrThrow(categoryCode),
                        BirthYear = birthYear.Value,
                        PersonalBest = null // v přihlášce PB typicky není
                    });
                }

                return result;
            }
        }

        // ----------------- helpers -----------------

        private static int? FindHeaderRow(IXLWorksheet ws)
        {
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 1; r <= lastRow; r++)
            {
                var texts = ws.Row(r).CellsUsed().Select(c => Normalize(c.GetString()) ?? "").ToList();
                if (texts.Count == 0) continue;

                bool hasPor = texts.Any(t => t.Contains("por") || t.Contains("poř"));
                bool hasJm = texts.Any(t => t.Contains("jmeno") || t.Contains("jméno"));
                bool hasKat = texts.Any(t => t.Contains("kategorie"));
                bool hasOsh = texts.Any(t => t == "osh" || t.Contains("osh"));

                if (hasPor && hasJm && (hasKat || hasOsh))
                    return r;
            }
            return null;
        }

        private sealed record ColMap(
            int OrderCol,
            int NameCol,
            int? LastNameCol,
            int? BirthCol,
            int? CategoryCol,
            int? OshCol,
            int? OrganizaceCol,
            int LeftCol,
            int RightCol
        );

        private static ColMap BuildColumnMap(IXLWorksheet ws, int headerRow)
        {
            int? orderCol = null, nameCol = null, lastNameCol = null, birthCol = null, catCol = null, oshCol = null, organizaceCol = null;

            var used = ws.Row(headerRow).CellsUsed().ToList();
            foreach (var cell in used)
            {
                var t = Normalize(cell.GetString());
                if (t == null) continue;

                int c = cell.Address.ColumnNumber;

                if (t.Contains("por")) orderCol = c;
                else if (t.Contains("jmeno") || t.Contains("jméno")) nameCol = c;
                else if (t.Contains("prijmeni")) lastNameCol = c;
                else if (t.Contains("datum") && (t.Contains("naro") || t.Contains("naroz"))) birthCol = c;
                else if (t.Contains("kategorie")) catCol = c;
                else if (t == "osh" || t.Contains("osh")) oshCol = c;
                else if (t.Contains("organizace")) organizaceCol = c;
            }

            if (orderCol is null || nameCol is null)
                throw new InvalidOperationException($"HeaderRow {headerRow}: Nenalezen povinný sloupec poř. č. nebo jméno.");

            int left = orderCol.Value;
            int right = oshCol ?? catCol ?? used.Max(c => c.Address.ColumnNumber);

            return new ColMap(orderCol.Value, nameCol.Value, lastNameCol, birthCol, catCol, oshCol, organizaceCol, left, right);
        }

        private static int FindTableLastRowByBorders(IXLWorksheet ws, int firstDataRow, int leftCol, int rightCol, int maxScanRows = 200)
        {
            int lastUsed = ws.LastRowUsed()?.RowNumber() ?? firstDataRow;
            int scanUntil = Math.Min(lastUsed, firstDataRow + maxScanRows);

            int lastGridRow = 0;
            int miss = 0;

            for (int r = firstDataRow; r <= scanUntil; r++)
            {
                bool gridRow = true;

                for (int c = leftCol; c <= rightCol; c++)
                {
                    var b = ws.Cell(r, c).Style.Border;
                    bool hasBorder =
                        b.BottomBorder != XLBorderStyleValues.None ||
                        b.TopBorder != XLBorderStyleValues.None ||
                        b.LeftBorder != XLBorderStyleValues.None ||
                        b.RightBorder != XLBorderStyleValues.None;

                    if (!hasBorder)
                    {
                        gridRow = false;
                        break;
                    }
                }

                if (gridRow)
                {
                    lastGridRow = r;
                    miss = 0;
                }
                else
                {
                    miss++;
                    if (miss >= 2) break;
                }
            }

            return lastGridRow;
        }

        private static string? FindLabelValue(IXLWorksheet ws, string labelKey)
        {
            foreach (var cell in ws.CellsUsed())
            {
                var t = Normalize(cell.GetString());
                if (t == null) continue;

                if (t.StartsWith(labelKey) || t.StartsWith(labelKey + ":"))
                {
                    int r = cell.Address.RowNumber;
                    int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? cell.Address.ColumnNumber;

                    for (int c = cell.Address.ColumnNumber + 1; c <= lastCol; c++)
                    {
                        var val = ws.Cell(r, c).GetString().Trim();
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }
                }
            }
            return null;
        }

        private static (string FirstName, string LastName) SplitName(string fullName)
        {
            // "Jméno Příjmení" -> last token jako příjmení
            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return (parts[0], "");

            var last = parts[^1];
            var first = string.Join(" ", parts.Take(parts.Length - 1));
            return (first, last);
        }

        private static DateTime? TryGetDate(IXLCell cell, out string? rawText)
        {
            rawText = null;

            if (cell.TryGetValue<DateTime>(out var dt))
                return dt.Date;

            if (cell.TryGetValue<double>(out var d))
            {
                if (d > 1000 && d < 60000)
                {
                    var oa = DateTime.FromOADate(d).Date;
                    if (oa.Year >= 1980 && oa.Year <= DateTime.Now.Year)
                        return oa;
                }
            }

            var s = cell.GetString()?.Trim();
            rawText = s;

            if (string.IsNullOrWhiteSpace(s))
                return null;

            var formats = new[] { "d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "dd.MM.yy", "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd" };
            if (DateTime.TryParseExact(s, formats, new CultureInfo("cs-CZ"), DateTimeStyles.None, out var parsed))
                return parsed.Date;

            if (DateTime.TryParse(s, new CultureInfo("cs-CZ"), DateTimeStyles.None, out parsed))
                return parsed.Date;

            return null;
        }

        private static int? TryExtractYear(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            for (int i = 0; i <= s.Length - 4; i++)
            {
                if (char.IsDigit(s[i]) && char.IsDigit(s[i + 1]) && char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3]))
                {
                    var sub = s.Substring(i, 4);
                    if (int.TryParse(sub, out int y) && y >= 1980 && y <= DateTime.Now.Year)
                        return y;
                }
            }
            return null;
        }

        private static string? MapOkresHeaderToOsh(string? okresHeader)
        {
            if (string.IsNullOrWhiteSpace(okresHeader)) return null;
            var key = TextNorm.Normalize(okresHeader).ToLowerInvariant();
            return OkresToOsh.TryGetValue(key, out var osh) ? osh : null;
        }

        private static (string? osh, string? warnCode, string? warnMsg) NormalizeOsh(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return (null, null, null);

            var s = RemoveDiacritics(input.Trim()).ToUpperInvariant();
            s = s.Replace("OKRES", " ").Replace("OSH", " ");
            s = Regex.Replace(s, @"[.\-_,;()]", " ");
            s = s.Replace("/", " / ").Replace("\\", " / ");
            s = Regex.Replace(s, @"\s+", " ").Trim();

            var compact = s.Replace(" ", "").Replace("/", "");

            // UO variants
            if (compact == "UO" || compact == "UNO" || compact == "USTI")
                return ("UO", null, null);

            if (compact.Contains("USTI") && (compact.Contains("ORLICI") || compact.Contains("ORL") || compact.Contains("NO") || compact.Contains("NADO")))
                return ("UO", null, null);

            // RK/SY/CR text variants
            if (compact.Contains("RYCHNOV") && compact.Contains("KNEZNOU")) return ("RK", null, null);
            if (compact.Contains("SVITAVY")) return ("SY", null, null);
            if (compact.Contains("CHRUDIM")) return ("CR", null, null);

            var tokens = Regex.Matches(s, @"\b[A-Z]{2,3}\b").Select(m => m.Value).Distinct().ToList();
            if (tokens.Count == 0) return (null, "OSH_UNPARSEABLE", $"Neumím vytěžit OSH z '{input}'");
            if (tokens.Count == 1) return (tokens[0], null, null);

            return (tokens[0], "OSH_MULTI", $"Více OSH ({string.Join(",", tokens)}), beru první {tokens[0]}. Původní: '{input}'");
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

        private static string RemoveDiacritics(string input)
        {
            var normalized = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        public static List<Competitor> DeduplicateInteractive(List<Competitor> input)
        {
            string Key(Competitor c) =>
            $"{Norm(c.Club)}|{Norm(c.FirstName)}|{Norm(c.LastName)}|{c.BirthYear}";


            var groups = input
            .Select((c, idx) => (c, idx))
            .GroupBy(x => Key(x.c))
            .Where(g => g.Count() > 1)
            .ToList();


            if (groups.Count == 0)
                return input;


            Console.WriteLine();
            Console.WriteLine($"⚠️ Nalezeno možných duplicit (stejný SDH+jméno+rok): {groups.Count}");


            var keep = new bool[input.Count];
            Array.Fill(keep, true);


            foreach (var g in groups)
            {
                var items = g.ToList();
                var sample = items[0].c;


                Console.WriteLine();
                Console.WriteLine($"Duplicitní záznamy: {sample.Club} – {sample.FirstName} {sample.LastName} ({sample.BirthYear})");
                for (int i = 0; i < items.Count; i++)
                {
                    var c = items[i].c;
                    Console.WriteLine($" [{i + 1}] {c.Category.Code} (index {items[i].idx})");
                }


                Console.Write("Akce: [1] ponechat první, [2] ponechat všechny, [3] vybrat ručně: ");
                var choice = (Console.ReadLine() ?? "").Trim();


                if (choice == "2")
                {
                    continue; // nech vše
                }
                else if (choice == "3")
                {
                    Console.Write("Zadej čísla k ponechání (např. 1,3): ");
                    var line = (Console.ReadLine() ?? "").Trim();


                    var selected = new HashSet<int>();
                    foreach (var part in line.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(part.Trim(), out var n) && n >= 1 && n <= items.Count)
                            selected.Add(n);
                    }


                    for (int i = 0; i < items.Count; i++)
                    {
                        if (!selected.Contains(i + 1))
                            keep[items[i].idx] = false;
                    }
                }
                else
                {
                    // default: keep first
                    for (int i = 1; i < items.Count; i++)
                        keep[items[i].idx] = false;
                }
            }


            return input.Where((c, idx) => keep[idx]).ToList();
        }


        static string Norm(string? s) => TextNorm.Normalize(s ?? "");

    }
}
