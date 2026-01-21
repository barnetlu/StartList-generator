using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling
{
    public sealed class SchedulerConfig
    {
        public IReadOnlyList<CategoryKey> CategoryOrder { get; init; } = Array.Empty<CategoryKey>();
        public SchedulingRules Rules { get; init; } = new SchedulingRules();
        public TrackPlan TrackPlan { get; init; } = new(); // 👈 NOVÉ
    }
}
