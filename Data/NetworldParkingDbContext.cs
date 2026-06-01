using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Domain.Entities;

namespace NetworldParkingLot.Api.Data;

public sealed class NetworldParkingDbContext(DbContextOptions<NetworldParkingDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppRole> AppRoles => Set<AppRole>();
    public DbSet<AppModule> AppModules => Set<AppModule>();
    public DbSet<AppModuleAction> AppModuleActions => Set<AppModuleAction>();
    public DbSet<AppUserRole> AppUserRoles => Set<AppUserRole>();
    public DbSet<AppRolePermission> AppRolePermissions => Set<AppRolePermission>();
    public DbSet<AppUserPermission> AppUserPermissions => Set<AppUserPermission>();
    public DbSet<ParkingCompany> ParkingCompanies => Set<ParkingCompany>();
    public DbSet<ParkingSubscription> ParkingSubscriptions => Set<ParkingSubscription>();
    public DbSet<ParkingInvoice> ParkingInvoices => Set<ParkingInvoice>();
    public DbSet<ParkingPayment> ParkingPayments => Set<ParkingPayment>();
    public DbSet<ParkingSession> ParkingSessions => Set<ParkingSession>();
    public DbSet<GateActivityLog> GateActivityLogs => Set<GateActivityLog>();
    public DbSet<OutsideDisplayEvent> OutsideDisplayEvents => Set<OutsideDisplayEvent>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<SystemCounter> SystemCounters => Set<SystemCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasKey(x => x.UserId);
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<AppUser>().Property(x => x.Status).HasDefaultValue("Active");

        modelBuilder.Entity<AppRole>().HasKey(x => x.RoleId);
        modelBuilder.Entity<AppRole>().HasIndex(x => x.RoleKey).IsUnique();

        modelBuilder.Entity<AppModule>().HasKey(x => x.ModuleId);
        modelBuilder.Entity<AppModule>().HasIndex(x => x.ModuleKey).IsUnique();

        modelBuilder.Entity<AppModuleAction>().HasKey(x => x.ActionId);
        modelBuilder.Entity<AppModuleAction>().HasIndex(x => new { x.ModuleId, x.ActionKey }).IsUnique();
        modelBuilder.Entity<AppModuleAction>()
            .HasOne(x => x.Module)
            .WithMany(x => x.Actions)
            .HasForeignKey(x => x.ModuleId);

        modelBuilder.Entity<AppUserRole>().HasKey(x => new { x.UserId, x.RoleId });
        modelBuilder.Entity<AppUserRole>()
            .HasOne(x => x.User)
            .WithMany(x => x.UserRoles)
            .HasForeignKey(x => x.UserId);
        modelBuilder.Entity<AppUserRole>()
            .HasOne(x => x.Role)
            .WithMany(x => x.UserRoles)
            .HasForeignKey(x => x.RoleId);

        modelBuilder.Entity<AppRolePermission>().HasKey(x => new { x.RoleId, x.ModuleId, x.ActionKey });
        modelBuilder.Entity<AppRolePermission>()
            .HasOne(x => x.Role)
            .WithMany(x => x.Permissions)
            .HasForeignKey(x => x.RoleId);
        modelBuilder.Entity<AppRolePermission>()
            .HasOne(x => x.Module)
            .WithMany()
            .HasForeignKey(x => x.ModuleId);

        modelBuilder.Entity<AppUserPermission>().HasKey(x => new { x.UserId, x.ModuleId, x.ActionId });
        modelBuilder.Entity<AppUserPermission>()
            .HasOne(x => x.User)
            .WithMany(x => x.UserPermissions)
            .HasForeignKey(x => x.UserId);
        modelBuilder.Entity<AppUserPermission>()
            .HasOne(x => x.Module)
            .WithMany()
            .HasForeignKey(x => x.ModuleId);
        modelBuilder.Entity<AppUserPermission>()
            .HasOne(x => x.Action)
            .WithMany()
            .HasForeignKey(x => x.ActionId);

        modelBuilder.Entity<ParkingCompany>().HasKey(x => x.CompanyId);
        modelBuilder.Entity<ParkingCompany>().HasIndex(x => x.CompanyCode).IsUnique();
        modelBuilder.Entity<ParkingCompany>().Property(x => x.OpeningBalance).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingCompany>().Property(x => x.CreditLimit).HasPrecision(18, 2);

        modelBuilder.Entity<ParkingSubscription>().HasKey(x => x.SubscriptionId);
        modelBuilder.Entity<ParkingSubscription>().Property(x => x.RatePerSlot).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingSubscription>().Property(x => x.DiscountAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingSubscription>().Property(x => x.VatAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingSubscription>().Property(x => x.TotalAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingSubscription>().Property(x => x.PaidAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingSubscription>().Property(x => x.BalanceAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingSubscription>()
            .HasOne(x => x.Company)
            .WithMany(x => x.Subscriptions)
            .HasForeignKey(x => x.CompanyId);

        modelBuilder.Entity<ParkingInvoice>().HasKey(x => x.InvoiceId);
        modelBuilder.Entity<ParkingInvoice>().HasIndex(x => x.InvoiceNo).IsUnique();
        modelBuilder.Entity<ParkingInvoice>().Property(x => x.SubTotal).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingInvoice>().Property(x => x.DiscountAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingInvoice>().Property(x => x.VatAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingInvoice>().Property(x => x.TotalAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingInvoice>().Property(x => x.PaidAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingInvoice>().Property(x => x.BalanceAmount).HasPrecision(18, 2);

        modelBuilder.Entity<ParkingPayment>().HasKey(x => x.PaymentId);
        modelBuilder.Entity<ParkingPayment>().HasIndex(x => x.ReceiptNo).IsUnique();
        modelBuilder.Entity<ParkingPayment>().Property(x => x.Amount).HasPrecision(18, 2);

        modelBuilder.Entity<ParkingSession>().HasKey(x => x.SessionId);
        modelBuilder.Entity<ParkingSession>().HasIndex(x => x.BarcodeNo).IsUnique();
        modelBuilder.Entity<ParkingSession>().HasIndex(x => new { x.CompanyId, x.Status });
        modelBuilder.Entity<ParkingSession>().HasIndex(x => new { x.PlateNo, x.Status });
        modelBuilder.Entity<ParkingSession>().Property(x => x.OverstayAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ParkingSession>()
            .HasOne(x => x.Company)
            .WithMany(x => x.ParkingSessions)
            .HasForeignKey(x => x.CompanyId);
        modelBuilder.Entity<ParkingSession>()
            .HasOne(x => x.Subscription)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<GateActivityLog>().HasKey(x => x.ActivityLogId);
        modelBuilder.Entity<OutsideDisplayEvent>().HasKey(x => x.DisplayEventId);
        modelBuilder.Entity<OutsideDisplayEvent>().Property(x => x.AmountDue).HasPrecision(18, 2);

        modelBuilder.Entity<SystemSetting>().HasKey(x => x.SettingId);
        modelBuilder.Entity<SystemSetting>().HasIndex(x => x.SettingKey).IsUnique();

        modelBuilder.Entity<SystemCounter>().HasKey(x => x.SystemCounterId);
        modelBuilder.Entity<SystemCounter>().HasIndex(x => x.CounterName).IsUnique();
    }
}
