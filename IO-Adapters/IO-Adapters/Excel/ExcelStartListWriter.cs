using ClosedXML.Excel;
using IO_Adapters.Interfaces;
using IO_Adapters.SchedulerConfig;
using StartList_Core.Models;
using StartList_Core.Models.Enums;
using StartList_Core.Scheduling.Report;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Excel
{
    public sealed class ExcelStartListWriter : IStartListWriter
    {
        public void Write(string outputPath, IReadOnlyList<Heat> heats, AppConfig cfg, SchedulingReport? report = null)
        {
            using var wb = new XLWorkbook();
            WriteAllSheet(wb, heats, cfg);
            WriteClubsSheet(wb, heats, cfg);
            WriteResultsSheet(wb, heats, cfg);
            if (report != null)
                WriteReportSheet(wb, report);
            wb.SaveAs(outputPath);
        }

        public byte[] WriteToBytes(IReadOnlyList<Heat> heats, AppConfig cfg, SchedulingReport? report = null)
        {
            using var wb = new XLWorkbook();
            WriteAllSheet(wb, heats, cfg);
            WriteClubsSheet(wb, heats, cfg);
            WriteResultsSheet(wb, heats, cfg);
            if (report != null)
                WriteReportSheet(wb, report);
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteAllSheet(XLWorkbook wb, IReadOnlyList<Heat> heats, AppConfig style)
        {
            var ws = wb.Worksheets.Add("Startovka");
            int r = 1;
            int totalLanes = style.Excel.TotalLanes;
            int colsPerLane = 4;

            ws.Cell(r, 1).Value = "Startovka";
            ws.Range(r, 1, r, totalLanes * colsPerLane-1).Merge();
            ws.Row(r).Style.Font.Bold = true;
            ws.Row(r).Height = 24.75;
            ws.Row(r).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(r).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            r++;
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                ws.Cell(r, (lane - 1) * colsPerLane + 1).Value = $"Dráha {lane}";
                ws.Range(r, (lane - 1) * colsPerLane + 1, r, lane * colsPerLane-1).Merge();
            }
            ws.Row(r).Style.Font.Bold = true;
            ws.Row(r).Height = 24.75;
            ws.Row(r).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(r).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            r++;

            // Header – 3 sloupce na dráhu: Name | SDH | Start#
            int col = 1;
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                ws.Column(col).Width = 23;
                ws.Cell(r, col++).Value = $"Jméno a příjmení";
                ws.Column(col).Width = 23;
                ws.Cell(r, col++).Value = "SDH";
                ws.Column(col).Width = 3.5;
                ws.Cell(r, col++).Value = "#";
                ws.Column(col++).Width = 2;
            }

            ws.Row(r).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);
            ws.Row(r).Height = 24.75;
            r++;

            var startNos = BuildStartNumbers(heats, style.Scheduler.StartNumberMode);

            foreach (var h in heats.OrderBy(x => x.Number))
            {
                var arranged = h.Lanes;
                col = 1;

                for (int lane = 1; lane <= style.Excel.TotalLanes; lane++)
                {
                    var nameCell = ws.Cell(r, col++);
                    var clubCell = ws.Cell(r, col++);
                    var startCell = ws.Cell(r, col++);
                    col++;

                    var c = arranged[lane - 1];
                    if (c is null)
                    {
                        nameCell.Value = "";
                        clubCell.Value = "";
                        continue;
                    }

                    // FIX: Check for null before accessing startNos[c]
                    if (startNos.TryGetValue(c, out var startNo))
                    {
                        startCell.Value = startNo;
                        ApplyFill(startCell, style.Excel.LaneStartColors[lane]);
                    }
                    else
                    {
                        startCell.Value = "";
                    }
                    nameCell.Value = $"{c.FirstName} {c.LastName}";
                    clubCell.Value = c.Club;

                    var rgb = style.Excel.CategoryColors.TryGetValue(c.Category.Key, out var cc)
                        ? cc
                        : style.Excel.DefaultCategoryColor;

                    ApplyFill(nameCell, rgb);
                    ApplyFill(clubCell, rgb);
                }
                ws.Row(r).Height = 24.75;
                r++;
            }

            IXLRange tableRange;
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                tableRange = ws.Range(2, (lane - 1) * colsPerLane + 1, r-1, lane * colsPerLane - 1);
                tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                tableRange.Style.Border.InsideBorderColor = XLColor.Black;
                tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                tableRange.Style.Border.OutsideBorderColor = XLColor.Black;
            }
        }

        private static Dictionary<Competitor, (int heatNo, int laneNo)> BuildStartMap(IReadOnlyList<Heat> heats, int totalLanes)
        {
            var map = new Dictionary<Competitor, (int heatNo, int laneNo)>();

            foreach (var h in heats.OrderBy(x => x.Number))
            {
                var arranged = h.Lanes;
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
            // bariéry zprava v pořadí: 200 -> 170 -> 150
            var barriers = h.Competitors
                .Where(c => c.Category.ObstacleType == ObstacleType.Barrier200)
                .Concat(h.Competitors.Where(c => c.Category.ObstacleType == ObstacleType.Barrier170))
                .Concat(h.Competitors.Where(c => c.Category.ObstacleType == ObstacleType.Barrier150));
            //var bariera150 = h.Competitors.Where(c => c.Category.ObstacleType == ObstacleType.Barrier150).ToList();
            //var bariera170 = h.Competitors.Where(c => c.Category.ObstacleType == ObstacleType.Barrier170).ToList();
            //var bariera200 = h.Competitors.Where(c => c.Category.ObstacleType == ObstacleType.Barrier200).ToList();

            // Brevno zleva
            int i = 0;
            foreach (var c in brevno)
            {
                if (i >= totalLanes) break;
                lanes[i++] = c;
            }

            // Bariéra zprava (poslední dráhy)
            int j = totalLanes - 1;
            foreach (var c in barriers)
            {
                while (j >= 0 && lanes[j] != null) j--;
                if (j < 0) break;
                lanes[j--] = c;
            }
            //foreach (var c in bariera170)
            //{
            //    while (j >= 0 && lanes[j] != null) j--;
            //    if (j < 0) break;
            //    lanes[j--] = c;
            //}
            //foreach (var c in bariera150)
            //{
            //    while (j >= 0 && lanes[j] != null) j--;
            //    if (j < 0) break;
            //    lanes[j--] = c;
            //}

            return lanes;
        }

        private static void WriteClubsSheet(XLWorkbook wb, IReadOnlyList<Heat> heats, AppConfig cfg)
        {
            var ws = wb.Worksheets.Add("SDH");
            int r = 1;

            var startMap = BuildStartMap(heats, cfg.Excel.TotalLanes);
            var startNos = BuildStartNumbers(heats, cfg.Scheduler.StartNumberMode);

            // vezmeme všechny závodníky z heats (unikátně)
            var all = heats.SelectMany(h => h.Competitors).Distinct().ToList();

            var clubs = all
                .GroupBy(c => (c.Club ?? "").Trim())
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var g in clubs)
            {
                var firstRow = r;
                // Nadpis SDH
                ws.Cell(r, 1).Value = g.Key;
                ws.Range(r, 1, r, 2).Merge();
                ws.Row(r).Style.Font.Bold = true;
                ws.Row(r).Height = 24.75;
                r++;

                // hlavička
                ws.Cell(r, 1).Value = "#";
                ws.Cell(r, 2).Value = "Jméno a příjmení";
                ws.Row(r).Style.Font.Bold = true;
                ws.Row(r).Height = 24.75;
                r++;

                foreach (var c in g.OrderBy(x => startMap.TryGetValue(x, out var s) ? s.heatNo : 999999))
                {
                    // když by někdo nebyl ve startMap (neměl přiřazený heat), dáme prázdno
                    startMap.TryGetValue(c, out var s);

                    var startCell = ws.Cell(r, 1);
                    var nameCell = ws.Cell(r, 2);

                    if (startNos.TryGetValue(c, out var startNo))
                    {
                        startCell.Value = startNo;
                    }
                    else
                    {
                        startCell.Value = "";
                    }
                    nameCell.Value = $"{c.FirstName} {c.LastName}";

                    // barvy: Start podle dráhy, Name podle kategorie
                    var laneColor = cfg.Excel.LaneStartColors.TryGetValue(s.laneNo, out var lc)
                        ? lc
                        : cfg.Excel.DefaultLaneStartColor;
                    ApplyFill(startCell, laneColor);

                    var catColor = cfg.Excel.CategoryColors.TryGetValue(c.Category.Key, out var cc)
                        ? cc
                        : cfg.Excel.DefaultCategoryColor;
                    ApplyFill(nameCell, catColor);
                    ws.Row(r).Height = 24.75;
                    r++;
                }
                var tableRange = ws.Range(firstRow, 1, r - 1, 2);
                tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                tableRange.Style.Border.InsideBorderColor = XLColor.Black;
                tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                tableRange.Style.Border.OutsideBorderColor = XLColor.Black;
                r++; // mezera mezi SDH
            }

            ws.Columns().AdjustToContents(1, 60);
        }

        private static void WriteResultsSheet(XLWorkbook wb, IReadOnlyList<Heat> heats, AppConfig cfg)
        {
            var ws = wb.Worksheets.Add("Zápis");

            int totalLanes = cfg.Excel.TotalLanes;
            int colsPerLane = 6;

            int r = 1;
            ws.Cell(r,1).Value = "Zápis výsledků";
            ws.Range(r,1,r,totalLanes * colsPerLane - 1).Merge();
            ws.Row(r).Style.Font.Bold = true;
            ws.Row(r).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(r).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(r).Height = 24.75;
            r++;
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                ws.Cell(r, (lane - 1)*colsPerLane + 1).Value = $"Dráha {lane}";
                ws.Range(r, (lane - 1)*colsPerLane + 1, r, lane*colsPerLane-1).Merge();
            }
            ws.Row(r).Style.Font.Bold = true;
            ws.Row(r).Height = 24.75;
            r++;
            // ===== HEADER (stejně jako Startovka, jen s Čas + Poznámka) =====
            int col = 1;
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                ws.Column(col).Width = 3.5;
                ws.Cell(r, col++).Value = $"#";
                ws.Column(col).Width = 23;
                ws.Cell(r, col++).Value = $"Jméno a příjmení";
                ws.Column(col).Width = 23;
                ws.Cell(r, col++).Value = "SDH";
                ws.Column(col).Width = 8.5;
                ws.Cell(r, col++).Value = "Čas";
                ws.Column(col).Width = 12;
                ws.Cell(r, col++).Value = "Poznámka";
                ws.Column(col++).Width = 2;
            }

            ws.Row(r).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);
            ws.Row(r).Height = 24.75;
            r++;
            var startNos = BuildStartNumbers(heats, cfg.Scheduler.StartNumberMode);

            // ===== DATA: jeden heat = jeden řádek =====
            foreach (var h in heats.OrderBy(x => x.Number))
            {
                var arranged = h.Lanes;

                col = 1;
                for (int lane = 1; lane <= totalLanes; lane++)
                {
                    var startCell = ws.Cell(r, col++);
                    var nameCell = ws.Cell(r, col++);
                    var clubCell = ws.Cell(r, col++);
                    var timeCell = ws.Cell(r, col++);
                    var noteCell = ws.Cell(r, col++);
                    col++;

                    var laneColor = cfg.Excel.LaneStartColors.TryGetValue(lane, out var lc)
                        ? lc
                        : cfg.Excel.DefaultLaneStartColor;
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

                    // FIX: Check for null before accessing startNos[c]
                    if (startNos.TryGetValue(c, out var startNo))
                    {
                        startCell.Value = startNo;
                        ApplyFill(startCell, cfg.Excel.LaneStartColors[lane]);
                    }
                    else
                    {
                        startCell.Value = "";
                    }

                    // Jméno + SDH (barva podle kategorie)
                    nameCell.Value = $"{c.FirstName} {c.LastName}";
                    clubCell.Value = c.Club;

                    var catColor = cfg.Excel.CategoryColors.TryGetValue(c.Category.Key, out var cc)
                        ? cc
                        : cfg.Excel.DefaultCategoryColor;

                    ApplyFill(nameCell, catColor);
                    ApplyFill(clubCell, catColor);

                    // Čas + Poznámka zůstanou prázdné
                    timeCell.Value = "";
                    noteCell.Value = "";
                }
                ws.Row(r).Height = 24.75;
                r++;
            }

            IXLRange tableRange;
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                tableRange = ws.Range(2, (lane - 1) * colsPerLane + 1, r - 1, lane * colsPerLane - 1);
                tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                tableRange.Style.Border.InsideBorderColor = XLColor.Black;
                tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                tableRange.Style.Border.OutsideBorderColor = XLColor.Black;
            }

            //           ws.Columns().AdjustToContents(1, 60);

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

        public static Dictionary<Competitor, int> BuildStartNumbers(IReadOnlyList<Heat> heats, StartNumberMode mode)
        {
            var map = new Dictionary<Competitor, int>(SchedulerConfig.ReferenceEqualityComparer.Instance);

            if (mode == StartNumberMode.PerHeat)
            {
                foreach (var h in heats)
                {
                    foreach (var c in h.Competitors)
                        map[c] = h.Number;
                }
                return map;
            }

            // GlobalSequential
            int n = 1;

            // pořadí: heat 1..N, v heatu lane 1..L pokud existují, jinak pořadí Competitors
            foreach (var h in heats.OrderBy(x => x.Number))
            {
                IEnumerable<Competitor?> seq = h.Lanes is not null
                    ? h.Lanes
                    : h.Competitors.Cast<Competitor?>();

                foreach (var c in seq)
                {
                    if (c is null) continue;
                    if (!map.ContainsKey(c))
                        map[c] = n++;
                }
            }

            return map;
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
