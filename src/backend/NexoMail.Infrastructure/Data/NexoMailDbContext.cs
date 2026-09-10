using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;

namespace NexoMail.Infrastructure.Data;

/// <summary>Operational storage plus lightweight mail metadata indexes. Message bodies and attachment contents are never persisted here.</summary>
public sealed class NexoMailDbContext(DbContextOptions<NexoMailDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<UserSessionEntity> UserSessions => Set<UserSessionEntity>();
    public DbSet<MailAccountEntity> MailAccounts => Set<MailAccountEntity>();
    public DbSet<OAuthCredentialEntity> OAuthCredentials => Set<OAuthCredentialEntity>();
    public DbSet<ControlCenterStateEntity> ControlCenterStates => Set<ControlCenterStateEntity>();
    public DbSet<IgnoredSenderEntity> IgnoredSenders => Set<IgnoredSenderEntity>();
    public DbSet<MailMessageIndexEntity> MailMessageIndex => Set<MailMessageIndexEntity>();
    public DbSet<MailAttachmentIndexEntity> MailAttachmentIndex => Set<MailAttachmentIndexEntity>();
    public DbSet<MailIndexStateEntity> MailIndexStates => Set<MailIndexStateEntity>();
    public DbSet<CommercialPlanEntity> CommercialPlans => Set<CommercialPlanEntity>();

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
            entity.Property(x => x.PlanCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LegalConsentVersion).HasMaxLength(32);
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
        modelBuilder.Entity<ControlCenterStateEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ConversationId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.LastMessageId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.AccountId, x.ConversationId }).IsUnique();
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MailAccountEntity>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IgnoredSenderEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SenderAddress).HasMaxLength(320).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.AccountId, x.SenderAddress }).IsUnique();
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MailAccountEntity>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MailMessageIndexEntity>(entity =>
        {
            entity.ToTable("MailMessageIndex");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderMessageId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ThreadId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Direction).HasMaxLength(16).IsRequired();
            entity.Property(x => x.FromName).HasMaxLength(320);
            entity.Property(x => x.FromAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.ToAddresses).HasMaxLength(4000);
            entity.Property(x => x.Subject).HasMaxLength(1000);
            entity.Property(x => x.Snippet).HasMaxLength(1200);
            entity.HasIndex(x => new { x.UserId, x.AccountId, x.ProviderMessageId }).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.OccurredAt });
            entity.HasIndex(x => new { x.UserId, x.ThreadId, x.OccurredAt });
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MailAccountEntity>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MailAttachmentIndexEntity>(entity =>
        {
            entity.ToTable("MailAttachmentIndex");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderMessageId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.AttachmentId).HasMaxLength(512).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.AccountId, x.ProviderMessageId, x.AttachmentId }).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.IndexedAt });
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MailAccountEntity>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MailIndexStateEntity>(entity =>
        {
            entity.ToTable("MailIndexStates");
            entity.HasKey(x => x.AccountId);
            entity.HasIndex(x => new { x.UserId, x.LastIndexedAt });
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MailAccountEntity>().WithOne().HasForeignKey<MailIndexStateEntity>(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CommercialPlanEntity>(entity =>
        {
            entity.ToTable("CommercialPlans");
            entity.HasKey(x => x.Code);
            entity.Property(x => x.Code).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Price).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Cadence).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(600).IsRequired();
            entity.Property(x => x.FeaturesJson).HasMaxLength(6000).IsRequired();
            entity.HasIndex(x => new { x.IsActive, x.SortOrder });
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
    public string PlanCode { get; set; } = CommercialPlanCatalog.Freemium;
    public bool IsAdministrator { get; set; }
    public string? LegalConsentVersion { get; set; }
    public DateTimeOffset? LegalConsentAcceptedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CommercialPlanEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public string Cadence { get; set; } = string.Empty;
    public int? MaxAccounts { get; set; }
    public string Description { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "[]";
    public bool IsFeatured { get; set; }
    public bool IsCorporate { get; set; }
    public bool IsWhiteLabel { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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
public sealed class ControlCenterStateEntity { public Guid Id { get; set; } public Guid UserId { get; set; } public Guid AccountId { get; set; } public string ConversationId { get; set; } = string.Empty; public string LastMessageId { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public DateTimeOffset? SnoozedUntil { get; set; } public DateTimeOffset UpdatedAt { get; set; } }
public sealed class IgnoredSenderEntity { public Guid Id { get; set; } public Guid UserId { get; set; } public Guid AccountId { get; set; } public string SenderAddress { get; set; } = string.Empty; public DateTimeOffset CreatedAt { get; set; } }
public sealed class MailMessageIndexEntity { public Guid Id { get; set; } public Guid UserId { get; set; } public Guid AccountId { get; set; } public string ProviderMessageId { get; set; } = string.Empty; public string ThreadId { get; set; } = string.Empty; public string Direction { get; set; } = string.Empty; public string FromName { get; set; } = string.Empty; public string FromAddress { get; set; } = string.Empty; public string ToAddresses { get; set; } = string.Empty; public string Subject { get; set; } = string.Empty; public string Snippet { get; set; } = string.Empty; public DateTimeOffset OccurredAt { get; set; } public bool HasAttachments { get; set; } public DateTimeOffset IndexedAt { get; set; } }
public sealed class MailAttachmentIndexEntity { public Guid Id { get; set; } public Guid UserId { get; set; } public Guid AccountId { get; set; } public string ProviderMessageId { get; set; } = string.Empty; public string AttachmentId { get; set; } = string.Empty; public string FileName { get; set; } = string.Empty; public string ContentType { get; set; } = string.Empty; public long Size { get; set; } public DateTimeOffset IndexedAt { get; set; } }
public sealed class MailIndexStateEntity { public Guid AccountId { get; set; } public Guid UserId { get; set; } public DateTimeOffset LastIndexedAt { get; set; } public int WindowDays { get; set; } public int IndexedMessageCount { get; set; } }