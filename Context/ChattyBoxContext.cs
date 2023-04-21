using System;
using System.Collections.Generic;
using ChattyBox.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Context;

public partial class ChattyBoxContext : IdentityDbContext<User, Role, string, UserClaim, UserRole, UserLogin, RoleClaim, UserToken> {
  public ChattyBoxContext() {
  }

  public ChattyBoxContext(DbContextOptions<ChattyBoxContext> options)
      : base(options) {
  }

  public virtual DbSet<Blocked> Blocks { get; set; } = null!;

  public virtual DbSet<ClientConnection> ClientConnections { get; set; } = null!;

  public virtual DbSet<Chat> Chats { get; set; } = null!;

  public virtual DbSet<ChatToUser> ChatToUsers { get; set; } = null!;

  public virtual DbSet<FriendRequest> FriendRequests { get; set; } = null!;

  public virtual DbSet<Message> Messages { get; set; } = null!;

  public virtual DbSet<ReadMessage> ReadMessages { get; set; } = null!;

  public override DbSet<Role> Roles { get; set; } = null!;

  public override DbSet<RoleClaim> RoleClaims { get; set; } = null!;

  public override DbSet<User> Users { get; set; } = null!;

  public override DbSet<UserClaim> UserClaims { get; set; } = null!;

  public override DbSet<UserLogin> UserLogins { get; set; } = null!;

  public override DbSet<UserRole> UserRoles { get; set; } = null!;

  public override DbSet<UserToken> UserTokens { get; set; } = null!;

  protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
    if (!optionsBuilder.IsConfigured) {
      var configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.Development.json")
        .Build();
      var connString = configuration.GetConnectionString("Default");
      optionsBuilder.UseSqlServer(connString);
    }
  }

  protected override void OnModelCreating(ModelBuilder modelBuilder) {

    modelBuilder.Entity<ClientConnection>(entity => {
      entity.HasKey(e => e.UserId).HasName("ClientConnection_pkey");

      entity.HasOne(e => e.User).WithOne(u => u.Connection);
    });

    modelBuilder.Entity<Chat>(entity => {
      entity.HasKey(e => e.Id).HasName("Chat_pkey");

      entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
      entity.Property(e => e.MaxUsers).HasDefaultValueSql("((2))");
      entity.HasMany<User>(e => e.Users).WithMany(u => u.Chats)
        .UsingEntity<ChatToUser>(
          r => r.HasOne(r => r.BNavigation).WithMany()
            .HasForeignKey("B")
            .HasConstraintName("_ChatToUser_B_fkey"),
          l => l.HasOne(r => r.ANavigation).WithMany()
            .HasForeignKey("A")
            .HasConstraintName("_ChatToUser_A_fkey"),
          j => {
            j.HasKey("A", "B").HasName("_ChatToUser_AB_unique");
            j.ToTable("_ChatToUser");
          });
    });

    modelBuilder.Entity<FriendRequest>(entity => {
      entity.HasKey(e => new { e.UserAddingId, e.UserBeingAddedId }).HasName("FriendRequest_pkey");

      entity.Property(e => e.SentAt).HasDefaultValueSql("(getdate())");

      entity.HasOne(d => d.UserAdding).WithMany(p => p.FriendRequestsSent)
          .OnDelete(DeleteBehavior.ClientSetNull)
          .HasConstraintName("FriendRequest_userAddingId_fkey");

      entity.HasOne(d => d.UserBeingAdded).WithMany(p => p.FriendRequestsReceived)
          .OnDelete(DeleteBehavior.ClientSetNull)
          .HasConstraintName("FriendRequest_userBeingAddedId_fkey");
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
        .IsRequired(false)
        .HasConstraintName("Message_replyToId_fkey");
    });

    modelBuilder.Entity<ReadMessage>(entity => {
      entity.HasKey(e => new { e.MessageId, e.UserId }).HasName("ReadMessage_pkey");

      entity.HasOne(d => d.Message).WithMany(p => p.ReadBy)
          .OnDelete(DeleteBehavior.ClientSetNull)
          .HasConstraintName("ReadMessage_messageId_fkey");

      entity.HasOne(d => d.ReadBy).WithMany(p => p.ReadMessages)
          .OnDelete(DeleteBehavior.ClientSetNull)
          .HasConstraintName("ReadMessage_userId_fkey");

      entity.Property(e => e.ReadAt).HasDefaultValueSql("(getdate())");
    });

    modelBuilder.Entity<RoleClaim>(entity => {
      entity.HasOne(d => d.Role).WithMany(p => p.RoleClaims).HasConstraintName("FK_RoleClaims_Roles_RoleId");
    });

    modelBuilder.Entity<User>(entity => {
      entity.HasMany(d => d.Roles).WithMany(p => p.Users)
        .UsingEntity<UserRole>(
          r => r.HasOne<Role>().WithMany()
            .HasForeignKey("RoleId")
            .HasConstraintName("FK_UserRoles_Roles_RoleId"),
          l => l.HasOne<User>().WithMany()
            .HasForeignKey("UserId")
            .HasConstraintName("FK_UserRoles_Users_UserId"),
          j => {
            j.HasKey("UserId", "RoleId").HasName("PK_UserRoles");
            j.ToTable("UserRole");
          });

      entity.HasMany(d => d.IsAdminIn).WithMany(p => p.Admins)
        .UsingEntity<ChatAdmin>(
          r => r.HasOne<Chat>().WithMany()
            .HasForeignKey("ChatId")
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("ChatAdmin_chatId_fkey"),
          l => l.HasOne<User>().WithMany()
            .HasForeignKey("UserId")
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("ChatAdmin_userId_fkey"),
          j => {
            j.HasKey("UserId", "ChatId").HasName("ChatAdmin_pkey");
            j.ToTable("ChatAdmin");
          });

      entity.HasMany(d => d.Friends).WithMany(p => p.IsFriendsWith)
        .UsingEntity<Friend>(
          r => r.HasOne(c => c.ANavigation).WithMany()
            .HasForeignKey("A")
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("_friends_A_fkey"),
          l => l.HasOne(c => c.BNavigation).WithMany()
            .HasForeignKey("B")
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("_friends_B_fkey"),
          j => {
            j.HasKey("A", "B").HasName("_friends_AB_unique");
            j.ToTable("_friends");
          });

      entity.HasMany(d => d.Blocking).WithMany(p => p.BlockedBy)
        .UsingEntity<Blocked>(
          r => r.HasOne(c => c.ANavigation).WithMany()
            .HasForeignKey("A")
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("_blocked_A_fkey"),
          l => l.HasOne(c => c.BNavigation).WithMany()
            .HasForeignKey("B")
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("_blocked_B_fkey"),
          j => {
            j.HasKey("A", "B").HasName("_blocked_AB_unique");
            j.ToTable("_blocked");
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
