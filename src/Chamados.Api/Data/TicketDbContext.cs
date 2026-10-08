using Chamados.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace Chamados.Api.Data;
public sealed class TicketDbContext(DbContextOptions<TicketDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketEvent> Events => Set<TicketEvent>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Ticket>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<Ticket>().HasIndex(x => new { x.Status, x.CreatedAtUtc });
        model.Entity<TicketEvent>().HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId);
    }
}
