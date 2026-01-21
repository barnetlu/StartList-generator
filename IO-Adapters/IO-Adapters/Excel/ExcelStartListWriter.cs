using ClosedXML.Excel;
using IO_Adapters.Interfaces;
using IO_Adapters.SchedulerConfig;
using StartList_Core.Models;
using StartList_Core.Scheduling;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Excel
{
    public sealed class ExcelStartListWriter : IStartListWriter
    {
        public void Write(string outputPath, IReadOnlyList<Heat> heats, ExcelStyleConfig excelCfg, SchedulingReport? report = null)
        {
            using var wb = new XLWorkbook();
            WriteAllSheet(wb, heats, excelCfg);
            WriteClubsSheet(wb, heats, excelCfg);          // NOVÉ
            WriteResultsSheet(wb, heats, excelCfg);        // NOVÉ
            if (report != null)
                WriteReportSheet(wb, report);
            wb.SaveAs(outputPath);
        }

        private static void WriteAllSheet(XLWorkbook wb, IReadOnlyList<Heat> heats, ExcelStyleConfig style)
        {
            var ws = wb.Worksheets.Add("Startovka");
            int r = 1;
            int totalLanes = style.TotalLanes;

            // Header – 3 sloupce na dráhu: Name | SDH | Start#
            int col = 1;
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                ws.Cell(r, col++).Value = $"Dráha {lane} – Jméno a příjmení";
                ws.Cell(r, col++).Value = "SDH";
                ws.Cell(r, col++).Value = "Start #";
            }

            ws.Row(r).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);
            r++;

            foreach (var h in heats.OrderBy(x => x.Number))
            {
                var arranged = ArrangeLanes(h, style.TotalLanes);
                col = 1;

                for (int lane = 1; lane <= style.TotalLanes; lane++)
                {
                    var nameCell = ws.Cell(r, col++);
                    var clubCell = ws.Cell(r, col++);
                    var startCell = ws.Cell(r, col++);

                    startCell.Value = h.Number;
                    ApplyFill(startCell, style.LaneStartColors[lane]);

                    var c = arranged[lane - 1];
                    if (c is null)
                    {
                        nameCell.Value = "";
                        clubCell.Value = "";
                        continue;
                    }

                    nameCell.Value = $"{c.FirstName} {c.LastName}";
                    clubCell.Value = c.Club;

                    var rgb = style.CategoryColors.TryGetValue(c.Category.Key, out var cc)
                        ? cc
                        : style.DefaultCategoryColor;

                    ApplyFill(nameCell, rgb);
                    ApplyFill(clubCell, rgb);
                }

                r++;
            }

            ws.Columns().AdjustToContents(1, 60);
        }

        private static Dictionary<Competitor, (int heatNo, int laneNo)> BuildStartMap(IReadOnlyList<Heat> heats, int totalLanes)
        {
            var map = new Dictionary<Competitor, (int heatNo, int laneNo)>();

            foreach (var h in heats.OrderBy(x => x.Number))
            {
                var arranged = ArrangeLanes(h, totalLanes);
                for (int lane = 1; lane <= totalLanes; lane++)
                {
                    var c = arranged[lane - 1];
                    if (c is null) continue;

                    map[c] = (h.Number, lane);
                }
            }

            return map;
        }

        private static void ApplyFill(IXLCell cell, (int r, int g, int b) rgb)
        {
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(rgb.r, rgb.g, rgb.b);
        }

        private static Competitor?[] ArrangeLanes(Heat h, int totalLanes)
        {
            var lanes = new Competitor?[totalLanes];

            var brevno = h.Competitors.Where(c => c.Category.ObstacleType == ObstacleType.Crossbar).ToList();
            var bariera = h.Competitors.Where(c => c.Category.ObstacleType == ObstacleType.Barrier150).ToList();

            // Brevno zleva
            int i = 0;
            foreach (var c in brevno)
            {
                if (i >= totalLanes) break;
                lanes[i++] = c;
            }

            // Bariéra zprava (poslední dráhy)
            int j = totalLanes - 1;
            foreach (var c in bariera)
            {
                while (j >= 0 && lanes[j] != null) j--;
                if (j < 0) break;
                lanes[j--] = c;
            }

            return lanes;
        }

        private static void WriteClubsSheet(XLWorkbook wb, IReadOnlyList<Heat> heats, ExcelStyleConfig excelCfg)
        {
            var ws = wb.Worksheets.Add("SDH");
            int r = 1;

            var startMap = BuildStartMap(heats, excelCfg.TotalLanes);

            // vezmeme všechny závodníky z heats (unikátně)
            var all = heats.SelectMany(h => h.Competitors).Distinct().ToList();

            var clubs = all
                .GroupBy(c => (c.Club ?? "").Trim())
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var g in clubs)
            {
                // Nadpis SDH
                ws.Cell(r, 1).Value = g.Key;
                ws.Range(r, 1, r, 2).Merge();
                ws.Row(r).Style.Font.Bold = true;
                r++;

                // hlavička
                ws.Cell(r, 1).Value = "Start #";
                ws.Cell(r, 2).Value = "Jméno a příjmení";
                ws.Row(r).Style.Font.Bold = true;
                r++;

                foreach (var c in g.OrderBy(x => startMap.TryGetValue(x, out var s) ? s.heatNo : 999999))
                {
                    // když by někdo nebyl ve startMap (neměl přiřazený heat), dáme prázdno
                    startMap.TryGetValue(c, out var s);

                    var startCell = ws.Cell(r, 1);
                    var nameCell = ws.Cell(r, 2);

                    startCell.Value = s.heatNo == 0 ? "" : s.heatNo;
                    nameCell.Value = $"{c.FirstName} {c.LastName}";

                    // barvy: Start podle dráhy, Name podle kategorie
                    var laneColor = excelCfg.LaneStartColors.TryGetValue(s.laneNo, out var lc)
                        ? lc
                        : excelCfg.DefaultLaneStartColor;
                    ApplyFill(startCell, laneColor);

                    var catColor = excelCfg.CategoryColors.TryGetValue(c.Category.Key, out var cc)
                        ? cc
                        : excelCfg.DefaultCategoryColor;
                    ApplyFill(nameCell, catColor);

                    r++;
                }

                r++; // mezera mezi SDH
            }

            ws.Columns().AdjustToContents(1, 60);
        }

        private static void WriteResultsSheet(XLWorkbook wb, IReadOnlyList<Heat> heats, ExcelStyleConfig excelCfg)
        {
            var ws = wb.Worksheets.Add("Vysledky");

            int totalLanes = excelCfg.TotalLanes;
            int colsPerLane = 5;

            int r = 1;

            // ===== HEADER (stejně jako Startovka, jen s Čas + Poznámka) =====
            int col = 1;
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                ws.Cell(r, col++).Value = $"Dráha {lane} – #";
                ws.Cell(r, col++).Value = $"Dráha {lane} – Jméno a příjmení";
                ws.Cell(r, col++).Value = "SDH";
                ws.Cell(r, col++).Value = "Čas";
                ws.Cell(r, col++).Value = "Poznámka";
            }

            ws.Row(r).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);
            r++;

            // ===== DATA: jeden heat = jeden řádek =====
            foreach (var h in heats.OrderBy(x => x.Number))
            {
                var arranged = ArrangeLanes(h, totalLanes);

                col = 1;
                for (int lane = 1; lane <= totalLanes; lane++)
                {
                    var startCell = ws.Cell(r, col++);
                    var nameCell = ws.Cell(r, col++);
                    var clubCell = ws.Cell(r, col++);
                    var timeCell = ws.Cell(r, col++);
                    var noteCell = ws.Cell(r, col++);

                    // # (heat number) + barva dráhy
                    startCell.Value = h.Number;

                    var laneColor = excelCfg.LaneStartColors.TryGetValue(lane, out var lc)
                        ? lc
                        : excelCfg.DefaultLaneStartColor;
                    ApplyFill(startCell, laneColor);

                    var c = arranged[lane - 1];
                    if (c is null)
                    {
                        nameCell.Value = "";
                        clubCell.Value = "";
                        timeCell.Value = "";
                        noteCell.Value = "";
                        continue;
                    }

                    // Jméno + SDH (barva podle kategorie)
                    nameCell.Value = $"{c.FirstName} {c.LastName}";
                    clubCell.Value = c.Club;

                    var catColor = excelCfg.CategoryColors.TryGetValue(c.Category.Key, out var cc)
                        ? cc
                        : excelCfg.DefaultCategoryColor;

                    ApplyFill(nameCell, catColor);
                    ApplyFill(clubCell, catColor);

                    // Čas + Poznámka zůstanou prázdné
                    timeCell.Value = "";
                    noteCell.Value = "";
                }

                r++;
            }

            ws.Columns().AdjustToContents(1, 60);

            // # do středu pro všechny dráhy
            for (int lane = 1; lane <= totalLanes; lane++)
                ws.Column(1 + (lane - 1) * colsPerLane).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Volitelné: čas jako číslo se 2 desetinnými místy
            // (uživatel to pak může psát jako 12,34 nebo 12.34 dle nastavení)
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                int timeCol = 4 + (lane - 1) * colsPerLane;
                ws.Column(timeCol).Style.NumberFormat.Format = "0.00";
            }
        }


        private static void WriteReportSheet(XLWorkbook wb, SchedulingReport report)
        {
            var ws = wb.Worksheets.Add("Report");
            int r = 1;

            ws.Cell(r, 1).Value = "Warnings";
            ws.Row(r).Style.Font.Bold = true;
            r++;

            if (report.Warnings.Count == 0)
            {
                ws.Cell(r++, 1).Value = "(none)";
            }
            else
            {
                foreach (var w in report.Warnings)
                    ws.Cell(r++, 1).Value = w;
            }

            r += 2;

            ws.Cell(r, 1).Value = "Fallback summary";
            ws.Row(r).Style.Font.Bold = true;
            r++;

            ws.Cell(r, 1).Value = "RelaxCooldown";
            ws.Cell(r, 2).Value = report.SkippedBecauseCooldown;
            r++;

            r += 2;

            ws.Cell(r, 1).Value = "Fallback details";
            ws.Row(r).Style.Font.Bold = true;
            r++;

            ws.Cell(r, 1).Value = "Heat";
            ws.Cell(r, 2).Value = "UsedPool";
            ws.Cell(r, 3).Value = "RquestedPool";
            ws.Cell(r, 4).Value = "Club";
            ws.Cell(r, 5).Value = "Competitor";
            ws.Row(r).Style.Font.Bold = true;
            ws.Row(r).Style.Fill.BackgroundColor = XLColor.LightGray;
            r++;

            foreach (var f in report.FallbackPicks.OrderBy(f => f.HeatNo))
            {
                ws.Cell(r, 1).Value = f.HeatNo;
                ws.Cell(r, 2).Value = f.UsedPool;
                ws.Cell(r, 3).Value = f.RequestedPool.ToString();
                ws.Cell(r, 4).Value = f.Club;
                ws.Cell(r, 5).Value = f.CompetitorName;
                r++;
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(1, 100);
        }

        private static string SanitizeSheetName(string name)
        {
            // Excel limits: max 31 chars, no : \ / ? * [ ]
            var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            foreach (var ch in invalid) name = name.Replace(ch, '_');
            name = name.Trim();
            if (name.Length > 31) name = name.Substring(0, 31);
            if (string.IsNullOrWhiteSpace(name)) name = "Sheet";
            return name;
        }
    }
}
