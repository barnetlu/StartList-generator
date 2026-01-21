using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling
{
    public sealed class TrackPlan
    {
        public int TotalLanes { get; init; } = 3;
        public int InitialBarieraLanes { get; init; } = 0;
        public int AfterSwitchBarieraLanes { get; init; } = 1;
        public SwitchRuleType SwitchRule { get; init; } = SwitchRuleType.BarieraRemainingEqualsBrevnoRemainingDiv2;
    }
}
