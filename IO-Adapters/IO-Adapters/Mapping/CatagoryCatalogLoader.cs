using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace IO_Adapters.Mapping
{
    public static class CategoryCatalogLoader
    {
        public static CategoryCatalog Load(
            string path,
            Func<AgeGroup, SexEnum, SubGroup, string> codeBuilder, Discipline? discipline)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(path);

            var json = File.ReadAllText(path);

            var dto = JsonSerializer.Deserialize<CategoryCatalogDto>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? throw new InvalidOperationException("Invalid category JSON");

            static T ParseEnum<T>(string value) where T : struct
            {
                if (!Enum.TryParse<T>(value, true, out var result))
                    throw new InvalidOperationException(
                        $"Invalid value '{value}' for enum {typeof(T).Name}");
                return result;
            }

            var categories = dto.Categories.Select(c =>
                new Category(
                    name: c.Name,
                    sex: ParseEnum<SexEnum>(c.Sex),
                    ageGroup: ParseEnum<AgeGroup>(c.AgeGroup),
                    subGroup: ParseEnum<SubGroup>(c.SubGroup),
                    obstacleType: ParseEnum<ObstacleType>(c.ObstacleType),
                    discipline: ParseEnum<Discipline>(c.Discipline),
                    seedByPerformance: c.SeedByPerformance,
                    codeBuilder: codeBuilder
                )
            ).ToList();

            return new CategoryCatalog(discipline.HasValue ? categories.Where(x=>x.Discipline.Equals(discipline.Value)) : categories);
        }
    }
}
