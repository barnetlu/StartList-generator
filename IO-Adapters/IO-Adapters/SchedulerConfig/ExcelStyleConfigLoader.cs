using IO_Adapters.SchedulerConfig.DTO;
using StartList_Core.Models;
using StartList_Core.Models.Enums;
using StartList_Core.Scheduling;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace IO_Adapters.SchedulerConfig
{
    public static class ExcelStyleConfigLoader
    {
        public static ExcelStyleConfig Load(string jsonPath, int totalLanes)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                throw new ArgumentException("Config path is empty.", nameof(jsonPath));
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Config JSON not found.", jsonPath);
            if (totalLanes <= 0)
                throw new ArgumentOutOfRangeException(nameof(totalLanes), "totalLanes must be > 0.");

            var json = File.ReadAllText(jsonPath);

            // načteme jen excelStyle část (může být v tom samém jsonu jako scheduler config)
            var root = JsonSerializer.Deserialize<ExcelStyleRootDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ExcelStyleRootDto();

            var dto = root.ExcelStyle ?? new ExcelStyleDto();

            var defaultCat = dto.DefaultCategoryRgb is not null
                ? ParseRgb(dto.DefaultCategoryRgb, "excelStyle.defaultCategoryRgb")
                : (230, 230, 230);

            var defaultLane = dto.DefaultLaneStartRgb is not null
                ? ParseRgb(dto.DefaultLaneStartRgb, "excelStyle.defaultLaneStartRgb")
                : (255, 255, 255);

            // ---- category colors ----
            var catColors = new Dictionary<CategoryKey, (int r, int g, int b)>();
            foreach (var item in dto.CategoryColors ?? new List<CategoryColorDto>())
            {
                var key = new CategoryKey(
                    ParseEnum<AgeGroup>(item.AgeGroup, "excelStyle.categoryColors[].ageGroup"),
                    ParseEnum<SexEnum>(item.Sex, "excelStyle.categoryColors[].sex"),
                    ParseEnum<SubGroup>(item.SubGroup, "excelStyle.categoryColors[].subGroup")
                );

                if (catColors.ContainsKey(key))
                    throw new InvalidOperationException($"Duplicate category color mapping for key: {key}");

                catColors[key] = ParseRgb(item.Rgb, $"excelStyle.categoryColors[{item.AgeGroup},{item.Sex},{item.SubGroup}].rgb");
            }

            // ---- lane start colors ----
            var laneColors = new Dictionary<int, (int r, int g, int b)>();
            foreach (var item in dto.LaneStartColors ?? new List<LaneColorDto>())
            {
                if (item.Lane <= totalLanes)
                {
                    if (item.Lane <= 0)
                        throw new InvalidOperationException($"Invalid lane={item.Lane} in excelStyle.laneStartColors (totalLanes={totalLanes}).");

                    if (laneColors.ContainsKey(item.Lane))
                        throw new InvalidOperationException($"Duplicate lane start color mapping for lane {item.Lane}.");

                    laneColors[item.Lane] = ParseRgb(item.Rgb, $"excelStyle.laneStartColors[lane={item.Lane}].rgb");
                }
            }

            // doplň defaulty pro všechny dráhy, aby writer nemusel řešit TryGetValue
            for (int lane = 1; lane <= totalLanes; lane++)
            {
                if (!laneColors.ContainsKey(lane))
                    laneColors[lane] = defaultLane;
            }

            return new ExcelStyleConfig
            {
                TotalLanes = totalLanes,
                CategoryColors = catColors,
                LaneStartColors = laneColors,
                DefaultCategoryColor = defaultCat,
                DefaultLaneStartColor = defaultLane
            };
        }

        private static (int r, int g, int b) ParseRgb(int[] rgb, string context)
        {
            if (rgb is null || rgb.Length != 3)
                throw new InvalidOperationException($"RGB must have exactly 3 integers: {context}");

            static int Clamp(int x) => x < 0 ? 0 : (x > 255 ? 255 : x);
            return (Clamp(rgb[0]), Clamp(rgb[1]), Clamp(rgb[2]));
        }

        private static T ParseEnum<T>(string value, string context) where T : struct
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Empty enum value at {context}.");

            if (!Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var result))
                throw new InvalidOperationException($"Invalid value '{value}' for enum {typeof(T).Name} at {context}.");

            return result;
        }
    }
}
