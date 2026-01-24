using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping
{
    public sealed class CategoryDto
    {
        public string Name { get; set; } = "";
        public string Sex { get; set; } = "";
        public string AgeGroup { get; set; } = "";
        public string SubGroup { get; set; } = "";
        public string ObstacleType { get; set; } = "";
        public string Discipline { get; set; } = "";
        public bool SeedByPerformance { get; set; }
    }
}
