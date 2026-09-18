using ACA360.BusinessLogic.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;

        public MemoryCacheService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public void Remove(string key)
        {
            _cache.Remove(key);
        }

        public void ClearAll()
        {
            // Cast to MemoryCache to access internal keys
            if (_cache is MemoryCache memoryCache)
            {
                memoryCache.Compact(1.0); // Removes everything
            }
        }
    }
}
