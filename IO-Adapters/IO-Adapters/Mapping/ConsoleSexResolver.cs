using IO_Adapters.Interfaces;
using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping
{
    public sealed class ConsoleSexResolver : IManualResolver
    {
        public SexEnum Resolve(CompetitorDraft draft, AgeGroup group)
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
