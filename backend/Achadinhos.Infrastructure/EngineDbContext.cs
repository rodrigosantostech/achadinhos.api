using Achadinhos.Domain.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Achadinhos.Infrastructure;

public sealed class EngineDbContext(DbContextOptions<EngineDbContext> options) : DbContext(options)
{
    public DbSet<Marketplace> Marketplaces => Set<Marketplace>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<IntegrationRun> IntegrationRuns => Set<IntegrationRun>();
    public DbSet<OfferReview> OfferReviews => Set<OfferReview>();
    public DbSet<Publication> Publications => Set<Publication>();

    public DbSet<PublicationChannel> PublicationChannels => Set<PublicationChannel>();
    public DbSet<PublicationAttempt> PublicationAttempts => Set<PublicationAttempt>();

    public DbSet<PublicationSimulation> PublicationSimulations => Set<PublicationSimulation>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var marketplace = model.Entity<Marketplace>();
        marketplace.ToTable("marketplaces");
        marketplace.HasKey(x => x.Id);
        marketplace.Property(x => x.Code).HasMaxLength(50);
        marketplace.Property(x => x.Name).HasMaxLength(200);
        marketplace.HasIndex(x => x.Code).IsUnique();
        marketplace.HasData(new Marketplace
        {
            Id = Guid.Parse("5b70fb4d-7329-4da9-9167-3e049321a13e"),
            Code = "manual",
            Name = "Cadastro manual",
            Enabled = true,
            CreatedAt = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero)
        });

        var review = model.Entity<OfferReview>();
        review.ToTable("offer_reviews");
        review.HasKey(x => x.Id);
        review.Property(x => x.Reason).HasMaxLength(2000);
        review.Property(x => x.Operator).HasMaxLength(200);
        review.HasIndex(x => new { x.OfferId, x.CreatedAt });
        review.HasOne<Offer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Restrict);

        var publication = model.Entity<Publication>();
        publication.ToTable("publications");
        publication.HasKey(x => x.Id);
        publication.Property(x => x.Operator).HasMaxLength(200);
        publication.HasIndex(x => x.OfferId).IsUnique().HasFilter("\"Status\" = 'Scheduled'");
        publication.Property(x => x.PayloadSnapshot).HasColumnType("jsonb");
        publication.Property(x => x.IdempotencyKey).HasMaxLength(150);
        publication.HasIndex(x => x.IdempotencyKey).IsUnique();
        publication.HasIndex(x => new { x.Status, x.ScheduledAt });
        publication.HasIndex(x => new { x.Status, x.LeaseUntil });
        publication.HasIndex(x => new { x.OfferId, x.ChannelId }).IsUnique()
            .HasFilter("\"ChannelId\" IS NOT NULL AND \"Status\" IN ('Scheduled','Processing','RetryScheduled')");
        publication.HasOne<PublicationChannel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        var channel = model.Entity<PublicationChannel>();
        channel.ToTable("publication_channels");
        channel.HasKey(x => x.Id);
        channel.HasIndex(x => x.Code).IsUnique();
        channel.Property(x => x.Code).HasMaxLength(100);
        var attempt = model.Entity<PublicationAttempt>();
        attempt.ToTable("publication_attempts");
        attempt.HasKey(x => x.Id);
        attempt.HasIndex(x => new { x.PublicationId, x.Number }).IsUnique();
        attempt.HasOne<Publication>().WithMany().HasForeignKey(x => x.PublicationId).OnDelete(DeleteBehavior.Restrict);
        publication.HasOne<Offer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Restrict);

        var simulation = model.Entity<PublicationSimulation>();
        simulation.ToTable("publication_simulations");
        simulation.HasKey(x => x.Id);
        simulation.HasIndex(x => x.PublicationId).IsUnique();
        simulation.Property(x => x.Operator).HasMaxLength(200);
        simulation.Property(x => x.Snapshot).HasColumnType("jsonb");
        simulation.Property(x => x.ProductUrl).HasMaxLength(2048);
        simulation.HasOne<Publication>().WithMany().HasForeignKey(x => x.PublicationId).OnDelete(DeleteBehavior.Restrict);

        var offer = model.Entity<Offer>();
        offer.ToTable("offers", t =>
        {
            t.HasCheckConstraint("ck_offers_prices", "\"CurrentPrice\" >= 0 AND \"OriginalPrice\" >= 0");
        });
        offer.HasKey(x => x.Id);
        offer.Property(x => x.ExternalOfferId).HasMaxLength(200);
        offer.Property(x => x.Title).HasMaxLength(1000);
        offer.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        offer.Property(x => x.CurrentPrice).HasPrecision(18, 2);
        offer.Property(x => x.OriginalPrice).HasPrecision(18, 2);
        offer.Property(x => x.RawPayload).HasColumnType("jsonb");
        offer.HasIndex(x => new { x.MarketplaceId, x.ExternalOfferId }).IsUnique();
        offer.HasOne<Marketplace>().WithMany().HasForeignKey(x => x.MarketplaceId).OnDelete(DeleteBehavior.Restrict);

        var snapshot = model.Entity<PriceSnapshot>();
        snapshot.ToTable("price_snapshots", t => t.HasCheckConstraint("ck_snapshot_price", "\"Price\" >= 0"));
        snapshot.HasKey(x => x.Id);
        snapshot.Property(x => x.Price).HasPrecision(18, 2);
        snapshot.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        snapshot.HasIndex(x => new { x.OfferId, x.CapturedAt }).IsDescending(false, true);
        snapshot.HasOne<Offer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Restrict);

        var run = model.Entity<IntegrationRun>();
        run.ToTable("integration_runs");
        run.HasKey(x => x.Id);
        run.Property(x => x.ErrorSummary).HasMaxLength(2000);
        run.HasIndex(x => new { x.Provider, x.StartedAt });
    }
}

public sealed class EngineDbContextFactory : IDesignTimeDbContextFactory<EngineDbContext>
{
    public EngineDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Engine")
            ?? "Host=localhost;Database=achadinhos;Username=achadinhos";
        return new EngineDbContext(new DbContextOptionsBuilder<EngineDbContext>().UseNpgsql(connection).Options);
    }
}
