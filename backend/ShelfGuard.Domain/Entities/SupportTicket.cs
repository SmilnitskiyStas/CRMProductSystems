namespace ShelfGuard.Domain.Entities;

public sealed class SupportTicket
{
    public Guid   Id         { get; init; }  = Guid.NewGuid();
    public Guid   TenantId   { get; init; }
    public int    Number     { get; init; }                             // human-readable TK-0001
    public string Title       { get; set;  }  = string.Empty;
    public string Description { get; set;  }  = string.Empty;
    public string Category   { get; set;  }  = SupportTicketCategory.General;
    public string Priority   { get; set;  }  = SupportTicketPriority.Medium;
    public string Status     { get; set;  }  = SupportTicketStatus.Open;

    // FK: creator (required)
    public Guid   CreatedBy  { get; init; }
    public User   CreatedByUser { get; init; } = null!;

    // FK: assignee (optional, support agent)
    public Guid?  AssignedTo     { get; set; }
    public User?  AssignedToUser { get; set; }

    // FK: location that raised the ticket (optional)
    public Guid?     LocationId { get; set; }
    public Location? Location   { get; set; }

    public DateTimeOffset?  ResolvedAt            { get; set;  }
    public DateTimeOffset   CreatedAt             { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset   UpdatedAt             { get; set;  } = DateTimeOffset.UtcNow;

    // Kept for backward compat with existing SupportMessage read-tracking
    public DateTimeOffset? LastReadByTenantAt   { get; set;  }
    public DateTimeOffset? LastReadByProviderAt { get; set;  }

    // Legacy messages (SupportMessage) — kept for backward compat
    public ICollection<SupportMessage>  Messages  { get; init; } = [];

    // New structured comments
    public ICollection<TicketComment>   Comments  { get; init; } = [];
}

public static class SupportTicketStatus
{
    public const string Open       = "open";
    public const string InProgress = "in_progress";
    public const string Waiting    = "waiting";
    public const string Resolved   = "resolved";
    public const string Closed     = "closed";
}

public static class SupportTicketPriority
{
    public const string Low      = "low";
    public const string Medium   = "medium";
    public const string High     = "high";
    public const string Critical = "critical";
}

public static class SupportTicketCategory
{
    public const string General        = "general";
    public const string Technical      = "technical";
    public const string Billing        = "billing";
    public const string FeatureRequest = "feature_request";
    public const string Bug            = "bug";
}
