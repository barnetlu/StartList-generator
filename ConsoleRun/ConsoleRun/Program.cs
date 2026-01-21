
using IO_Adapters;
using IO_Adapters.Excel;
using IO_Adapters.Mapping;
using IO_Adapters.SchedulerConfig;
using StartList_Core.Models;
using StartList_Core.Scheduling;

// 1) CategoryCatalog
var catalog = new CategoryCatalog(new[]
{
    new Category("ML_M", "Mladší žáci – chlapci", SexEnum.Male,   AgeGroup.Mladsi,    SubGroup.None, ObstacleType.Crossbar, lanes: 3, seedByPerformance: false),
    new Category("ML_F", "Mladší žáci – dívky",   SexEnum.Female, AgeGroup.Mladsi,    SubGroup.None, ObstacleType.Crossbar, lanes: 3, seedByPerformance: false),
    new Category("ST_M", "Starší žáci – chlapci", SexEnum.Male,   AgeGroup.Starsi,    SubGroup.None, ObstacleType.Barrier150, lanes: 3, seedByPerformance: false),
    new Category("ST_F", "Starší žáci – dívky",   SexEnum.Female, AgeGroup.Starsi,    SubGroup.None, ObstacleType.Crossbar, lanes: 3, seedByPerformance: false),
    new Category("PR_MIX","Přípravka",            SexEnum.Mixed,  AgeGroup.Pripravka, SubGroup.None, ObstacleType.Crossbar, lanes: 3, seedByPerformance: false),
});

// 2) Config paths
var mappingPath = @"Config/category-mapping.json";
var schedulerPath = @"Config/scheduler-config.json";
var excelPath = @"C:\Users\adbalu1\OneDrive - Siemens AG\soukromé\hasiči\OSH UO\Makra_jednotlivci\Zamberk\Startovka_v3_60m.xlsm";
var outputPath = @"C:\Users\adbalu1\OneDrive - Siemens AG\soukromé\hasiči\OSH UO\Makra_jednotlivci\Zamberk\Startovka_vystup_" + DateTime.Now.ToString("yyyy-mm-dd_HH-mm-ss") + ".xlsx";

// 3) Load mapping + mapper
var mapping = MappingConfigLoader.Load(mappingPath);
var mapper = new CategoryTextMapper(mapping);

// 4) Load scheduler config (order + rules)
var appCfg = AppConfigLoader.Load(schedulerPath);

// 5) Read competitors from Excel
var reader = new ExcelEntryReader(catalog, mapper, sheetName: "Zavodnici"); // nebo null
var competitors = reader.Read(excelPath);

Console.WriteLine($"Loaded competitors: {competitors.Count}");
foreach (var g in competitors.GroupBy(c => c.Category.Code).OrderBy(g => g.Key))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

// 6) Scheduler (club-first)
//var scheduler = new ClubFirstScheduler(schedCfg.CategoryOrder, schedCfg.Rules);

//var (heats, report) = scheduler.GenerateWithReport(competitors);

var scheduler = new TwoPhaseTrackScheduler(appCfg.Scheduler.TrackPlan, appCfg.Scheduler.CategoryOrder, appCfg.Scheduler.Rules);
(var heats, var report) = scheduler.GenerateWithReport(competitors);

Console.WriteLine();
Console.WriteLine($"Generated heats: {heats.Count}");

var writer = new ExcelStartListWriter();
writer.Write(outputPath, heats, appCfg.Excel, report);
Console.WriteLine($"Saved: {outputPath}");