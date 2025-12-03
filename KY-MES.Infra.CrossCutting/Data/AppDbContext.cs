using KY_MES.Domain.V1.DTOs.InputModels;
using Microsoft.EntityFrameworkCore;

namespace KY_MES.Infra.CrossCutting.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<InspectionRun> InspectionRuns => Set<InspectionRun>();
        public DbSet<InspectionUnit> InspectionUnits => Set<InspectionUnit>();
        public DbSet<InspectionDefect> InspectionDefects => Set<InspectionDefect>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InspectionRun>(b =>
            {
                b.ToTable("inspection_runs");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();
                b.Property(x => x.InspectionBarcode).HasMaxLength(100);
                b.Property(x => x.Result).HasMaxLength(20);
                b.Property(x => x.Program).HasMaxLength(100);
                b.Property(x => x.Side).HasMaxLength(20);
                b.Property(x => x.Stencil).HasMaxLength(50);
                b.Property(x => x.Machine).HasMaxLength(100);
                b.Property(x => x.User).HasMaxLength(100);
                b.Property(x => x.ManufacturingArea).HasMaxLength(100);
                b.Property(x => x.Carrier).HasMaxLength(100);
                b.Property(x => x.RawJson).HasColumnType("nvarchar(max)");
                b.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()"); 
                b.HasIndex(x => x.InspectionBarcode);
                b.HasIndex(x => x.Carrier);
                b.HasMany(x => x.Units)
                    .WithOne(u => u.Run)
                    .HasForeignKey(u => u.InspectionRunId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<InspectionUnit>(b =>
            {
                b.ToTable("inspection_units");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();
                b.Property(x => x.UnitBarcode).HasMaxLength(100);
                b.Property(x => x.Result).HasMaxLength(20);
                b.Property(x => x.Side).HasMaxLength(20);
                b.Property(x => x.Machine).HasMaxLength(100);
                b.Property(x => x.User).HasMaxLength(100);
                b.Property(x => x.ManufacturingArea).HasMaxLength(100);
                b.Property(x => x.Carrier).HasMaxLength(100);
                b.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()"); 
                b.HasIndex(x => x.InspectionRunId);
                b.HasIndex(x => x.UnitBarcode);
                b.HasIndex(x => x.Carrier);
                b.HasIndex(x => new { x.InspectionRunId, x.ArrayIndex }).IsUnique();
            });

            modelBuilder.Entity<InspectionDefect>(b =>
            {
                b.ToTable("inspection_defects");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd(); 
                b.Property(x => x.Comp).HasMaxLength(100);
                b.Property(x => x.Part).HasMaxLength(100);
                b.Property(x => x.DefectCode).HasMaxLength(100).IsRequired();
                b.Property(x => x.Carrier).HasMaxLength(100); 
                b.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()"); 
                b.HasIndex(x => x.InspectionUnitId);
                b.HasIndex(x => x.DefectCode);
                b.HasIndex(x => x.Carrier);
                b.HasOne(x => x.Unit)
                    .WithMany(u => u.Defects)
                    .HasForeignKey(x => x.InspectionUnitId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}