using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters
{
    public sealed class CategoryCatalog
    {
        private readonly Dictionary<string, Category> _byCode;

        public CategoryCatalog(IEnumerable<Category> categories)
        {
            _byCode = categories.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        }

        public bool TryGetByCode(string code, out Category category)
            => _byCode.TryGetValue(code?.Trim() ?? "", out category!);

        public Category GetByCodeOrThrow(string code)
        {
            if (TryGetByCode(code, out var cat))
                return cat;

            throw new InvalidOperationException(
                $"Unknown category code '{code}'. Add it to CategoryCatalog or fix mapping.");
        }
    }
}
