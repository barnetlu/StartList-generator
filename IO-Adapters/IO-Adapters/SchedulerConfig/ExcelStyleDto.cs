using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.SchedulerConfig
{
    public sealed class ExcelStyleDto
    {
        public List<CategoryColorDto> CategoryColors { get; set; } = new();
        public List<LaneColorDto> LaneStartColors { get; set; } = new();
        public int[]? DefaultCategoryRgb { get; set; }
        public int[]? DefaultLaneStartRgb { get; set; }
    }

    public sealed class CategoryColorDto
    {
        public string AgeGroup { get; set; } = "";
        public string Sex { get; set; } = "";
        public string SubGroup { get; set; } = "None";
        public int[] Rgb { get; set; } = new[] { 230, 230, 230 };
    }

    public sealed class LaneColorDto
    {
        public int Lane { get; set; }
        public int[] Rgb { get; set; } = new[] { 255, 255, 255 };
    }

    public sealed class ExcelStyleRootDto
    {
        public ExcelStyleDto ExcelStyle { get; set; } = new();
    }
}
