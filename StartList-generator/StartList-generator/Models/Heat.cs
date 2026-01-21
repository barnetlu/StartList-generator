using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Models
{
    public class Heat
    {
        public int Number { get; init; }
        public Category Category { get; init; }

        public IReadOnlyList<Competitor> Competitors { get; } = [];

        public Heat()
        {
            Number = 0;
            Category = new Category();
        }
        public Heat(int number, Category category, IReadOnlyList<Competitor> competitors)
        {
            Number = number;
            Category = category;
            Competitors = competitors;
        }
    }
}
