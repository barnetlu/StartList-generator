using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Models
{
    public class Competitor
    {
        public string FirstName { get; init; }
        public string LastName { get; init; }

        public string Club { get; init; }

        public Category Category { get; init; }

        public int BirthYear { get; init; }
        public TimeSpan? PersonalBest { get; init; }

        public Competitor()
        {
            FirstName = string.Empty;
            LastName = string.Empty;
            Club = string.Empty;
            Category = new Category();
        }
    }
}
