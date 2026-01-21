using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Rules
{
    public class Ruleset : IRule
    {
        public int Lanes { get; set; }
        public int MaxSameClubPerHeat { get; set; }

        public Ruleset() 
        {
            Lanes = 0;
            MaxSameClubPerHeat = 0;
        }

        public Ruleset(int lanes, int maxSameClubPerHeat) 
        {
            Lanes = lanes;
            MaxSameClubPerHeat = maxSameClubPerHeat;
        }

        public bool IsSatisfied(Heat heat)
        {
            return true;
        }
    }
}
