namespace NetworldParkingLot.Api.Features.SystemActivity.Services;

public interface ISystemActivityService
{
    Task RecordAsync(SystemActivityRequest request, CancellationToken cancellationToken = default);
    Task<PagedSystemActivityLogResultDto> SearchAsync(SystemActivityQueryRequest request, CancellationToken cancellationToken = default);
    Task<SystemActivityLogDto> GetByIdAsync(long systemActivityLogId, CancellationToken cancellationToken = default);
}
