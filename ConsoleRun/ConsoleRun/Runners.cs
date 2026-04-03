using IO_Adapters;
using IO_Adapters.Excel;
using IO_Adapters.Interfaces;
using IO_Adapters.Mapping;
using IO_Adapters.Mapping.IO_Adapters.Mapping;
using IO_Adapters.SchedulerConfig;
using StartList_Core.Models;
using StartList_Core.Models.Enums;
using StartList_Core.Scheduling;
using StartList_Core.Scheduling.Config;
using StartList_Core.Scheduling.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace ConsoleRun
{
    public sealed record RunContext(
        Discipline Discipline,
        string MappingPath,
        string SchedulerPath,
        CategoryCatalog Catalog,
        Func<AppConfig, IScheduler> SchedulerFactory,
        string? SheetName = null
        );

    internal static class Runners
    {
        internal static void Run(RunContext ctx, string inputDir, string outputPath)
        {
            // mapping + mapper
            var mapping = MappingConfigLoader.Load(ctx.MappingPath);
            var mapper = new CategoryTextMapper(mapping);

            // scheduler config
            var appCfg = AppConfigLoader.Load(ctx.SchedulerPath);
            appCfg = ConfigureTrackPlan(appCfg, ctx.SchedulerPath, ctx.Discipline);

            // resolver + reader
            IInteractiveResolver resolver = new ConsoleInteractiveResolver();
            var reader = new ExcelRegistrationReader(ctx.Catalog, mapper, resolver, sheetName: ctx.SheetName);

            // files
            var excelFiles = Directory
            .EnumerateFiles(inputDir, "*.xls*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("~$"))
            .ToList();

            // read
            var competitors = new List<Competitor>();

            foreach (var file in excelFiles)
            {
                Console.WriteLine();
                Console.WriteLine($"=== Reading: {Path.GetFileName(file)} ===");

                try
                {
                    competitors.AddRange(reader.Read(file));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR při čtení {file}");
                    Console.WriteLine(ex.Message);

                    Console.Write("Pokračovat dál? [A]no / [N]e: ");
                    var input = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();
                    if (input != "A") return;
                }
            }

            RunScheduler(ctx, appCfg, competitors, outputPath);
        }

        internal static void RunSingleFile(RunContext ctx, string filePath, string outputPath)
        {
            // mapping + mapper
            var mapping = MappingConfigLoader.Load(ctx.MappingPath);
            var mapper = new CategoryTextMapper(mapping);

            // scheduler config
            var appCfg = AppConfigLoader.Load(ctx.SchedulerPath);
            appCfg = ConfigureTrackPlan(appCfg, ctx.SchedulerPath, ctx.Discipline);

            // resolver + reader
            IInteractiveResolver resolver = new ConsoleInteractiveResolver();
            var reader = new CommonRegistrationReader(ctx.Catalog, mapper, resolver);

            // read
            Console.WriteLine();
            Console.WriteLine($"=== Reading: {Path.GetFileName(filePath)} ===");

            List<Competitor> competitors;
            try
            {
                competitors = reader.Read(filePath).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR při čtení {filePath}");
                Console.WriteLine(ex.Message);
                return;
            }

            RunScheduler(ctx, appCfg, competitors, outputPath);
        }

        private static void RunScheduler(RunContext ctx, AppConfig appCfg, List<Competitor> competitors, string outputPath)
        {
            Console.WriteLine($"Loaded competitors: {competitors.Count}");
            foreach (var g in competitors.GroupBy(c => c.Category.Code).OrderBy(g => g.Key))
                Console.WriteLine($" {g.Key}: {g.Count()}");

            competitors = ExcelRegistrationReader.DeduplicateInteractive(competitors);
            Console.WriteLine($"After dedupe: {competitors.Count}");

            // obranná kontrola (optional)
            if (competitors.Any(c => c.Category.Discipline != ctx.Discipline))
                throw new InvalidOperationException("Načteni závodníci neodpovídají zvolené disciplíně (catalog/config mismatch).");

            var scheduler = ctx.SchedulerFactory(appCfg);
            var (heats, report) = scheduler.GenerateWithReport(competitors);

            Console.WriteLine();
            Console.WriteLine($"Generated heats: {heats.Count}");

            var writer = new ExcelStartListWriter();
            writer.Write(outputPath, heats, appCfg, report);

            Console.WriteLine($"Saved: {outputPath}");
        }

        internal static RunContext BuildRun60()
        {
            var catalog = CategoryCatalogLoader.Load(
                @"Config/categories.json",
                CategoryCodeBuilder.Build, Discipline.Run60);
            //var catalog = new CategoryCatalog(new[]
            //{
            //    new Category(CategoryCodeBuilder.Build(),"Přípravka", SexEnum.Mixed, AgeGroup.Pripravka, SubGroup.None, ObstacleType.Crossbar, Discipline.Run60, seedByPerformance:false),
            //    new Category("ML_F", "Mladší žáci – dívky", SexEnum.Female, AgeGroup.Mladsi, SubGroup.None, ObstacleType.Crossbar, Discipline.Run60, seedByPerformance:false),
            //    new Category("ML_M", "Mladší žáci – chlapci", SexEnum.Male, AgeGroup.Mladsi, SubGroup.None, ObstacleType.Crossbar, Discipline.Run60, seedByPerformance:false),
            //    new Category("ST_F", "Starší žáci – dívky", SexEnum.Female, AgeGroup.Starsi, SubGroup.None, ObstacleType.Crossbar, Discipline.Run60, seedByPerformance:false),
            //    new Category("ST_M", "Starší žáci – chlapci", SexEnum.Male, AgeGroup.Starsi, SubGroup.None, ObstacleType.Barrier150, Discipline.Run60, seedByPerformance:false),
            //});


            return new RunContext(
            Discipline.Run60,
            MappingPath: @"Config/category-mapping-60.json",
            SchedulerPath: @"Config/scheduler-config-60.json",
            Catalog: catalog,
            SchedulerFactory: cfg => new IndependentLaneSwitchScheduler(cfg.Scheduler.TrackPlan, cfg.Scheduler.CategoryOrder, cfg.Scheduler.Rules),
            SheetName: null
            );
        }


        private static AppConfig ConfigureTrackPlan(AppConfig cfg, string schedulerPath, Discipline discipline)
        {
            var tp = cfg.Scheduler.TrackPlan;

            Console.WriteLine();
            Console.WriteLine("=== Nastavení dráhy ===");

            int totalLanes = AskInt("Počet drah", tp.TotalLanes);

            TrackPlan newTp;
            if (discipline == Discipline.Run60)
            {
                int initialBariera  = AskInt("Počet drah s bariérou (před přepnutím)", tp.InitialBarieraLanes);
                int afterBariera    = AskInt("Počet drah s bariérou (po přepnutí)",    tp.AfterSwitchBarieraLanes);

                if (initialBariera > totalLanes)
                    throw new InvalidOperationException($"Počet drah s bariérou ({initialBariera}) nesmí překročit celkový počet drah ({totalLanes}).");
                if (afterBariera > totalLanes)
                    throw new InvalidOperationException($"Počet drah s bariérou po přepnutí ({afterBariera}) nesmí překročit celkový počet drah ({totalLanes}).");

                newTp = new TrackPlan
                {
                    TotalLanes             = totalLanes,
                    InitialBarieraLanes    = initialBariera,
                    AfterSwitchBarieraLanes = afterBariera,
                    SwitchRule             = tp.SwitchRule
                };
            }
            else
            {
                int initialB170 = AskInt("Počet drah s bariérou 170 (před přepnutím)", tp.InitialBariera170Lanes);
                int afterB170   = AskInt("Počet drah s bariérou 170 (po přepnutí)",    tp.AfterSwitchBariera170Lanes);
                int initialB200 = AskInt("Počet drah s bariérou 200 (před přepnutím)", tp.InitialBariera200Lanes);
                int afterB200   = AskInt("Počet drah s bariérou 200 (po přepnutí)",    tp.AfterSwitchBariera200Lanes);

                if (initialB170 + initialB200 > totalLanes)
                    throw new InvalidOperationException($"Součet bariér 170+200 před přepnutím ({initialB170 + initialB200}) nesmí překročit počet drah ({totalLanes}).");
                if (afterB170 + afterB200 > totalLanes)
                    throw new InvalidOperationException($"Součet bariér 170+200 po přepnutí ({afterB170 + afterB200}) nesmí překročit počet drah ({totalLanes}).");

                newTp = new TrackPlan
                {
                    TotalLanes                 = totalLanes,
                    InitialBariera170Lanes     = initialB170,
                    AfterSwitchBariera170Lanes = afterB170,
                    InitialBariera200Lanes     = initialB200,
                    AfterSwitchBariera200Lanes = afterB200,
                    SwitchRule                 = tp.SwitchRule
                };
            }

            var newScheduler = new SchedulerConfig
            {
                CategoryOrder  = cfg.Scheduler.CategoryOrder,
                Rules          = cfg.Scheduler.Rules,
                TrackPlan      = newTp,
                StartNumberMode = cfg.Scheduler.StartNumberMode
            };

            var newExcel = ExcelStyleConfigLoader.Load(schedulerPath, totalLanes);

            return new AppConfig { Scheduler = newScheduler, Excel = newExcel };
        }

        private static int AskInt(string label, int defaultValue)
        {
            while (true)
            {
                Console.Write($"{label} [{defaultValue}]: ");
                var input = (Console.ReadLine() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(input)) return defaultValue;
                if (int.TryParse(input, out var n) && n >= 0) return n;
                Console.WriteLine("  Zadej celé číslo >= 0.");
            }
        }

        internal static RunContext BuildRun100()
        {
            var catalog = CategoryCatalogLoader.Load(
               @"Config/categories.json",
               CategoryCodeBuilder.Build, Discipline.Run100);
            //var catalog = new CategoryCatalog(new[]
            //{
            //    new Category("DOR-ML_F", "Dorostenky - mladší", SexEnum.Female, AgeGroup.Dorost, SubGroup.Mladsi, ObstacleType.Crossbar, Discipline.Run100, seedByPerformance:false),
            //    new Category("DOR-STR_F","Dorostenky - střední", SexEnum.Female, AgeGroup.Dorost, SubGroup.Stredni, ObstacleType.Crossbar, Discipline.Run100, seedByPerformance:false),
            //    new Category("DOR-ST_F", "Dorostenky - starší", SexEnum.Female, AgeGroup.Dorost, SubGroup.Starsi, ObstacleType.Crossbar, Discipline.Run100, seedByPerformance:false),
            //    new Category("AD_F", "Ženy", SexEnum.Female, AgeGroup.Dospeli, SubGroup.None, ObstacleType.Crossbar, Discipline.Run100, seedByPerformance:false),


            //    new Category("DOR-ML_M", "Dorostenci - mladší", SexEnum.Male, AgeGroup.Dorost, SubGroup.Mladsi, ObstacleType.Barrier170, Discipline.Run100, seedByPerformance:false),
            //    new Category("DOR-STR_M","Dorostenci - střední", SexEnum.Male, AgeGroup.Dorost, SubGroup.Stredni, ObstacleType.Barrier170, Discipline.Run100, seedByPerformance:false),
            //    new Category("DOR-ST_M", "Dorostenci - starší", SexEnum.Male, AgeGroup.Dorost, SubGroup.Starsi, ObstacleType.Barrier200, Discipline.Run100, seedByPerformance:false),
            //    new Category("AD_M", "Muži", SexEnum.Male, AgeGroup.Dospeli, SubGroup.None, ObstacleType.Barrier200, Discipline.Run100, seedByPerformance:false),
            //});


            return new RunContext(
            Discipline.Run100,
            MappingPath: @"Config/category-mapping-100.json",
            SchedulerPath: @"Config/scheduler-config-100.json",
            Catalog: catalog,
            SchedulerFactory: cfg => new IndependentLaneSwitchScheduler(cfg.Scheduler.TrackPlan, cfg.Scheduler.CategoryOrder, cfg.Scheduler.Rules), // nový pro 100m
            SheetName: null
            );
        }
    }
}
