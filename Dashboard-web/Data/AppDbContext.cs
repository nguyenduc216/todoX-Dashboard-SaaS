using Microsoft.EntityFrameworkCore;
using TodoX.Dashboard.Models;

namespace TodoX.Dashboard.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<TokenWallet> TokenWallets => Set<TokenWallet>();
    public DbSet<TokenTransaction> TokenTransactions => Set<TokenTransaction>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<RenderJob> RenderJobs => Set<RenderJob>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sales");
        modelBuilder.Entity<SystemUser>().HasIndex(x => x.UserName).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(x => x.Name);
        modelBuilder.Entity<CustomerAccount>().HasIndex(x => x.UserName).IsUnique();
        modelBuilder.Entity<TokenWallet>().HasIndex(x => x.CustomerId).IsUnique();
        modelBuilder.Entity<PricingRule>().HasIndex(x => x.ServiceCode).IsUnique();
        modelBuilder.Entity<SystemSetting>().HasIndex(x => x.Key).IsUnique();
    }
}
