using Microsoft.EntityFrameworkCore;

namespace SchoolManager.Server.Data;

public sealed class ServerDb(DbContextOptions<ServerDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ReleaseNote> ReleaseNotes => Set<ReleaseNote>();
    public DbSet<DevApproval> DevApprovals => Set<DevApproval>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ServerSetting> Settings => Set<ServerSetting>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(user =>
        {
            user.HasIndex(u => u.UserName).IsUnique();
            user.Property(u => u.UserName).HasMaxLength(64);
            user.Property(u => u.DisplayName).HasMaxLength(100);
            user.HasMany(u => u.Groups).WithMany(g => g.Users).UsingEntity("UserGroups");
            user.HasMany(u => u.Sessions).WithOne(s => s.User).HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Group>(group =>
        {
            group.HasIndex(g => g.Name).IsUnique();
            group.Property(g => g.Name).HasMaxLength(64);
            group.Property(g => g.Description).HasMaxLength(300);
        });

        model.Entity<Session>().HasIndex(s => s.TokenHash).IsUnique();

        model.Entity<Message>().Property(m => m.Text).HasMaxLength(1000);

        model.Entity<ReleaseNote>(note =>
        {
            note.HasKey(n => n.Tag);
            note.Property(n => n.Text).HasMaxLength(1000);
        });

        model.Entity<DevApproval>(approval =>
        {
            approval.HasKey(a => a.Tag);
            approval.HasMany(a => a.Users).WithMany().UsingEntity("DevApprovalUsers");
            approval.HasMany(a => a.Groups).WithMany().UsingEntity("DevApprovalGroups");
        });

        model.Entity<AuditEntry>().HasIndex(a => a.Time);

        model.Entity<ServerSetting>().HasKey(s => s.Key);

        // SQLite kann DateTimeOffset weder sortieren noch vergleichen; als
        // Ticks in UTC gespeichert geht beides.
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
                        value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero)));
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset?, long?>(
                        value => value.HasValue ? value.Value.UtcTicks : null,
                        ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null));
            }
        }
    }
}
