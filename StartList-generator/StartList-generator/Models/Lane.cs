using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Models
{
    public class Lane
    {
        public int Number { get; init; }
        public Competitor? Competitor { get; set; }
    }
}
