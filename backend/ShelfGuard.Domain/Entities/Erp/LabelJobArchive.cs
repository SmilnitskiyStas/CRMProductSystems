namespace ShelfGuard.Domain.Entities.Erp;
public sealed class LabelArchivedJob
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LocationId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime ClientCreatedAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string CommandHash { get; set; } = "";
    public string IntentHash { get; set; } = "";
    public string ArtifactHash { get; set; } = "";
    public int Copies { get; set; }
    public bool RequiresBulk { get; set; }
    public bool HasPrice { get; set; }
    public string JobProtocol { get; set; } = "label-job.v1";
    public Guid? PrinterRevisionId { get; set; }
    public string InitialPayload { get; set; } = "";
}
public sealed class LabelArchivedJobEvent
{
    public Guid TenantId { get; set; }
    public Guid JobId { get; set; }
    public int Seq { get; set; }
    public string Status { get; set; } = "";
    public int Attempt { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public Guid ReportedBy { get; set; }
    public string ContentHash { get; set; } = "";
    public string Payload { get; set; } = "";
    public string? CheckpointPayload { get; set; }
}
public sealed class LabelArchivedJobReceipt
{
    public Guid TenantId { get; set; }
    public Guid JobId { get; set; }
    public int EventSeq { get; set; }
    public DateTime ClientAcceptedAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string CommandHash { get; set; } = "";
    public string ArtifactHash { get; set; } = "";
    public int Copies { get; set; }
    public string Payload { get; set; } = "";
}
public sealed class LabelArchivedJobUpload
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JobId { get; set; }
    public Guid ReportedBy { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string RequestHash { get; set; } = "";
    public string Result { get; set; } = "";
}
