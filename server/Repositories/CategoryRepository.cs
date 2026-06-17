using Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        ShowsCenterContext _context;
        public CategoryRepository(ShowsCenterContext ShowsCenterContext)
        {
            _context = ShowsCenterContext;
        }
        public async Task<List<Category>> getAllCategories()
        {
            return await _context.Categories.Take(100).ToListAsync();
        }
        public async Task<Category> getCategoryById(int id)
        {
            return await _context.Categories.FindAsync(id);
        }
        public async Task<Category> addCategory(Category category)
        {
            await _context.Categories.AddAsync(category);
            await _context.SaveChangesAsync();
            return category;
        }
        public async Task<int> Delete(int id)
        {
            var item = await _context.Categories.FindAsync(id);
            if (item.Shows.Count()>0) {
                return 0;
            }
            _context.Categories.Remove(item);
            return await _context.SaveChangesAsync();
        }

    }
}
