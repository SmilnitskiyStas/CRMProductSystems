using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data;
internal static class LabelJobArchiveModelConfiguration
{
    internal static void Configure(ModelBuilder b)
    {
        var j = b.Entity<LabelArchivedJob>();
        j.ToTable("label_test_jobs", t => {
            t.HasCheckConstraint("CK_label_test_job_hashes", "\"CommandHash\" ~ '^[0-9a-f]{64}$' AND \"IntentHash\" ~ '^[0-9a-f]{64}$' AND \"ArtifactHash\" ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("CK_label_test_job_copies", "\"Copies\" BETWEEN 1 AND 20 AND (\"Copies\"=1 OR \"RequiresBulk\")");
        });
        j.HasKey(x => new { x.TenantId, x.Id }); j.HasIndex(x => new { x.TenantId, x.LocationId, x.ReceivedAt, x.Id });
        j.HasOne<Location>().WithMany().HasForeignKey(x => new { x.TenantId, x.LocationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        j.Property(x => x.CommandHash).HasMaxLength(64).IsRequired(); j.Property(x => x.IntentHash).HasMaxLength(64).IsRequired(); j.Property(x => x.ArtifactHash).HasMaxLength(64).IsRequired(); j.Property(x => x.InitialPayload).HasColumnType("json").IsRequired();
        var e = b.Entity<LabelArchivedJobEvent>(); e.ToTable("label_test_job_events", t => {
            t.HasCheckConstraint("CK_label_test_job_event", "\"Seq\" BETWEEN 1 AND 16 AND \"Attempt\" BETWEEN 0 AND 3 AND \"Status\" IN ('queued','running','unknown','failed','succeeded','cancelled')");
            t.HasCheckConstraint("CK_label_test_job_event_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");
        });
        e.HasKey(x => new { x.TenantId, x.JobId, x.Seq }); e.HasOne<LabelArchivedJob>().WithMany().HasForeignKey(x => new { x.TenantId, x.JobId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Status).HasMaxLength(20).IsRequired(); e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired(); e.Property(x => x.Payload).HasColumnType("json").IsRequired(); e.Property(x => x.CheckpointPayload).HasColumnType("json");
        var r = b.Entity<LabelArchivedJobReceipt>(); r.ToTable("label_test_job_receipts", t => {
            t.HasCheckConstraint("CK_label_test_job_receipt_hashes", "\"CommandHash\" ~ '^[0-9a-f]{64}$' AND \"ArtifactHash\" ~ '^[0-9a-f]{64}$' AND \"Copies\" BETWEEN 1 AND 20");
        });
        r.HasKey(x => new { x.TenantId, x.JobId }); r.HasOne<LabelArchivedJobEvent>().WithMany().HasForeignKey(x => new { x.TenantId, x.JobId, x.EventSeq }).OnDelete(DeleteBehavior.Restrict);
        r.Property(x => x.CommandHash).HasMaxLength(64).IsRequired(); r.Property(x => x.ArtifactHash).HasMaxLength(64).IsRequired(); r.Property(x => x.Payload).HasColumnType("json").IsRequired();
        var u = b.Entity<LabelArchivedJobUpload>(); u.ToTable("label_test_job_uploads", t => t.HasCheckConstraint("CK_label_test_job_upload_hash", "\"RequestHash\" ~ '^[0-9a-f]{64}$'"));
        u.HasKey(x => new { x.TenantId, x.Id }); u.HasOne<LabelArchivedJob>().WithMany().HasForeignKey(x => new { x.TenantId, x.JobId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        u.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); u.Property(x => x.Result).HasColumnType("json").IsRequired();
    }
}
