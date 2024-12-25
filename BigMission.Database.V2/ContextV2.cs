using Microsoft.EntityFrameworkCore;

namespace BigMission.Database.V2;

public class ContextV2 : DbContext
{
    public DbSet<Models.UI.Channels.CarStatusTable.Configuration> CarStatusTableConfiguration { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=localhost;Database=redmistdb-prod;User Id=sa;Password=;TrustServerCertificate=True");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo2");

    }
}
