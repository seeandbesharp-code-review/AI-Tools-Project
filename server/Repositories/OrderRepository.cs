using DTOs;
using Entities;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Repositories
{
    public class OrderRepository : IOrderRepository
    {
        ShowsCenterContext _context;
        public OrderRepository(ShowsCenterContext ShowsCenterContext)
        {
            _context = ShowsCenterContext;
        }
        public async Task<List<Order>> getAllOrders()
        {
            return await _context.Orders.Include(i => i.User)
                .Include(c => c.OrderedSeats).Take(100).ToListAsync();
        }
        public async Task<List<Order>> getOrdersForUser(int userId)
        {
            return await _context.Orders
                .Include(i => i.User)
                .Include(c => c.OrderedSeats)
                .Include(c => c.OrderedSeats).ThenInclude(s => s.Section)
                .Where(u => u.UserId == userId).Take(100).ToListAsync();
        }

        public async Task<Order> getOrderById(int id)
        {
            return await _context.Orders
                .Include(i => i.User)
                .Include(c => c.OrderedSeats)
                .FirstOrDefaultAsync(o => o.Id == id);
        }
        public async Task<Order> addOrder(Order order)
        {
            await _context.Orders.AddAsync(order);
            await _context.SaveChangesAsync();
            if (await getOrderById(order.Id) != null)
                return order;
            else
                return null;
        }

        public async Task<Order> updateOrder(Order order)
        {
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
            return order;
        }


        public async Task<Order?> getOrderByOrderesSeatId(int seatId)
        {
            OrderedSeat ordSeat = await _context.OrderedSeats.FirstOrDefaultAsync(s => s.Id == seatId);
            Order order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == ordSeat.OrderId);
            if (order == null)
                return null;
            return order;
        }

        public async Task<Order> Checkout(CheckoutDTO orderToUpdate)
        {
            List<Order> ordForUser = await getOrdersForUser(orderToUpdate.UserId);
            Order ord = ordForUser.FirstOrDefault(o => o.OrderedSeats.Any(s => s.Status == 1));
            if (ord == null)
            {
                return null; 
            }
            decimal? sum = ord.OrderedSeats.Sum(o => o.Section.Price);
            ord.Price = (double)sum;
            ord.OrderDate = DateTime.Now;
            foreach (var item in ord.OrderedSeats)
            {
                item.Status = 2;
                _context.OrderedSeats.Update(item);
            }

            _context.Orders.Update(ord);
            await _context.SaveChangesAsync();
            return ord;
        }

        public async Task<int> unLockSeat(int id)
        {
            OrderedSeat ord = await _context.OrderedSeats.FirstOrDefaultAsync(u => u.Id == id);
            _context.OrderedSeats.Remove(ord);
            int rows = await _context.SaveChangesAsync();
            return rows;
        }
        public async Task<OrderedSeat> addOrderedSeat(OrderedSeat orderedSeat)
        {
            var exists = await _context.OrderedSeats
                .AsNoTracking()
                .AnyAsync(s =>
                    s.ShowId == orderedSeat.ShowId 
                    && s.Row == orderedSeat.Row 
                    && s.Col == orderedSeat.Col 
                    && s.SectionId == orderedSeat.SectionId 
                    && s.Status != 0
                );
            if (exists) throw new InvalidOperationException("Seat already taken");
            await _context.OrderedSeats.AddAsync(orderedSeat);
            Section s = await _context.Sections.FirstOrDefaultAsync(o => o.Id == orderedSeat.SectionId);
            orderedSeat.ShowId = s.ShowId;
            await _context.SaveChangesAsync();
            Order order = await getOrderByOrderesSeatId(orderedSeat.Id);
            return order.OrderedSeats.FirstOrDefault(o => o.Id == orderedSeat.Id);
        }
        //public async Task deleteOrder(int id)
        //{
        //    await _context.Orders.ExecuteDeleteAsync(await .getOrderById(id));
        //    await _context.SaveChangesAsync();
        //}
        public async Task<List<OrderedSeat>> getOrderedSeatsByShowId(int showId)
        {
            return await _context.OrderedSeats
                .Include(s=>s.Show)
                .Include(s=>s.Section)
                .Include(s => s.Order)
                .ThenInclude(u=>u.User).Where(s => s.ShowId == showId).Take(100).ToListAsync();
        }

        public async Task<List<OrderedSeat>> getOrderedSeatsByUserId(int userId)
        {
            return await _context.OrderedSeats
                .Include(s => s.Show)
                .Include(s => s.Section)
                .Include(s => s.Order)
                .Where(s=>s.Order.UserId == userId).Take(100).ToListAsync();

        }
    }
}
