using System.Text.Json.Serialization;
using Iryomera.Content.SDK;

namespace Iryomera.ContentStudio.Services;

public sealed record StudioReviewedRecord(string Id, Provenance Provenance);
public sealed record StudioPermissionNotice(string RecordId, string Digest, bool Permitted, string Reviewer, string Evidence, DateTime AtUtc);
public sealed record StudioReleaseReport
{
    [JsonRequired] public string SchemaVersion { get; init; } = "1.0.0";
    public required Guid ProjectId { get; init; }
    public required long Revision { get; init; }
    public required string RightsReviewer { get; init; }
    public required string RightsNote { get; init; }
    public List<StudioReviewedRecord> ApprovedRecords { get; init; } = [];
    public List<StudioPermissionNotice> Permissions { get; init; } = [];
}
