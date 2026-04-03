using StartList_Core.Models;
using System.Text.Json;
using StartList_Core.Scheduling;
using StartList_Core.Scheduling.Config;
using StartList_Core.Models.Enums;
using IO_Adapters.SchedulerConfig.DTO;

namespace IO_Adapters.SchedulerConfig
{
    public static class SchedulerConfigLoader
    {
        public static StartList_Core.Scheduling.Config.SchedulerConfig LoadSchedulerConfig(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Scheduler config path is empty.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("Scheduler config JSON not found.", path);

            return LoadFromJson(File.ReadAllText(path));
        }

        public static StartList_Core.Scheduling.Config.SchedulerConfig LoadFromJson(string json)
        {

            var dto = JsonSerializer.Deserialize<SchedulerConfigDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Failed to parse scheduler config JSON.");

            static T ParseEnum<T>(string value) where T : struct
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"Empty enum value for {typeof(T).Name}.");

                if (!Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var result))
                    throw new InvalidOperationException($"Invalid value '{value}' for enum {typeof(T).Name}.");

                return result;
            }

            // ---- categoryOrder ----
            var order = new List<CategoryKey>(dto.CategoryOrder.Count);
            for (int i = 0; i < dto.CategoryOrder.Count; i++)
            {
                var item = dto.CategoryOrder[i];

                var age = ParseEnum<AgeGroup>(item.AgeGroup);
                var sex = ParseEnum<SexEnum>(item.Sex);
                var sub = ParseEnum<SubGroup>(item.SubGroup);

                order.Add(new CategoryKey(age, sex, sub));
            }

            // Volitelně: detekce duplicit
            var dup = order.GroupBy(x => x).FirstOrDefault(g => g.Count() > 1);
            if (dup is not null)
                throw new InvalidOperationException($"Duplicate categoryOrder entry in scheduler config: {dup.Key}");

            // ---- rules ----
            var rulesDto = dto.Rules ?? new SchedulerRulesDto();

            var rules = new SchedulingRules
            {
                MaxOneClubPerHeat = rulesDto.MaxOneClubPerHeat,
                ClubCooldownHeats = Math.Max(1, rulesDto.ClubCooldownHeats) // 1 = žádný cooldown
            };

            var tpDto = dto.TrackPlan ?? new TrackPlanDto();

            var trackPlan = new TrackPlan
            {
                TotalLanes = Math.Max(1, tpDto.TotalLanes),

                // 60m legacy
                InitialBarieraLanes = Math.Max(0, tpDto.InitialBarieraLanes),
                AfterSwitchBarieraLanes = Math.Max(0, tpDto.AfterSwitchBarieraLanes),

                // 100m
                InitialBariera170Lanes = Math.Max(0, tpDto.InitialBariera170Lanes),
                AfterSwitchBariera170Lanes = Math.Max(0, tpDto.AfterSwitchBariera170Lanes),

                InitialBariera200Lanes = Math.Max(0, tpDto.InitialBariera200Lanes),
                AfterSwitchBariera200Lanes = Math.Max(0, tpDto.AfterSwitchBariera200Lanes),

                SwitchRule = ParseEnum<SwitchRuleType>(tpDto.SwitchRule)
            };

            // sanity: pro 60m kontroluj legacy (Barrier150)
            if (trackPlan.InitialBarieraLanes > trackPlan.TotalLanes)
                throw new InvalidOperationException("initialBarieraLanes > totalLanes");
            if (trackPlan.AfterSwitchBarieraLanes > trackPlan.TotalLanes)
                throw new InvalidOperationException("afterSwitchBarieraLanes > totalLanes");

            // sanity: pro 100m kontroluj součet 170+200 (Crossbar = zbytek)
            int init100 = trackPlan.InitialBariera170Lanes + trackPlan.InitialBariera200Lanes;
            int after100 = trackPlan.AfterSwitchBariera170Lanes + trackPlan.AfterSwitchBariera200Lanes;

            if (init100 > trackPlan.TotalLanes)
                throw new InvalidOperationException("initialBariera170Lanes + initialBariera200Lanes > totalLanes");
            if (after100 > trackPlan.TotalLanes)
                throw new InvalidOperationException("afterSwitchBariera170Lanes + afterSwitchBariera200Lanes > totalLanes");
            var startNoMode = ParseEnum<StartNumberMode>(dto.StartNumberMode);

            return new StartList_Core.Scheduling.Config.SchedulerConfig { CategoryOrder = order, Rules = rules, TrackPlan = trackPlan, StartNumberMode = startNoMode };
        }
    }
}