using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.SchedulerConfig.DTO
{
    public sealed class SchedulerConfigDto
    {
        public List<CategoryKeyDto> CategoryOrder { get; set; } = new();
        public SchedulerRulesDto Rules { get; set; } = new();
        public TrackPlanDto TrackPlan { get; set; } = new();
        public ExcelStyleDto ExcelStyle { get; set; } = new();
        public string StartNumberMode { get; set; } = "PerHeat";
    }
}
