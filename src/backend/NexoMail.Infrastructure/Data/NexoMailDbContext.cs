using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;

namespace NexoMail.Infrastructure.Data;

/// <summary>Operational storage only. Mail messages and attachments are never persisted here.</summary>
public sealed class NexoMailDbContext(DbContextOptions<NexoMailDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<UserSessionEntity> UserSessions => Set<UserSessionEntity>();
    public DbSet<MailAccountEntity> MailAccounts => Set<MailAccountEntity>();
    public DbSet<OAuthCredentialEntity> OAuthCredentials => Set<OAuthCredentialEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(1024);
            entity.Property(x => x.PasswordResetTokenHash).HasMaxLength(128);
            entity.Property(x => x.EmailVerificationTokenHash).HasMaxLength(128);
            entity.Property(x => x.AvatarDataUrl).HasMaxLength(200_000);
            entity.HasIndex(x => x.Email).IsUnique();
        });
        modelBuilder.Entity<UserSessionEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.RevokedAt });
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MailAccountEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.EmailAddress).HasMaxLength(320).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.EmailAddress }).IsUnique();
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<OAuthCredentialEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EncryptedRefreshToken).IsRequired();
            entity.HasOne<MailAccountEntity>().WithOne().HasForeignKey<OAuthCredentialEntity>(x => x.MailAccountId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; set; }
    public int PasswordResetAttempts { get; set; }
    public bool IsEmailVerified { get; set; } = true;
    public string? EmailVerificationTokenHash { get; set; }
    public DateTimeOffset? EmailVerificationTokenExpiresAt { get; set; }
    public int EmailVerificationAttempts { get; set; }
    public string? AvatarDataUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class UserSessionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
}

public sealed class MailAccountEntity { public Guid Id { get; set; } public Guid UserId { get; set; } public MailProviderType Provider { get; set; } public string EmailAddress { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public string Color { get; set; } = "#0f6b78"; public bool IsActive { get; set; } = true; public DateTimeOffset CreatedAt { get; set; } }
public sealed class OAuthCredentialEntity { public Guid Id { get; set; } public Guid MailAccountId { get; set; } public string EncryptedRefreshToken { get; set; } = string.Empty; public DateTimeOffset? ExpiresAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } }
