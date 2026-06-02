namespace NetworldParkingLot.Api.Features.SystemActivity.Services;

public sealed record SystemActivityChange(string FieldName, string? OldValue, string? NewValue);

public sealed record SystemActivityRequest(
    int? UserId,
    string Username,
    string ModuleKey,
    string ActionKey,
    string Result,
    string EntityType,
    string? EntityId,
    string Title,
    string? Message,
    string? IpAddress,
    string? UserAgent,
    string? RequestPath,
    IReadOnlyList<SystemActivityChange>? Changes = null);

public sealed class SystemActivityQueryRequest
{
    public string? SearchText { get; set; }
    public int? UserId { get; set; }
    public string? ModuleKey { get; set; }
    public string? ActionKey { get; set; }
    public string? Result { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? SortDirection { get; set; } = "Desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed record SystemActivityLogItemDto(
    long SystemActivityLogId,
    DateTime ActivityDate,
    int? UserId,
    string Username,
    string ModuleKey,
    string ActionKey,
    string Result,
    string EntityType,
    string? EntityId,
    string Title,
    string? Message,
    string? RequestPath);

public sealed record SystemActivityLogDetailDto(
    long SystemActivityLogDetailId,
    string FieldName,
    string? OldValue,
    string? NewValue);

public sealed record SystemActivityLogDto(
    long SystemActivityLogId,
    DateTime ActivityDate,
    int? UserId,
    string Username,
    string ModuleKey,
    string ActionKey,
    string Result,
    string EntityType,
    string? EntityId,
    string Title,
    string? Message,
    string? IpAddress,
    string? UserAgent,
    string? RequestPath,
    IReadOnlyList<SystemActivityLogDetailDto> Details);

public sealed record PagedSystemActivityLogResultDto(
    IReadOnlyList<SystemActivityLogItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int SuccessCount,
    int FailureCount);
