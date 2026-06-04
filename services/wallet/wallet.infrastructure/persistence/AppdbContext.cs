using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using wallet.domain.contracts;
using wallet.domain.entities;

namespace wallet.infrastructure.persistence;

public class AppdbContext(DbContextOptions<AppdbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Currency> Currency => Set<Currency>();
    public DbSet<WalletAccount> Wallet => Set<WalletAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AppUserConfiguration())
                .ApplyConfiguration(new WalletAccountConfiguration())
                    .ApplyConfiguration(new TransactionConfiguration())
                        .ApplyConfiguration(new OutboxMessageConfiguration())
                            .ApplyConfiguration(new CurrencyConfiguration());

        base.OnModelCreating(builder);
    }

    public class AppdbContextFactory : IDesignTimeDbContextFactory<AppdbContext>
    {
        public AppdbContext CreateDbContext(string[] args)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../wallet.api"))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<AppdbContext>();
            optionsBuilder.UseNpgsql(configuration.GetConnectionString("Postgres"),
                npgsql =>
                {
                    npgsql.MigrationsAssembly("wallet.infrastructure");
                    npgsql.EnableRetryOnFailure(5);
                    npgsql.CommandTimeout(30);
                });

            return new AppdbContext(optionsBuilder.Options);
        }
    }
}