using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.SchedulerConfig
{
    public sealed class TrackPlanDto
    {
        public int TotalLanes { get; set; } = 3;

        // 60m legacy
        public int InitialBarieraLanes { get; set; } = 0;
        public int AfterSwitchBarieraLanes { get; set; } = 1;

        // 100m
        public int InitialBariera170Lanes { get; set; } = 0;
        public int AfterSwitchBariera170Lanes { get; set; } = 1;

        public int InitialBariera200Lanes { get; set; } = 0;
        public int AfterSwitchBariera200Lanes { get; set; } = 1;

        public string SwitchRule { get; set; } =
            "BarieraRemainingEqualsBrevnoRemainingDivRemainingLanes"; // <-- musí sedět na enum
    }
}
