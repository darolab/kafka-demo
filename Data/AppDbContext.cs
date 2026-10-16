using Microsoft.EntityFrameworkCore;
using KafkaDemo.Models;
namespace KafkaDemo.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<AuthenticationBootstrapState> AuthenticationBootstrapStates =>
        Set<AuthenticationBootstrapState>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<NotificationDeliveryAudit> NotificationDeliveryAudits =>
        Set<NotificationDeliveryAudit>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(user => user.NormalizedEmail)
                .HasMaxLength(320)
                .IsRequired();
            entity.Property(user => user.Username)
                .HasMaxLength(64);
            entity.Property(user => user.NormalizedUsername)
                .HasMaxLength(64);
            entity.Property(user => user.PasswordHash)
                .HasMaxLength(512);
            entity.Property(user => user.IsEnabled)
                .HasDefaultValue(false);
            entity.Property(user => user.FailedLoginCount)
                .HasDefaultValue(0);
            entity.HasIndex(user => user.NormalizedEmail)
                .IsUnique();
            entity.HasIndex(user => user.NormalizedUsername)
                .IsUnique()
                .HasFilter("[NormalizedUsername] IS NOT NULL");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(role => role.Id);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_Roles_Name_Supported",
                "[Name] IN ('Admin', 'User')"));
            entity.Property(role => role.Name)
                .HasMaxLength(32)
                .IsRequired();
            entity.HasIndex(role => role.Name)
                .IsUnique();
            entity.HasData(
                new Role { Id = 1, Name = "User" },
                new Role { Id = 2, Name = "Admin" });
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(userRole => new { userRole.UserId, userRole.RoleId });
            entity.HasOne(userRole => userRole.User)
                .WithMany(user => user.UserRoles)
                .HasForeignKey(userRole => userRole.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(userRole => userRole.Role)
                .WithMany(role => role.UserRoles)
                .HasForeignKey(userRole => userRole.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuthenticationBootstrapState>(entity =>
        {
            entity.HasKey(state => state.Id);
            entity.HasData(new AuthenticationBootstrapState { Id = 1 });
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.TransactionNumber)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.FromAccount)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ToAccount)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.Amount)
                .HasColumnType("decimal(18,2)");

            entity.Property(x => x.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(x => x.TransactionNumber)
                .IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.EventType)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Topic)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.MessageKey)
                .HasMaxLength(200)
                .IsRequired()
                .HasDefaultValue(string.Empty);

            entity.Property(x => x.LeaseOwner)
                .HasMaxLength(100);

            entity.Property(x => x.Payload)
                .IsRequired();

            entity.HasIndex(x => new { x.PublishedAt, x.LeaseExpiresAtUtc, x.Id });
        });

        modelBuilder.Entity<NotificationDeliveryAudit>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.InstanceId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Result).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ErrorDetail).HasMaxLength(1000);
            entity.HasIndex(x => new { x.NotificationId, x.InstanceId });
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        });
    }

}