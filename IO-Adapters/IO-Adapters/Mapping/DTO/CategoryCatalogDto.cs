using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping.DTO
{
    public sealed class CategoryCatalogDto
    {
        public List<CategoryDto> Categories { get; set; } = new();
    }
}
