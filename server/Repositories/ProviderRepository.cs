using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities;
using Microsoft.EntityFrameworkCore;

namespace Repositories
{
    public class ProviderRepository : IProviderRepository
    {
        ShowsCenterContext _context;
        public ProviderRepository(ShowsCenterContext ShowsCenterContext)
        {
            _context = ShowsCenterContext;
        }
        public async Task<Provider> getProviderById(int id)
        {
            return await _context.Providers.Include(s=>s.Shows).FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<List<Provider>> getAllProviders()
        {
            return await _context.Providers.Take(100).ToListAsync();
        }

        public async Task<Provider> addProvider(Provider provider)
        {
            await _context.Providers.AddAsync(provider);
            await _context.SaveChangesAsync();
            var saved = await getProviderById(provider.Id);
            return saved != null ? provider : null;
        }

        public async Task<bool> deleteProvider(int id)
        {
            Provider providerToDelete = await getProviderById(id);
            if (providerToDelete == null)
                return false;
            if (providerToDelete.Shows.Count() > 0)
                return false;
            _context.Providers.Remove(providerToDelete);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
