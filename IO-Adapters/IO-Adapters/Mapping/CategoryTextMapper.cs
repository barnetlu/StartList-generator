using IO_Adapters.Interfaces;
using IO_Adapters.Mapping.IO_Adapters.Mapping;
using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping
{
    public sealed class CategoryTextMapper
    {
        private readonly CategoryMappingConfig _cfg;

        public CategoryTextMapper(CategoryMappingConfig cfg)
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

        /// <summary>
        /// Maps category text and validates group against birth year using AgeRules.
        /// If mismatch occurs, it is resolved immediately via resolver.
        /// </summary>
        public (AgeGroup Group, SexEnum Sex, SubGroup Subgroup) Map(string raw, int? birthYear, CompetitorDraft draft, IInteractiveResolver resolver, bool resolveWhenGroupUnknown = true)
        {
            // 1) text mapping
            var (group, sex, sub) = Map(raw);


            // 2) ensure birth year (if required)
            if (!birthYear.HasValue)
            {
                // birthRaw můžeš mít uložené třeba v draftu nebo si ho předej navíc
                // tady nechávám null – ty to typicky budeš volat až po parsování.
                birthYear = resolver.ResolveBirthYear(draft, birthRaw: null);
            }


            // 3) expected group by year
            var expected = _cfg.GetExpectedByBirthYear(birthYear.Value);


            // 4) mismatch handling
            bool canCompare = group != AgeGroup.Unknown || resolveWhenGroupUnknown;


            if (canCompare && (expected.Group != group || expected.SubGroup != sub))
            {
                var (g2, s2) = resolver.ResolveAgeGroupMismatch(draft, group, sub, expected.Group, expected.SubGroup, _cfg.NoSubGroupIfGroup);
                group = g2;
                sub = s2;


                // po změně group/sub znovu aplikuj noSex/noSub pravidla
                if (_cfg.NoSexIfGroup.Contains(group))
                    sex = SexEnum.Mixed;
                if (_cfg.NoSubGroupIfGroup.Contains(group))
                    sub = SubGroup.None;
            }

            // 5) Sex handling (independent of group mismatch)
            if (IsSexRequired(group))
            {
                // když mapper nedokázal určit nebo je to "slabé" určení (např. jen D/CH)
                var normTokens = TextNorm.Tokens(raw);
                bool looksAmbiguousSex =
                sex == SexEnum.Mixed ||
                // typicky "D", "CH" apod. – může být jen zkratka bez kontextu
                normTokens.Count == 1 && (normTokens[0] == "D" || normTokens[0] == "CH");


                if (looksAmbiguousSex)
                    sex = resolver.ResolveSex(draft, group);
            }


            return (group, sex, sub);
        }

        private static T? Pick<T>(IReadOnlyList<string> tokens, IReadOnlyList<MapRule<T>> rules) where T : struct
        {
            foreach (var rule in rules/*.OrderByDescending(r => r.Priority)*/)
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
