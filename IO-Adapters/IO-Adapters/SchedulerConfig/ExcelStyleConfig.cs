using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.SchedulerConfig
{
    public sealed class ExcelStyleConfig
    {
        public int TotalLanes { get; init; }          // obvykle z TrackPlan.TotalLanes
        public IReadOnlyDictionary<CategoryKey, (int r, int g, int b)> CategoryColors { get; init; }
            = new Dictionary<CategoryKey, (int, int, int)>();

        public IReadOnlyDictionary<int, (int r, int g, int b)> LaneStartColors { get; init; }
            = new Dictionary<int, (int, int, int)>();

        public (int r, int g, int b) DefaultCategoryColor { get; init; } = (230, 230, 230);
        public (int r, int g, int b) DefaultLaneStartColor { get; init; } = (255, 255, 255);
    }
}
