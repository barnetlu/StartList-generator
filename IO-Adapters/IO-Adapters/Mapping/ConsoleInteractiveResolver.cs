using DocumentFormat.OpenXml.Vml.Office;
using IO_Adapters.Interfaces;
using StartList_Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping
{
    public class ConsoleInteractiveResolver : IInteractiveResolver
    {
        public (AgeGroup, SubGroup) ResolveAgeGroupMismatch(CompetitorDraft draft, AgeGroup parsedGroup, SubGroup parsedSub, AgeGroup expectedGroup, SubGroup expectedSub, IReadOnlySet<AgeGroup> noSubGroupIfGroup)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine($"⚠️ Nesoulad kategorie vs ročník (řádek {draft.RowNumber}): {draft.FirstName} {draft.LastName}, {draft.Club}, {draft.BirthYear}");
                Console.WriteLine($"Kategorie text: '{draft.CategoryRaw}'");
                Console.WriteLine($"Parsed: {parsedGroup} {parsedSub}");
                Console.WriteLine($"Expected: {expectedGroup} {expectedSub}");
                Console.Write("Volba: [1] parsed, [2] expected, [3] ručně vybrat: ");


                var choice = (Console.ReadLine() ?? "").Trim();


                if (choice == "1") 
                {
                    var sub = noSubGroupIfGroup.Contains(parsedGroup) ? SubGroup.None : parsedSub;
                    return (parsedGroup, sub); 
                }

                if (choice == "2")
                {
                    var sub = noSubGroupIfGroup.Contains(expectedGroup) ? SubGroup.None : expectedSub;
                    return (expectedGroup, sub);
                }

                if (choice == "3")
                {
                
                    var values = Enum.GetValues(typeof(AgeGroup)).Cast<AgeGroup>()
                    .Where(g => g != AgeGroup.Unknown) // pokud chceš
                    .ToList();

                group:
                    for (int i = 0; i < values.Count; i++)
                        Console.WriteLine($" [{i + 1}] {values[i]}");


                    Console.Write("Vyber číslo skupiny: ");
                    var pick = (Console.ReadLine() ?? "").Trim();

                    AgeGroup group;

                    if (int.TryParse(pick, out var n) && n >= 1 && n <= values.Count)
                        group = values[n - 1];
                    else
                    {
                        Console.WriteLine("Neplatná volba.");
                        goto group;
                    }
                    if (noSubGroupIfGroup.Contains(group)) 
                        return (group, SubGroup.None);
                    
                    var values2 = Enum.GetValues(typeof(SubGroup)).Cast<SubGroup>()
                    .ToList();

                subgroup:
                    for (int i = 0; i < values2.Count; i++)
                        Console.WriteLine($" [{i + 1}] {values2[i]}");


                    Console.Write("Vyber číslo podskupiny: ");
                    var pick2 = (Console.ReadLine() ?? "").Trim();

                    SubGroup subgroup;

                    if (int.TryParse(pick2, out var n2) && n2 >= 1 && n2 <= values2.Count)
                        subgroup = values2[n2 - 1];
                    else
                    {
                        Console.WriteLine("Neplatná volba.");
                        goto subgroup;
                    }

                    return (group, subgroup);
                }
                Console.WriteLine("Neplatná volba.");
            }
        }

        public int ResolveBirthYear(CompetitorDraft draft, string? birthRaw)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine($"Chybí/neplatné datum narození (řádek {draft.RowNumber}): {draft.FirstName} {draft.LastName}, {draft.Club}");
                Console.WriteLine($"Hodnota v Excelu: '{birthRaw ?? ""}'");
                Console.Write("Zadej rok narození (YYYY): ");


                var input = (Console.ReadLine() ?? "").Trim();
                if (int.TryParse(input, out var y) && y >= 1980 && y <= DateTime.Now.Year)
                    return y;


                Console.WriteLine("Neplatná hodnota. Zadej rok jako YYYY.");
            }
        }

        public SexEnum ResolveSex(CompetitorDraft draft, AgeGroup group)
        {
            // Přípravka = mix, neptáme se
            if (group == AgeGroup.Pripravka)
                return SexEnum.Mixed;

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine($"Chybí pohlaví (řádek {draft.RowNumber}): {draft.FirstName} {draft.LastName}, {draft.Club}, {draft.BirthYear}");
                Console.WriteLine($"Kategorie text: '{draft.CategoryRaw}'");
                Console.Write("Zadej pohlaví [M/F]: ");

                string input;
                do
                {
                    input = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();
                } while (string.IsNullOrWhiteSpace(input));

                if (input is "M" or "MALE") return SexEnum.Male;
                if (input is "F" or "FEMALE") return SexEnum.Female;

                Console.WriteLine("Neplatná hodnota. Zadej M nebo F!");
            }
        }
    }
}
