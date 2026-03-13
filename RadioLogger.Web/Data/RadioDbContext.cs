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
        public DbSet<RegisteredStation> RegisteredStations => Set<RegisteredStation>();
        public DbSet<License> Licenses => Set<License>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IncidentLog>().HasKey(e => e.Id);
            modelBuilder.Entity<IncidentLog>().Property(e => e.StationName).IsRequired();
            modelBuilder.Entity<IncidentLog>().Property(e => e.EventType).HasMaxLength(20);

            modelBuilder.Entity<RegisteredStation>().HasKey(e => e.Id);
            modelBuilder.Entity<RegisteredStation>().HasIndex(e => new { e.MachineId, e.StationName }).IsUnique();

            modelBuilder.Entity<License>().HasKey(e => e.Id);
            modelBuilder.Entity<License>().HasIndex(e => e.Key).IsUnique();
        }
    }
}
