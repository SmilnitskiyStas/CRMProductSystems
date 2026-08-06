namespace ShelfGuard.Domain.Entities;

/// <summary>
/// One matched customer within a <see cref="PostCampaignSegment"/> — created only for tokens
/// that resolved to a real <c>Customer.Id</c> in this tenant at import time. Unknown/invalid
/// tokens are never materialized as rows here; they exist only as counts + a capped sample on
/// the parent segment (<see cref="PostCampaignSegment.UnknownTokensSample"/>/
/// <see cref="PostCampaignSegment.InvalidTokensSample"/>).
///
/// Dedup against re-upload is the import step's responsibility (service layer, TASK-472); the
/// unique (SegmentId, CustomerId) index declared in AppDbContext is the hard backstop — a
/// customer appears at most once per segment.
/// </summary>
public sealed class PostCampaignSegmentMember
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid SegmentId { get; init; }
    public Guid CustomerId { get; init; }

    public PostCampaignSegment? Segment { get; init; }
    public Customer? Customer { get; init; }
}
