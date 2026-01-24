
using ConsoleRun;
using IO_Adapters;
using IO_Adapters.Excel;
using IO_Adapters.Mapping;
using IO_Adapters.Mapping.IO_Adapters.Mapping;
using IO_Adapters.SchedulerConfig;
using StartList_Core.Models;
using StartList_Core.Scheduling;

Console.WriteLine(AppDomain.CurrentDomain.BaseDirectory);
var choice = AskChoice("Závod:", "60 m překážek", "100 m překážek");
var ctx = choice == 1 ? Runners.BuildRun60() : Runners.BuildRun100();



var inputDir = Ask("Složka s přihláškami");
var outputPath = Ask("Výstupní soubor", $@".\Startovka_{(choice == 1 ? "60" : "100")}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.xlsx");


Runners.Run(ctx, inputDir, outputPath);

static string Ask(string label, string? @default = null)
{
    Console.Write($"{label}{(@default is null ? "" : $" [{@default}]")}: ");
    var input = (Console.ReadLine() ?? "").Trim();
    return string.IsNullOrWhiteSpace(input) ? (@default ?? "") : input;
}


static int AskChoice(string label, params string[] options)
{
    Console.WriteLine(label);
    for (int i = 0; i < options.Length; i++)
        Console.WriteLine($" {i + 1}) {options[i]}");
    while (true)
    {
        Console.Write("Vyber číslo: ");
        if (int.TryParse((Console.ReadLine() ?? "").Trim(), out var n) && n >= 1 && n <= options.Length)
            return n;
    }
}