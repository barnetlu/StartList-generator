using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IO_Adapters.Mapping
{
    using global::IO_Adapters.Mapping.DTO;
    using StartList_Core.Models.Enums;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    namespace IO_Adapters.Mapping
    {

        public sealed class CategoryMappingConfig
        {
            public required IReadOnlyList<MapRule<AgeGroup>> GroupRules { get; init; }
            public required IReadOnlyList<MapRule<SexEnum>> SexRules { get; init; }
            public required IReadOnlyList<MapRule<SubGroup>> SubgroupRules { get; init; }

            public required IReadOnlySet<AgeGroup> NoSexIfGroup { get; init; }
            public required IReadOnlySet<AgeGroup> NoSubGroupIfGroup { get; init; }

            /// <summary>
            /// Map BirthYear -> (Group, SubGroup). SubGroup is typically used for Dorost.
            /// </summary>
            public required IReadOnlyDictionary<int, (AgeGroup Group, SubGroup SubGroup)> AgeYearMap { get; init; }

            public required (AgeGroup Group, SubGroup SubGroup) FallbackOlder { get; init; }
            public required (AgeGroup Group, SubGroup SubGroup) FallbackYounger { get; init; }

            public required int? MinAgeRuleYear { get; init; }
            public required int? MaxAgeRuleYear { get; init; }

            /// <summary>
            /// Expected group/subgroup from BirthYear using AgeRules.
            /// Fallback:
            /// - older than min defined year -> Dospeli
            /// - younger than max defined year -> Pripravka
            /// - gap in middle -> Dospeli (conservative)
            /// </summary>
            public (AgeGroup Group, SubGroup SubGroup) GetExpectedByBirthYear(int birthYear)
            {
                if (AgeYearMap.TryGetValue(birthYear, out var r))
                    return r;

                if (MinAgeRuleYear.HasValue && birthYear < MinAgeRuleYear.Value)
                    return FallbackOlder;

                if (MaxAgeRuleYear.HasValue && birthYear > MaxAgeRuleYear.Value)
                    return FallbackYounger;

                // year not covered but between min/max -> a "hole" in config
                return FallbackOlder;
            }
        }

        // ----------------------------
        // Loader
        // ----------------------------

        public static class MappingConfigLoader
        {
            public static CategoryMappingConfig Load(string jsonPath)
            {
                if (!File.Exists(jsonPath))
                    throw new FileNotFoundException("Mapping config JSON not found.", jsonPath);

                return LoadFromJson(File.ReadAllText(jsonPath));
            }

            public static CategoryMappingConfig LoadFromJson(string json)
            {

                var dto = JsonSerializer.Deserialize<MappingConfigDto>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                        NumberHandling = JsonNumberHandling.Strict
                    }
                ) ?? throw new InvalidOperationException("Failed to parse mapping JSON.");

                static T ParseEnum<T>(string value) where T : struct
                {
                    if (!Enum.TryParse<T>(value, ignoreCase: true, out var result))
                        throw new InvalidOperationException($"Invalid enum value '{value}' for {typeof(T).Name}.");
                    return result;
                }

                static string[] NormPatterns(IEnumerable<string> patterns)
                    => patterns
                        .Select(TextNorm.Normalize)
                        .Where(p => p.Length > 0)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();

                static (AgeGroup, SubGroup) ParseFallback(AgeFallbackDto dto)
                {
                    var group = ParseEnum<AgeGroup>(dto.Group);
                    var sub = string.IsNullOrWhiteSpace(dto.SubGroup)
                    ? SubGroup.None
                    : ParseEnum<SubGroup>(dto.SubGroup);


                    return (group, sub);
                }

                // ------------------------
                // Compile rules
                // ------------------------

                var groupRules = dto.GroupRules
                        .OrderByDescending(r => r.Priority)
                        .Select(r => new MapRule<AgeGroup>(
                            Patterns: NormPatterns(r.Patterns),
                            Value: ParseEnum<AgeGroup>(r.Value),
                            Priority: r.Priority,
                            ExactOnly: r.ExactOnly
                        // ExactOnly pokud MapRule podporuje: přidej parametr / vlastnost
                        ))
                        .ToList();

                var sexRules = dto.SexRules
                    .OrderByDescending(r => r.Priority)
                    .Select(r => new MapRule<SexEnum>(
                        Patterns: NormPatterns(r.Patterns),
                        Value: ParseEnum<SexEnum>(r.Value),
                        Priority: r.Priority,
                        ExactOnly: r.ExactOnly
                    ))
                    .ToList();

                var subgroupRules = dto.SubgroupRules
                    .OrderByDescending(r => r.Priority)
                    .Select(r => new MapRule<SubGroup>(
                        Patterns: NormPatterns(r.Patterns),
                        Value: ParseEnum<SubGroup>(r.Value),
                        Priority: r.Priority,
                        ExactOnly: r.ExactOnly
                    ))
                    .ToList();

                var noSex = new HashSet<AgeGroup>(dto.NoSexIfGroup.Select(ParseEnum<AgeGroup>));
                var noSub = new HashSet<AgeGroup>(dto.NoSubGroupIfGroup.Select(ParseEnum<AgeGroup>));

                // ------------------------
                // Compile AgeRules
                // ------------------------

                var fallbackOlder = dto.FallbackOlder != null
                    ? ParseFallback(dto.FallbackOlder)
                    : throw new InvalidOperationException("fallbackOlder missing in mapping config.");


                var fallbackYounger = dto.FallbackYounger != null
                    ? ParseFallback(dto.FallbackYounger)
                    : throw new InvalidOperationException("fallbackYounger missing in mapping config.");

                var ageYearMap = new Dictionary<int, (AgeGroup Group, SubGroup SubGroup)>();
                var yearsAll = new List<int>();

                foreach (var ar in dto.AgeRules ?? new List<AgeRuleDto>())
                {
                    if (ar.Years is null || ar.Years.Count == 0)
                        continue;

                    var group = ParseEnum<AgeGroup>(ar.Group);

                    // subGroup may be empty for groups that don't use it
                    var subGroup = string.IsNullOrWhiteSpace(ar.SubGroup)
                        ? SubGroup.None
                        : ParseEnum<SubGroup>(ar.SubGroup);

                    foreach (var y in ar.Years.Distinct())
                    {
                        if (ageYearMap.ContainsKey(y))
                            throw new InvalidOperationException($"BirthYear {y} je definovaný ve více AgeRules (duplicitní ročník).");

                        ageYearMap[y] = (group, subGroup);
                        yearsAll.Add(y);
                    }
                }



                int? minYear = yearsAll.Count > 0 ? yearsAll.Min() : null;
                int? maxYear = yearsAll.Count > 0 ? yearsAll.Max() : null;

                // ------------------------
                // Done
                // ------------------------

                return new CategoryMappingConfig
                {
                    GroupRules = groupRules,
                    SexRules = sexRules,
                    SubgroupRules = subgroupRules,
                    NoSexIfGroup = noSex,
                    NoSubGroupIfGroup = noSub,

                    AgeYearMap = ageYearMap,
                    MinAgeRuleYear = minYear,
                    MaxAgeRuleYear = maxYear,
                    FallbackOlder = fallbackOlder,
                    FallbackYounger = fallbackYounger
                };
            }
        }
    }        
}