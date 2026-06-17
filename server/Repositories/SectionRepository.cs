using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities;
using Microsoft.EntityFrameworkCore;

namespace Repositories
{
    public class SectionRepository : ISectionRepository
    {
        ShowsCenterContext _context;
        public SectionRepository(ShowsCenterContext ShowsCenterContext)
        {
            _context = ShowsCenterContext;
        }
        public async Task<List<Section>> getSectionsByShowId(int showId)
        {
            return await _context.Sections.Where(s => s.ShowId == showId).Take(100).ToListAsync();
        }
        public async Task<Section> addSection(Section section)
        {
            await _context.Sections.AddAsync(section);
            await _context.SaveChangesAsync();
            if (getSectionsByShowId(section.ShowId) != null)
                return section;
            else
                return null;
        }
    }
}
