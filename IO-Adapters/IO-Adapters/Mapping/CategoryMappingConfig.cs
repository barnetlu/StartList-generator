using System;
using System.Collections.Generic;
using System.Text;
using StartList_Core.Models;

namespace IO_Adapters.Mapping
{
    using StartList_Core.Models;
    using System.Text.Json;

    public sealed class MappingConfig
    {
        public IReadOnlyList<MapRule<AgeGroup>> GroupRules { get; init; } = Array.Empty<MapRule<AgeGroup>>();
        public IReadOnlyList<MapRule<SexEnum>> SexRules { get; init; } = Array.Empty<MapRule<SexEnum>>();
        public IReadOnlyList<MapRule<SubGroup>> SubgroupRules { get; init; } = Array.Empty<MapRule<SubGroup>>();
        public IReadOnlySet<AgeGroup> NoSexIfGroup { get; init; } = new HashSet<AgeGroup>();
        public IReadOnlySet<AgeGroup> NoSubGroupIfGroup { get; init; } = new HashSet<AgeGroup>();
    }

    public static class MappingConfigLoader
    {
        public static MappingConfig Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Mapping config JSON not found.", jsonPath);

            var json = File.ReadAllText(jsonPath);

            var dto = JsonSerializer.Deserialize<MappingConfigDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Failed to parse mapping JSON.");

            static T ParseEnum<T>(string value) where T : struct
            {
                if (!Enum.TryParse<T>(value, ignoreCase: true, out var result))
                    throw new InvalidOperationException($"Invalid enum value '{value}' for {typeof(T).Name}.");
                return result;
            }

            static string[] NormPatterns(IEnumerable<string> patterns) =>
                patterns.Select(TextNorm.Normalize).Where(p => p.Length > 0).ToArray();

            return new MappingConfig
            {
                GroupRules = dto.GroupRules
                    .OrderByDescending(r => r.Priority)
                    .Select(r => new MapRule<AgeGroup>(NormPatterns(r.Patterns), ParseEnum<AgeGroup>(r.Value), r.Priority))
                    .ToList(),

                SexRules = dto.SexRules
                    .OrderByDescending(r => r.Priority)
                    .Select(r => new MapRule<SexEnum>(NormPatterns(r.Patterns), ParseEnum<SexEnum>(r.Value), r.Priority))
                    .ToList(),

                SubgroupRules = dto.SubgroupRules
                    .OrderByDescending(r => r.Priority)
                    .Select(r => new MapRule<SubGroup>(NormPatterns(r.Patterns), ParseEnum<SubGroup>(r.Value), r.Priority))
                    .ToList(),

                NoSexIfGroup = new HashSet<AgeGroup>(
                    dto.NoSexIfGroup.Select(ParseEnum<AgeGroup>)
                ),
                NoSubGroupIfGroup = new HashSet<AgeGroup>(
                    dto.NoSubGroupIfGroup.Select(ParseEnum<AgeGroup>)
                )

            };
        }
    }

    

    
}
