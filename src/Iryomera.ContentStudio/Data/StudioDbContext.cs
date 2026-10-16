using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Iryomera.ContentStudio.Data;

public sealed class StudioDbContext(DbContextOptions<StudioDbContext> options) : DbContext(options)
{
    public DbSet<StudioProject> Projects => Set<StudioProject>();
    public DbSet<DefinitionIndex> Definitions => Set<DefinitionIndex>();
    public DbSet<StudioImport> Imports => Set<StudioImport>();
    public DbSet<StudioAudit> Audits => Set<StudioAudit>();
    public DbSet<RecordPermission> Permissions => Set<RecordPermission>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<StudioProject>().Property(p => p.Revision).IsConcurrencyToken();
        model.Entity<StudioImport>().HasIndex(i => i.Sha256).IsUnique();
        model.Entity<StudioImport>().Property(p => p.Revision).IsConcurrencyToken();
        model.Entity<DefinitionIndex>().HasKey(d => new { d.ProjectId, d.Id });
        model.Entity<DefinitionIndex>().HasIndex(d => new { d.ProjectId, d.Kind, d.Status });
        model.Entity<DefinitionIndex>().HasOne<StudioProject>().WithMany().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<StudioAudit>().HasIndex(a => new { a.ScopeId, a.Id });
        model.Entity<RecordPermission>().HasIndex(p => new { p.ProjectId, p.RecordId, p.Id });
    }
}

public sealed class StudioProject
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public long Revision { get; set; }
    public string PackageJson { get; set; } = "";
    public string CatalogsJson { get; set; } = "[]";
    public Guid? SourceImportId { get; set; }
    public string SourceRecordIdsJson { get; set; } = "[]";
    public bool RightsConfirmed { get; set; }
    public string RightsReviewer { get; set; } = "";
    public string RightsNote { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DefinitionIndex
{
    public Guid ProjectId { get; set; }
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameDe { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class StudioImport
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string OriginalJson { get; set; } = "";
    public string WorkspaceJson { get; set; } = "";
    public long Revision { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class StudioAudit
{
    public long Id { get; set; }
    public Guid ScopeId { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Note { get; set; } = "";
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
    public DateTime AtUtc { get; set; }
}

public sealed class RecordPermission
{
    public long Id { get; set; }
    public Guid ProjectId { get; set; }
    public string RecordId { get; set; } = "";
    public string Digest { get; set; } = "";
    public bool Permitted { get; set; }
    public string Reviewer { get; set; } = "";
    public string Evidence { get; set; } = "";
    public DateTime AtUtc { get; set; }
}

public sealed class StudioDesignFactory : IDesignTimeDbContextFactory<StudioDbContext>
{
    public StudioDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<StudioDbContext>()
        .UseSqlite("Data Source=" + Path.Combine(Path.GetTempPath(), "iryomera-studio-design.db")).Options);
}
