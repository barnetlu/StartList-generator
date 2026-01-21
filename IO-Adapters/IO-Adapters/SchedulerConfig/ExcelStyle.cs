using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.SchedulerConfig
{
    public sealed class ExcelStyle
    {
        public IReadOnlyDictionary<CategoryKey, (int r, int g, int b)> CategoryColors { get; init; }
            = new Dictionary<CategoryKey, (int, int, int)>();

        public IReadOnlyDictionary<int, (int r, int g, int b)> LaneStartColors { get; init; }
            = new Dictionary<int, (int, int, int)>();
    }
}
