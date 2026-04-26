using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StartList_Core.Scheduling
{
    public static class NumberingScheduler
    {
        // colorGroups[i] = pre-sorted competitors for color i (0-based lane index).
        // Returns heats where Heat.Number = start number (1-based).
        // With PerHeat mode: all competitors in heat K get start number K.
        // Competitors of color i are placed in lane i+1 across heats 1..N_i,
        // so each color gets its own independent sequential range 1..N_i.
        public static IReadOnlyList<Heat> Generate(IReadOnlyList<IReadOnlyList<Competitor>> colorGroups)
        {
            if (colorGroups == null || colorGroups.Count == 0) return Array.Empty<Heat>();

            int totalColors = colorGroups.Count;
            int maxCount = colorGroups.Max(g => g.Count);
            var heats = new List<Heat>(maxCount);

            for (int k = 0; k < maxCount; k++)
            {
                var lanes = new Competitor?[totalColors];
                Category? placeholder = null;

                for (int i = 0; i < totalColors; i++)
                {
                    if (k < colorGroups[i].Count)
                    {
                        lanes[i] = colorGroups[i][k];
                        placeholder ??= colorGroups[i][k].Category;
                    }
                }

                heats.Add(new Heat(k + 1, placeholder ?? new Category(), lanes));
            }

            return heats;
        }
    }
}
