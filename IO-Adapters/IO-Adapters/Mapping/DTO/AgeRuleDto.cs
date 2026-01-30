using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping.DTO
{
    public class AgeRuleDto
    {
        public List<int> Years { get; set; } = new();
        public string Group { get; set; } = "";
        public string SubGroup { get; set; } = "";
    }
}
