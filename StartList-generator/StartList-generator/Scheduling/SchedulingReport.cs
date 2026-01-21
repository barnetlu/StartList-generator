using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling
{
    public sealed class SchedulingReport
    {
        public int? SwitchAtHeat { get; set; }

        public int EmptyLaneCount { get; set; }
        public List<EmptyLaneInfo> EmptyLanes { get; } = new();

        public int FallbackPickCount { get; set; }
        public List<FallbackPickInfo> FallbackPicks { get; } = new();

        public int SkippedBecauseDuplicateInHeat { get; set; }
        public int SkippedBecauseCooldown { get; set; }

        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
    }
}
