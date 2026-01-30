using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling.Config
{
    public sealed class TrackPlan
    {
        public int TotalLanes { get; init; } = 3;
        public int InitialBarieraLanes { get; init; } = 0;
        public int AfterSwitchBarieraLanes { get; init; } = 1;
        public int InitialBariera170Lanes { get; init; } = 0;
        public int AfterSwitchBariera170Lanes { get; init; } = 1;
        public int InitialBariera200Lanes { get; init; } = 0;
        public int AfterSwitchBariera200Lanes { get; init; } = 1;
        public SwitchRuleType SwitchRule { get; init; } = SwitchRuleType.Automatic;
    }
}
