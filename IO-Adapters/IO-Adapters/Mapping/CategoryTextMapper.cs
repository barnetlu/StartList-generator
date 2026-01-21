using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping
{
    public sealed class CategoryTextMapper
    {
        private readonly MappingConfig _cfg;

        public CategoryTextMapper(MappingConfig cfg)
        {
            _cfg = cfg;
        }

        public (AgeGroup Group, SexEnum Sex, SubGroup Subgroup) Map(string raw)
        {
            var tokens = TextNorm.Tokens(raw);

            var group = Pick(tokens, _cfg.GroupRules) ?? AgeGroup.Unknown;
            var sex = Pick(tokens, _cfg.SexRules) ?? SexEnum.Mixed;
            var sub = Pick(tokens, _cfg.SubgroupRules) ?? SubGroup.None;

            if (_cfg.NoSexIfGroup.Contains(group))
                sex = SexEnum.Mixed;
            if (_cfg.NoSubGroupIfGroup.Contains(group))
                sub = SubGroup.None;

            return (group, sex, sub);
        }

        private static T? Pick<T>(IReadOnlyList<string> tokens, IReadOnlyList<MapRule<T>> rules) where T : struct
        {
            foreach (var rule in rules.OrderByDescending(r => r.Priority))
            {
                if (rule.Patterns.Any(p => Matches(tokens, p, rule.ExactOnly)))
                    return rule.Value;
            }
            return null;
        }

        private static bool Matches(IReadOnlyList<string> tokens, string pattern, bool exactOnly)
        {
            return exactOnly
                ? tokens.Any(t => t.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                : tokens.Any(t => t.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                                  t.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsSexRequired(AgeGroup group)
        => !_cfg.NoSexIfGroup.Contains(group);
    }

}
