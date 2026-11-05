using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data;
internal static class LabelModelConfiguration
{
    internal static void Configure(ModelBuilder b)
    {
        var r = b.Entity<LabelTemplateRevision>();
        r.ToTable("label_template_revisions", t => {
            t.HasCheckConstraint("CK_label_template_version", "\"Version\" BETWEEN 1 AND 10000");
            t.HasCheckConstraint("CK_label_template_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$' AND \"RequestHash\" ~ '^[0-9a-f]{64}$'");
        });
        r.HasKey(x => x.Id); r.HasAlternateKey(x => new { x.TenantId, x.Id });
        r.HasIndex(x => new { x.TenantId, x.TemplateKey, x.Version }).IsUnique();
        r.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        r.Property(x => x.ContentHash).HasMaxLength(64).IsRequired(); r.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); r.Property(x => x.Payload).HasColumnType("json").IsRequired();
        var a = b.Entity<LabelStoreAssignment>();
        a.ToTable("label_store_assignments", t => {
            t.HasCheckConstraint("CK_label_assignment_version", "\"Version\">0");
            t.HasCheckConstraint("CK_label_assignment_hash", "\"RequestHash\" ~ '^[0-9a-f]{64}$'");
        });
        a.HasKey(x => x.Id); a.HasIndex(x => new { x.TenantId, x.LocationId, x.Version }).IsUnique();
        a.HasOne<LabelTemplateRevision>().WithMany().HasForeignKey(x => new { x.TenantId, x.RevisionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<Location>().WithMany().HasForeignKey(x => new { x.TenantId, x.LocationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        a.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
    }
}
