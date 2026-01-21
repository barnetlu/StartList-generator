using ClosedXML.Excel;
using IO_Adapters.Interfaces;
using IO_Adapters.Mapping;
using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Excel
{
    public sealed class ExcelEntryReader : IEntryReader
    {
        private readonly CategoryCatalog _categoryCatalog;
        private readonly string? _sheetName;
        private readonly CategoryTextMapper _categoryMapper;
        private readonly ConsoleSexResolver _sexResolver = new();

        public ExcelEntryReader(CategoryCatalog categoryCatalog, CategoryTextMapper categoryMapper, string? sheetName = null)
        {
            _categoryCatalog = categoryCatalog;
            _sheetName = sheetName;
            _categoryMapper = categoryMapper;
        }

        public IReadOnlyList<Competitor> Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Excel file not found.", filePath);

            using var wb = new XLWorkbook(filePath);
            var ws = _sheetName is null ? wb.Worksheets.First() : wb.Worksheet(_sheetName);

            // najdeme hlavičku v 1. řádku
            var headerRow = ws.Row(1);

            int Col(string name)
            {
                var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
                for (int c = 1; c <= lastCol; c++)
                {
                    var text = headerRow.Cell(c).GetString().Trim();
                    if (string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
                throw new InvalidOperationException($"Missing column '{name}' in header row.");
            }

            var cFirst = Col("Jméno");
            var cLast = Col("Príjmení");
            var cClub = Col("SDH");
            var cCat = Col("Kategorie (RAW)");
            var cYear = Col("Datum narození");

            // PB je volitelné
            int? cPb = null;
            try { cPb = Col("PB"); } catch { /* ignore */ }

            var result = new List<Competitor>();

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);

                // prázdný řádek přeskočíme
                if (row.CellsUsed().All(c => string.IsNullOrWhiteSpace(c.GetString())))
                    continue;

                var firstName = row.Cell(cFirst).GetString().Trim();
                var lastName = row.Cell(cLast).GetString().Trim();
                var club = row.Cell(cClub).GetString().Trim();
                var catCode = row.Cell(cCat).GetString();

                if (!int.TryParse(row.Cell(cYear).GetString().Trim(), out var birthYear))
                {
                    // když je to fakt číselná buňka
                    if (row.Cell(cYear).TryGetValue<double>(out var n))
                        birthYear = DateTime.FromOADate(n).Year;
                    else
                        throw new InvalidOperationException($"Row {r}: BirthYear is not a number.");
                }
                var (ageGroup, sex, sub) = _categoryMapper.Map(catCode);

                if (sex == SexEnum.Mixed && _categoryMapper.IsSexRequired(ageGroup))
                {
                    var draft = new CompetitorDraft
                    {
                        RowNumber = r,
                        FirstName = firstName,
                        LastName = lastName,
                        Club = club,
                        BirthYear = birthYear,
                        CategoryRaw = catCode,
                        SexRaw = ""
                    };

                    sex = _sexResolver.Resolve(draft, ageGroup);
                }
                var categoryCode = CategoryCodeBuilder.Build(ageGroup, sex, sub);

                TimeSpan? pb = null;
                if (cPb.HasValue)
                {
                    var pbText = row.Cell(cPb.Value).GetString().Trim();
                    pb = ParsePb(pbText);
                }

                result.Add(new Competitor
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Club = club,
                    Category =  _categoryCatalog.GetByCodeOrThrow(categoryCode),
                    BirthYear = birthYear,
                    PersonalBest = pb
                });
            }

            return result;
        }

        private static TimeSpan? ParsePb(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // 00:18.35 (TimeSpan neumí setiny přes tečku vždy spolehlivě podle kultury),
            // takže ošetříme i formát "18.35"
            text = text.Trim();

            // zkus "mm:ss.ff" nebo "ss.ff"
            // 1) pokud je tam dvojtečka, zkus TimeSpan
            if (text.Contains(':') && TimeSpan.TryParse(text, out var ts))
                return ts;

            // 2) "18.35" sekundy
            if (double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }

            return null; // PB ignorujeme, pokud je neparsovatelné
        }
    }

}