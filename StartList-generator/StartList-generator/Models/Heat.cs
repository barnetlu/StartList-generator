using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Models
{
    public class Heat
    {
        public int Number { get; init; }
        public Category Category { get; init; }

        public Competitor?[] Lanes { get; }

        public IReadOnlyList<Competitor> Competitors => Lanes.Where(x => x is not null).Cast<Competitor>().ToList();

        public Heat()
        {
            Number = 0;
            Category = new Category();
            Lanes = Array.Empty<Competitor?>();
        }
        public Heat(int number, Category category, Competitor?[] lanes)
        {
            Number = number;
            Category = category;
            Lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
        }
    }
}
