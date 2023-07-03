using Microsoft.EntityFrameworkCore;

namespace BigMission.Database
{
    /// <summary>
    /// Database context independent from Web project that is not fixed to older .Net 3.1.
    /// </summary>
    public partial class RedMist
    {
        public RedMist(string connectionString)
        {
            ConnectionString = connectionString;
        }

        private string ConnectionString { get; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer(ConnectionString);
            }
        }
    }
}
