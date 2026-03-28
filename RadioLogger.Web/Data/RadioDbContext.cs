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
        public DbSet<LogEntry> LogEntries => Set<LogEntry>();
        public DbSet<AppUser> Users => Set<AppUser>();
        public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IncidentLog>().HasKey(e => e.Id);
            modelBuilder.Entity<IncidentLog>().Property(e => e.StationName).IsRequired();
            modelBuilder.Entity<IncidentLog>().Property(e => e.EventType).HasMaxLength(20);

            modelBuilder.Entity<RegisteredStation>().HasKey(e => e.Id);
            modelBuilder.Entity<RegisteredStation>().HasIndex(e => new { e.MachineId, e.HardwareName }).IsUnique();

            modelBuilder.Entity<License>().HasKey(e => e.Id);
            modelBuilder.Entity<License>().HasIndex(e => e.Key).IsUnique();
            modelBuilder.Entity<License>().Property(e => e.LicenseType).HasMaxLength(20);
            modelBuilder.Entity<License>().Property(e => e.MachineId).HasMaxLength(100);
            modelBuilder.Entity<License>().Property(e => e.HardwareId).HasMaxLength(200);
            modelBuilder.Entity<License>().HasIndex(e => e.MachineId);

            modelBuilder.Entity<AppUser>().HasKey(e => e.Id);
            modelBuilder.Entity<AppUser>().HasIndex(e => e.Username).IsUnique();
            modelBuilder.Entity<AppUser>().Property(e => e.Username).HasMaxLength(100).IsRequired();
            modelBuilder.Entity<AppUser>().Property(e => e.Role).HasMaxLength(20).IsRequired();

            modelBuilder.Entity<AuditEntry>().HasKey(e => e.Id);
            modelBuilder.Entity<AuditEntry>().HasIndex(e => e.Timestamp);
            modelBuilder.Entity<AuditEntry>().Property(e => e.Username).HasMaxLength(100);
            modelBuilder.Entity<AuditEntry>().Property(e => e.Action).HasMaxLength(50);

            modelBuilder.Entity<LogEntry>().HasKey(e => e.Id);
            modelBuilder.Entity<LogEntry>().Property(e => e.MachineId).HasMaxLength(100).IsRequired();
            modelBuilder.Entity<LogEntry>().Property(e => e.Level).HasMaxLength(10).IsRequired();
            modelBuilder.Entity<LogEntry>().Property(e => e.Source).HasMaxLength(200);
            modelBuilder.Entity<LogEntry>().HasIndex(e => new { e.MachineId, e.Timestamp });
        }
    }
}
