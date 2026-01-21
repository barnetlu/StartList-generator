using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.SchedulerConfig
{
    public sealed class TrackPlanDto
    {
        public int TotalLanes { get; set; } = 3;
        public int InitialBarieraLanes { get; set; } = 0;
        public int AfterSwitchBarieraLanes { get; set; } = 1;
        public string SwitchRule { get; set; } = "BarieraRemainingEqualsBrevnoRemainingDiv2";
    }
}
