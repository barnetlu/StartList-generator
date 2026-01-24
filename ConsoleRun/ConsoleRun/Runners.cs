using IO_Adapters;
using IO_Adapters.Excel;
using IO_Adapters.Interfaces;
using IO_Adapters.Mapping;
using IO_Adapters.Mapping.IO_Adapters.Mapping;
using IO_Adapters.SchedulerConfig;
using StartList_Core.Models;
using StartList_Core.Scheduling;
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


            Console.WriteLine($"Loaded competitors: {competitors.Count}");
            foreach (var g in competitors.GroupBy(c => c.Category.Code).OrderBy(g => g.Key))
                Console.WriteLine($" {g.Key}: {g.Count()}");


            competitors = ExcelRegistrationReader.DeduplicateInteractive(competitors);
            Console.WriteLine($"After dedupe: {competitors.Count}");


            // scheduler
            var scheduler = ctx.SchedulerFactory(appCfg);


            // obranná kontrola (optional)
            if (competitors.Any(c => c.Category.Discipline != ctx.Discipline))
                throw new InvalidOperationException("Načteni závodníci neodpovídají zvolené disciplíně (catalog/config mismatch).");


            var (heats, report) = scheduler.GenerateWithReport(competitors);


            Console.WriteLine();
            Console.WriteLine($"Generated heats: {heats.Count}");


            var writer = new ExcelStartListWriter();
            writer.Write(outputPath, heats, appCfg.Excel, report);


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
