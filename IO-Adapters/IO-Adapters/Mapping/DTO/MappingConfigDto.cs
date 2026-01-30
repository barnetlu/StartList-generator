using IO_Adapters.SchedulerConfig.DTO;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping.DTO
{
    public sealed class MappingConfigDto
    {
        public List<RuleDto> GroupRules { get; set; } = new();
        public List<RuleDto> SexRules { get; set; } = new();
        public List<RuleDto> SubgroupRules { get; set; } = new();


        public List<string> NoSexIfGroup { get; set; } = new();
        public List<string> NoSubGroupIfGroup { get; set; } = new();


        public List<AgeRuleDto> AgeRules { get; set; } = new();

        public AgeFallbackDto? FallbackOlder { get; set; } = new();
        public AgeFallbackDto? FallbackYounger { get; set; } = new();
    }
}
