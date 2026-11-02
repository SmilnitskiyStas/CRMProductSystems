using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data;
internal static class LabelPrinterModelConfiguration
{
    internal static void Configure(ModelBuilder b)
    {
        var p = b.Entity<LabelPrinterRevision>(); p.ToTable("label_printer_profile_revisions", t => {
            t.HasCheckConstraint("CK_label_printer_version", "\"Version\" BETWEEN 1 AND 10000");
            t.HasCheckConstraint("CK_label_printer_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$' AND \"RequestHash\" ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("CK_label_printer_simulation", "(\"Payload\"->>'transport') IS NOT DISTINCT FROM 'simulation' AND COALESCE(\"Payload\"->>'protocol' IN ('pdf','zpl'),false)");
        });
        p.HasKey(x => new { x.TenantId, x.Id }); p.HasIndex(x => new { x.TenantId, x.ProfileKey, x.Version }).IsUnique(); p.HasIndex(x => new { x.TenantId, x.LocationId, x.ProfileKey });
        p.HasOne<Location>().WithMany().HasForeignKey(x => new { x.TenantId, x.LocationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        p.Property(x => x.ContentHash).HasMaxLength(64).IsRequired(); p.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); p.Property(x => x.Payload).HasColumnType("json").IsRequired();
        var j = b.Entity<LabelArchivedJob>(); j.Property(x => x.JobProtocol).HasMaxLength(32).HasDefaultValue("label-job.v1").IsRequired();
        j.ToTable("label_test_jobs", t => t.HasCheckConstraint("CK_label_test_job_printer_protocol", "(\"JobProtocol\"='label-job.v1' AND \"PrinterRevisionId\" IS NULL) OR (\"JobProtocol\"='label-job.v2' AND \"PrinterRevisionId\" IS NOT NULL)"));
        j.HasOne<LabelPrinterRevision>().WithMany().HasForeignKey(x => new { x.TenantId, x.PrinterRevisionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
