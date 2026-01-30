using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.SchedulerConfig.DTO
{
    public sealed class SchedulerRulesDto
    {
        public bool MaxOneClubPerHeat { get; set; } = true;
        public int ClubCooldownHeats { get; set; } = 2;
    }
}
