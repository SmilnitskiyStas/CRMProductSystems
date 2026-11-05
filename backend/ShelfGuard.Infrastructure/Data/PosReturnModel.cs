using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data;

internal static class PosReturnModel
{
    internal static void Configure(ModelBuilder b)
    {
        b.Entity<PosReturnFiscalization>(e => {
            e.ToTable("pos_return_fiscalizations", t => {
                t.HasCheckConstraint("CK_pos_return_amount", "\"Amount\">=0 AND \"TaxAmount\">=0 AND \"TaxAmount\"<=\"Amount\" AND \"Attempts\">=0");
                t.HasCheckConstraint("CK_pos_return_status", "\"Status\" IN ('pending','done','failed') AND (\"Status\"<>'done' OR \"FiscalNumber\" IS NOT NULL AND length(btrim(\"FiscalNumber\"))>0)");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.SaleId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
            e.Property(x => x.Amount).HasPrecision(12,2);e.Property(x => x.TaxAmount).HasPrecision(12,2);
            e.Property(x => x.RequestHash).HasMaxLength(64);e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.PaymentType).HasMaxLength(20);e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.FiscalNumber).HasMaxLength(128);e.Property(x => x.LastError).HasMaxLength(80);
            e.Property(x => x.RequestJson).HasColumnType("jsonb");
            e.HasOne<BusinessDocument>().WithMany().HasForeignKey(x => new { x.TenantId, x.Id }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PosTransaction>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PosShift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
