using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.SchedulerConfig.DTO
{
    public sealed class RuleDto
    {
        public List<string> Patterns { get; set; } = new();
        public string Value { get; set; } = "";
        public int Priority { get; set; } = 0;
        public bool ExactOnly { get; set; } = false;
    }
}
