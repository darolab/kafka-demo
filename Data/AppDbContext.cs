using Microsoft.EntityFrameworkCore;
using KafkaDemo.Models;
using System.Data;

namespace KafkaDemo.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

            entity.Property(x => x.Payload)
                .IsRequired();
        });
    }

}