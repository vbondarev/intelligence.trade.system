using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

public sealed class TradeSystemDbContext(DbContextOptions<TradeSystemDbContext> options)
    : DbContext(options)
{
}
