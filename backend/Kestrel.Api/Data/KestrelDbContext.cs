using System.Text.RegularExpressions;
using Kestrel.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kestrel.Api.Data;

public class KestrelDbContext : DbContext
{
    public KestrelDbContext(DbContextOptions<KestrelDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<OrderNote> OrderNotes => Set<OrderNote>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportError> ImportErrors => Set<ImportError>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Department).HasMaxLength(40);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<Store>(e =>
        {
            e.ToTable("stores");
            e.Property(x => x.Code).HasMaxLength(10);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Region).HasMaxLength(30);
            e.Property(x => x.City).HasMaxLength(60);
            e.Property(x => x.Timezone).HasMaxLength(40);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.PasswordHash).HasMaxLength(100);
            e.Property(x => x.Role).HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.Property(x => x.Sku).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Brand).HasMaxLength(60);
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
            e.Property(x => x.CostPrice).HasPrecision(10, 2);
            e.HasIndex(x => x.Sku).IsUnique();
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
        });

        b.Entity<StockLevel>(e =>
        {
            e.ToTable("stock_levels");
            e.HasIndex(x => new { x.StoreId, x.ProductId }).IsUnique();
            e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        });

        b.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.FirstName).HasMaxLength(60);
            e.Property(x => x.LastName).HasMaxLength(60);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.AddressLine1).HasMaxLength(120);
            e.Property(x => x.City).HasMaxLength(60);
            e.Property(x => x.Postcode).HasMaxLength(12);
            e.Property(x => x.DateOfBirth).HasColumnType("date");
            e.Property(x => x.LoyaltyTier).HasMaxLength(10);
            e.Property(x => x.Region).HasMaxLength(30);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.Property(x => x.OrderNumber).HasMaxLength(20);
            e.Property(x => x.Channel).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.Subtotal).HasPrecision(10, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(10, 2);
            e.Property(x => x.TotalAmount).HasPrecision(10, 2);
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.HasIndex(x => x.OrderedUtc);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId);
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.OrderId);
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.OrderId);
            e.HasMany(x => x.Notes).WithOne().HasForeignKey(x => x.OrderId);
        });

        b.Entity<OrderLine>(e =>
        {
            e.ToTable("order_lines");
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
            e.Property(x => x.LineTotal).HasPrecision(10, 2);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        });

        b.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.Property(x => x.Method).HasMaxLength(12);
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.CardLast4).HasMaxLength(4).IsFixedLength();
            e.Property(x => x.ProviderRef).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(12);
        });

        b.Entity<OrderStatusHistory>(e =>
        {
            e.ToTable("order_status_history");
            e.Property(x => x.FromStatus).HasMaxLength(12);
            e.Property(x => x.ToStatus).HasMaxLength(12);
        });

        b.Entity<OrderNote>(e =>
        {
            e.ToTable("order_notes");
            e.Property(x => x.Note).HasMaxLength(500);
        });

        b.Entity<ImportBatch>(e =>
        {
            e.ToTable("import_batches");
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(12);
            e.HasMany(x => x.Errors).WithOne().HasForeignKey(x => x.BatchId);
        });

        b.Entity<ImportError>(e =>
        {
            e.ToTable("import_errors");
            e.Property(x => x.Message).HasMaxLength(300);
        });

        // Column names are snake_case in MySQL.
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                prop.SetColumnName(ToSnake(prop.Name));
    }

    private static string ToSnake(string name) =>
        Regex.Replace(name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
}
