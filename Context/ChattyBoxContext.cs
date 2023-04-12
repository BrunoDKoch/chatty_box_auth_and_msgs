using System;
using System.Collections.Generic;
using ChattyBox.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Context;

public partial class ChattyBoxContext : IdentityDbContext<User, Role, string, UserClaim, IdentityUserRole<string>, UserLogin, RoleClaim, UserToken> {
  public ChattyBoxContext() {
  }

  public ChattyBoxContext(DbContextOptions<ChattyBoxContext> options)
      : base(options) {
  }

  public virtual DbSet<Blocked> Blocks { get; set; } = null!;

  public virtual DbSet<Chat> Chats { get; set; } = null!;

  public virtual DbSet<ChatToUser> ChatToUsers { get; set; } = null!;

  public virtual DbSet<Friend> Friends { get; set; } = null!;

  public virtual DbSet<Message> Messages { get; set; } = null!;

  public override DbSet<Role> Roles { get; set; } = null!;

  public override DbSet<RoleClaim> RoleClaims { get; set; } = null!;

  public override DbSet<User> Users { get; set; } = null!;

  public override DbSet<UserClaim> UserClaims { get; set; } = null!;

  public override DbSet<UserLogin> UserLogins { get; set; } = null!;

  public override DbSet<UserToken> UserTokens { get; set; } = null!;

  protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
      => optionsBuilder.UseSqlServer("Name=Default");

  protected override void OnModelCreating(ModelBuilder modelBuilder) {
    modelBuilder.Entity<Blocked>(entity => {
      entity.HasOne(d => d.ANavigation).WithMany()
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("_blocked_A_fkey");

      entity.HasOne(d => d.BNavigation).WithMany()
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("_blocked_B_fkey");
    });

    modelBuilder.Entity<Chat>(entity => {
      entity.HasKey(e => e.Id).HasName("Chat_pkey");

      entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
      entity.Property(e => e.MaxUsers).HasDefaultValueSql("((2))");
    });

    modelBuilder.Entity<ChatToUser>(entity => {
      entity.HasOne(d => d.ANavigation).WithMany().HasConstraintName("_ChatToUser_A_fkey");

      entity.HasOne(d => d.BNavigation).WithMany().HasConstraintName("_ChatToUser_B_fkey");
    });

    modelBuilder.Entity<Friend>(entity => {
      entity.HasOne(d => d.ANavigation).WithMany()
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("_friends_A_fkey");

      entity.HasOne(d => d.BNavigation).WithMany()
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("_friends_B_fkey");
    });

    modelBuilder.Entity<Message>(entity => {
      entity.HasKey(e => e.Id).HasName("Message_pkey");

      entity.Property(e => e.SentAt).HasDefaultValueSql("(getdate())");

      entity.HasOne(d => d.Chat).WithMany(p => p.Messages)
      .OnDelete(DeleteBehavior.ClientSetNull)
      .HasConstraintName("Message_chatId_fkey");

      entity.HasOne(d => d.From).WithMany(p => p.Messages)
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("Message_fromId_fkey");

      entity.HasOne(d => d.ReplyTo).WithMany(p => p.InverseReplyTo)
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("Message_replyToId_fkey");
    });

    modelBuilder.Entity<RoleClaim>(entity => {
      entity.HasOne(d => d.Role).WithMany(p => p.RoleClaims).HasConstraintName("FK_RoleClaims_Roles_RoleId");
    });

    modelBuilder.Entity<User>(entity => {
      entity.HasMany(d => d.Roles).WithMany(p => p.Users)
        .UsingEntity<Dictionary<string, object>>(
          "UserRole",
          r => r.HasOne<Role>().WithMany()
            .HasForeignKey("RoleId")
            .HasConstraintName("FK_UserRoles_Roles_RoleId"),
          l => l.HasOne<User>().WithMany()
            .HasForeignKey("UserId")
            .HasConstraintName("FK_UserRoles_Users_UserId"),
          j => {
            j.HasKey("UserId", "RoleId").HasName("PK_UserRoles");
            j.ToTable("UserRole");
            j.IndexerProperty<string>("UserId").HasColumnName("userId");
            j.IndexerProperty<string>("RoleId").HasColumnName("roleId");
          });
    });

    modelBuilder.Entity<UserClaim>(entity => {
      entity.HasOne(d => d.User).WithMany(p => p.UserClaims).HasConstraintName("FK_UserClaims_Users_UserId");
    });

    modelBuilder.Entity<UserLogin>(entity => {
      entity.HasOne(d => d.User).WithMany(p => p.UserLogins).HasConstraintName("FK_UserLogins_Users_UserId");
    });

    modelBuilder.Entity<UserToken>(entity => {
      entity.HasOne(d => d.User).WithMany(p => p.UserTokens).HasConstraintName("FK_UserTokens_Users_UserId");
    });

    OnModelCreatingPartial(modelBuilder);
  }

  partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
