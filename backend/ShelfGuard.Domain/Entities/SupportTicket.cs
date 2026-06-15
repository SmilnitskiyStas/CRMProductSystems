namespace ShelfGuard.Domain.Entities;

public sealed class SupportTicket
{
    public Guid   Id         { get; init; }  = Guid.NewGuid();
    public Guid   TenantId   { get; init; }
    public Guid   CreatedBy  { get; init; }
    public Guid?  AssignedTo { get; set; }
    public string Subject    { get; init; }  = string.Empty;
    public string Status     { get; set;  }  = SupportTicketStatus.Open;
    public string Priority   { get; init; }  = SupportTicketPriority.Normal;
    public DateTime  CreatedAt           { get; init; } = DateTime.UtcNow;
    public DateTime  UpdatedAt           { get; set;  } = DateTime.UtcNow;
    public DateTime? LastReadByTenantAt  { get; set;  }
    public DateTime? LastReadByProviderAt{ get; set;  }

    public ICollection<SupportMessage> Messages { get; init; } = [];
}

public static class SupportTicketStatus
{
    public const string Open       = "open";
    public const string InProgress = "in_progress";
    public const string Resolved   = "resolved";
    public const string Closed     = "closed";
}

public static class SupportTicketPriority
{
    public const string Low    = "low";
    public const string Normal = "normal";
    public const string High   = "high";
    public const string Urgent = "urgent";
}
