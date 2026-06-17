using Entities;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Repositories
{
    public class RatingRepository : IRatingRepository
    {
        private readonly ShowsCenterContext _showsCenterContext;

        public RatingRepository(ShowsCenterContext showsCenterContext)
        {
            _showsCenterContext = showsCenterContext;
        }

        public async Task<Rating> AddRating(Rating newRating)
        {
            newRating.Referer ??= "Unknown";
            await _showsCenterContext.Ratings.AddAsync(newRating);
            await _showsCenterContext.SaveChangesAsync();
            return newRating;
        }

        public async Task<List<Rating>> GetAllRatings()
        {
            return await _showsCenterContext.Ratings.Take(100).ToListAsync();
        }
    }
}