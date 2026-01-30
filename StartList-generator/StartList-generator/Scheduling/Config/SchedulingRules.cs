using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling.Config
{
    public sealed class SchedulingRules
    {
        public bool MaxOneClubPerHeat { get; init; }
        public int ClubCooldownHeats { get; init; }
    }
}
