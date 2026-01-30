using StartList_Core.Models;
using StartList_Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling.Config
{
    public sealed class SchedulerConfig
    {
        public IReadOnlyList<CategoryKey> CategoryOrder { get; init; } = Array.Empty<CategoryKey>();
        public SchedulingRules Rules { get; init; } = new SchedulingRules();
        public TrackPlan TrackPlan { get; init; } = new();
        public StartNumberMode StartNumberMode { get; init; } = StartNumberMode.PerHeat;
    }
}
