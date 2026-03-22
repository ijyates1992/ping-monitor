using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Data;

public sealed class PingMonitorDbContext : DbContext
{
    public PingMonitorDbContext(DbContextOptions<PingMonitorDbContext> options)
        : base(options)
    {
    }

    public DbSet<Agent> Agents => Set<Agent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var agent = modelBuilder.Entity<Agent>();

        agent.ToTable("Agents");
        agent.HasKey(x => x.AgentId);
        agent.Property(x => x.AgentId).HasMaxLength(64);
        agent.Property(x => x.InstanceId).HasMaxLength(255).IsRequired();
        agent.Property(x => x.Name).HasMaxLength(255);
        agent.Property(x => x.Site).HasMaxLength(255);
        agent.Property(x => x.ApiKeyHash).IsRequired();
        agent.Property(x => x.AgentVersion).HasMaxLength(50);
        agent.Property(x => x.Platform).HasMaxLength(50);
        agent.Property(x => x.MachineName).HasMaxLength(255);
        agent.Property(x => x.CreatedAtUtc).IsRequired();
        agent.Property(x => x.ApiKeyCreatedAtUtc).IsRequired();
        agent.Property(x => x.Enabled).HasDefaultValue(true);
        agent.Property(x => x.ApiKeyRevoked).HasDefaultValue(false);
        agent.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        agent.HasIndex(x => x.InstanceId).IsUnique();
    }
}
