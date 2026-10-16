using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wolsels.Api.Domain;
using Wolsels.Api.Services;

namespace Wolsels.Api.Data;

public class WolselsDbContext(DbContextOptions<WolselsDbContext> options, ICurrentUser? currentUser = null)
    : IdentityDbContext<IdentityUser>(options)
{
    /// <summary>Evaluated per request in the query filters below.</summary>
    private int? CurrentHouseholdId => currentUser?.HouseholdId;

    public DbSet<Wool> Wools => Set<Wool>();
    public DbSet<WoolPhoto> WoolPhotos => Set<WoolPhoto>();
    public DbSet<Blend> Blends => Set<Blend>();
    public DbSet<BlendComponent> BlendComponents => Set<BlendComponent>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Animal> Animals => Set<Animal>();
    public DbSet<Harvest> Harvests => Set<Harvest>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();
    public DbSet<HouseholdInvite> HouseholdInvites => Set<HouseholdInvite>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no decimal: store as REAL so that sorting and filtering in SQL keep working.
        configurationBuilder.Properties<decimal>().HaveConversion<double>();
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<string>();
        configurationBuilder.Properties<DateOnly>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each household only sees its own data.
        modelBuilder.Entity<Wool>().HasQueryFilter(e => e.HouseholdId == CurrentHouseholdId);
        modelBuilder.Entity<WoolPhoto>().HasQueryFilter(e => e.HouseholdId == CurrentHouseholdId);
        modelBuilder.Entity<Blend>().HasQueryFilter(e => e.HouseholdId == CurrentHouseholdId);
        modelBuilder.Entity<Location>().HasQueryFilter(e => e.HouseholdId == CurrentHouseholdId);
        modelBuilder.Entity<Animal>().HasQueryFilter(e => e.HouseholdId == CurrentHouseholdId);
        modelBuilder.Entity<Harvest>().HasQueryFilter(e => e.HouseholdId == CurrentHouseholdId);
        // Components are only reachable through their (filtered) blend.
        modelBuilder.Entity<BlendComponent>()
            .HasQueryFilter(c => Blends.Any(b => b.Id == c.BlendId));

        foreach (var type in modelBuilder.Model.GetEntityTypes().Where(t => typeof(IHouseholdData).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(type.ClrType).HasIndex(nameof(IHouseholdData.HouseholdId));
        }

        modelBuilder.Entity<Household>(e =>
        {
            e.Property(h => h.Name).HasMaxLength(100);
            e.Property(h => h.CreatedByUserId).HasMaxLength(450);
        });
        modelBuilder.Entity<HouseholdMember>(e =>
        {
            e.HasKey(m => new { m.UserId, m.HouseholdId });
            e.Property(m => m.UserId).HasMaxLength(450);
            e.HasOne(m => m.Household).WithMany().HasForeignKey(m => m.HouseholdId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.HouseholdId);
        });
        modelBuilder.Entity<UserSettings>(e =>
        {
            e.HasKey(s => s.UserId);
            e.Property(s => s.UserId).HasMaxLength(450);
            e.Property(s => s.Language).HasMaxLength(10);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<HouseholdInvite>(e =>
        {
            e.Property(i => i.TokenHash).HasMaxLength(64);
            e.HasIndex(i => i.TokenHash).IsUnique();
            e.Property(i => i.CreatedByUserId).HasMaxLength(450);
            e.Property(i => i.UsedByUserId).HasMaxLength(450);
            e.HasOne<Household>().WithMany().HasForeignKey(i => i.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Wool>(e =>
        {
            e.Property(w => w.Name).HasMaxLength(200);
            e.Property(w => w.WoolType).HasMaxLength(100);
            e.Property(w => w.Color).HasMaxLength(100);
            e.Property(w => w.ColorHex).HasMaxLength(7);
            e.Property(w => w.Origin).HasMaxLength(200);
            e.Property(w => w.Notes).HasMaxLength(4000);
        });
        // Photos disappear with their lot, also with ExecuteDelete (merging, deleting a household).
        modelBuilder.Entity<WoolPhoto>(e =>
        {
            e.HasOne<Wool>().WithMany().HasForeignKey(p => p.WoolId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.WoolId);
        });

        modelBuilder.Entity<Blend>(e =>
        {
            e.Property(b => b.Name).HasMaxLength(200);
            e.Property(b => b.Notes).HasMaxLength(4000);
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(b => b.Basis).HasConversion<string>().HasMaxLength(20);
            e.Property(b => b.YarnWeight).HasMaxLength(50);
            e.Property(b => b.PlyMethod).HasMaxLength(100);
            e.Property(b => b.SpinDirection).HasMaxLength(1);
            e.Property(b => b.DyeColor).HasMaxLength(100);
            e.Property(b => b.DyeColorHex).HasMaxLength(7);
            e.Property(b => b.DyeProduct).HasMaxLength(200);
            e.Property(b => b.DyeMethod).HasMaxLength(4000);
            e.HasMany(b => b.Components)
                .WithOne()
                .HasForeignKey(c => c.BlendId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BlendComponent>(e =>
        {
            // Wool used in a blend cannot be deleted.
            e.HasOne(c => c.Wool)
                .WithMany()
                .HasForeignKey(c => c.WoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    

        modelBuilder.Entity<Location>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(200);
            e.Property(l => l.Notes).HasMaxLength(4000);
        });

        modelBuilder.Entity<Animal>(e =>
        {
            e.Property(a => a.Name).HasMaxLength(200);
            e.Property(a => a.Species).HasMaxLength(100);
            e.Property(a => a.Breed).HasMaxLength(100);
            e.Property(a => a.Color).HasMaxLength(100);
            e.Property(a => a.Notes).HasMaxLength(4000);
            e.HasOne(a => a.Location)
                .WithMany()
                .HasForeignKey(a => a.LocationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Harvest>(e =>
        {
            e.Property(h => h.Method).HasMaxLength(100);
            e.Property(h => h.Grade).HasMaxLength(100);
            e.Property(h => h.Notes).HasMaxLength(4000);
            e.HasIndex(h => h.Date);
            // Animals and locations with harvests cannot be deleted (set an animal to inactive).
            e.HasOne(h => h.Animal).WithMany().HasForeignKey(h => h.AnimalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(h => h.Location).WithMany().HasForeignKey(h => h.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(h => h.Wool).WithMany().HasForeignKey(h => h.WoolId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_Harvest_AnimalOrLocation",
                "(\"AnimalId\" IS NOT NULL AND \"LocationId\" IS NULL) OR (\"AnimalId\" IS NULL AND \"LocationId\" IS NOT NULL)"));
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AssignOwner();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssignOwner();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>New data automatically belongs to the household of the logged-in user.</summary>
    private void AssignOwner()
    {
        foreach (var entry in ChangeTracker.Entries<IHouseholdData>().Where(e => e.State == EntityState.Added && e.Entity.HouseholdId == 0))
        {
            entry.Entity.HouseholdId = CurrentHouseholdId
                ?? throw new InvalidOperationException("Saving data requires a logged-in user with a household.");
        }
    }
}
