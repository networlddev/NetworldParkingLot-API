using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace NetworldParkingLot.Api.Features.SystemActivity.Services;

public sealed class SystemActivityService(NetworldParkingDbContext db) : ISystemActivityService
{
    public async Task RecordAsync(SystemActivityRequest request, CancellationToken cancellationToken = default)
    {
        var log = new SystemActivityLog
        {
            ActivityDate = DateTime.Now,
            UserId = request.UserId,
            Username = Trim(request.Username, 100),
            ModuleKey = Trim(request.ModuleKey, 100),
            ActionKey = Trim(request.ActionKey, 80),
            Result = Trim(string.IsNullOrWhiteSpace(request.Result) ? "Success" : request.Result, 20),
            EntityType = Trim(request.EntityType, 100),
            EntityId = TrimOrNull(request.EntityId, 100),
            Title = Trim(request.Title, 250),
            Message = TrimOrNull(request.Message, 1000),
            IpAddress = TrimOrNull(request.IpAddress, 80),
            UserAgent = TrimOrNull(request.UserAgent, 500),
            RequestPath = TrimOrNull(request.RequestPath, 300)
        };

        foreach (var change in request.Changes ?? [])
        {
            if (string.Equals(change.OldValue, change.NewValue, StringComparison.Ordinal))
                continue;

            log.Details.Add(new SystemActivityLogDetail
            {
                FieldName = Trim(change.FieldName, 150),
                OldValue = TrimOrNull(change.OldValue, 2000),
                NewValue = TrimOrNull(change.NewValue, 2000)
            });
        }

        await db.SystemActivityLogs.AddAsync(log, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedSystemActivityLogResultDto> SearchAsync(SystemActivityQueryRequest request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = request.PageSize <= 0 ? 25 : Math.Min(request.PageSize, 100);
        var query = ApplyFilters(db.SystemActivityLogs.AsNoTracking(), request);

        var totalCount = await query.CountAsync(cancellationToken);
        var successCount = await query.CountAsync(x => x.Result == "Success", cancellationToken);
        var failureCount = await query.CountAsync(x => x.Result == "Failure", cancellationToken);
        var desc = !string.Equals(request.SortDirection, "Asc", StringComparison.OrdinalIgnoreCase);

        var rows = await (desc ? query.OrderByDescending(x => x.ActivityDate).ThenByDescending(x => x.SystemActivityLogId) : query.OrderBy(x => x.ActivityDate).ThenBy(x => x.SystemActivityLogId))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SystemActivityLogItemDto(
                x.SystemActivityLogId,
                x.ActivityDate,
                x.UserId,
                x.Username,
                x.ModuleKey,
                x.ActionKey,
                x.Result,
                x.EntityType,
                x.EntityId,
                x.Title,
                x.Message,
                x.RequestPath))
            .ToListAsync(cancellationToken);

        var totalPages = pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PagedSystemActivityLogResultDto(rows, page, pageSize, totalCount, totalPages == 0 ? 1 : totalPages, successCount, failureCount);
    }

    public async Task<SystemActivityLogDto> GetByIdAsync(long systemActivityLogId, CancellationToken cancellationToken = default)
    {
        var log = await db.SystemActivityLogs
            .AsNoTracking()
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.SystemActivityLogId == systemActivityLogId, cancellationToken)
            ?? throw new InvalidOperationException("Activity log not found.");

        return new SystemActivityLogDto(
            log.SystemActivityLogId,
            log.ActivityDate,
            log.UserId,
            log.Username,
            log.ModuleKey,
            log.ActionKey,
            log.Result,
            log.EntityType,
            log.EntityId,
            log.Title,
            log.Message,
            log.IpAddress,
            log.UserAgent,
            log.RequestPath,
            log.Details
                .OrderBy(x => x.SystemActivityLogDetailId)
                .Select(x => new SystemActivityLogDetailDto(x.SystemActivityLogDetailId, x.FieldName, x.OldValue, x.NewValue))
                .ToList());
    }

    private static IQueryable<SystemActivityLog> ApplyFilters(IQueryable<SystemActivityLog> query, SystemActivityQueryRequest request)
    {
        var search = (request.SearchText ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.Username.Contains(search) ||
                x.ModuleKey.Contains(search) ||
                x.ActionKey.Contains(search) ||
                x.EntityType.Contains(search) ||
                (x.EntityId != null && x.EntityId.Contains(search)) ||
                x.Title.Contains(search) ||
                (x.Message != null && x.Message.Contains(search)));
        }

        if (request.UserId.HasValue && request.UserId.Value > 0)
            query = query.Where(x => x.UserId == request.UserId.Value);

        var module = CleanFilter(request.ModuleKey);
        if (module != null && !module.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.ModuleKey == module);

        var action = CleanFilter(request.ActionKey);
        if (action != null && !action.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.ActionKey == action);

        var result = CleanFilter(request.Result);
        if (result != null && !result.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Result == result);

        var entityType = CleanFilter(request.EntityType);
        if (entityType != null && !entityType.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.EntityType == entityType);

        var entityId = CleanFilter(request.EntityId);
        if (entityId != null)
            query = query.Where(x => x.EntityId == entityId);

        if (request.DateFrom.HasValue)
            query = query.Where(x => x.ActivityDate.Date >= request.DateFrom.Value.Date);

        if (request.DateTo.HasValue)
            query = query.Where(x => x.ActivityDate.Date <= request.DateTo.Value.Date);

        return query;
    }

    private static string? CleanFilter(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Trim(string? value, int maxLength)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static string? TrimOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }
}
