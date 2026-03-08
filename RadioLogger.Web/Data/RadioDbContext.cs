using Microsoft.EntityFrameworkCore;
using RadioLogger.Shared.Models;

namespace RadioLogger.Web.Data
{
    public class RadioDbContext : DbContext
    {
        public RadioDbContext(DbContextOptions<RadioDbContext> options) : base(options)
        {
        }

        public DbSet<IncidentLog> Incidents => Set<IncidentLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IncidentLog>().HasKey(e => e.Id);
            modelBuilder.Entity<IncidentLog>().Property(e => e.StationName).IsRequired();
            modelBuilder.Entity<IncidentLog>().Property(e => e.EventType).HasMaxLength(20);
        }
    }
}
