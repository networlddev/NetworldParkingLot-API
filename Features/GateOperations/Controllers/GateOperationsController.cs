using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Features.GateOperations.Dtos;
using NetworldParkingLot.Api.Features.GateOperations.Services;

namespace NetworldParkingLot.Api.Features.GateOperations.Controllers;

[ApiController]
[Route("api/gate-operation")]
public sealed class GateOperationsController(IGateOperationService service) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<GateSummaryDto>>> GetSummary(CancellationToken cancellationToken)
    {
        var data = await service.GetSummaryAsync(cancellationToken);
        return Ok(ApiResponse<GateSummaryDto>.Ok(data));
    }

    [HttpGet("companies/search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CompanySearchDto>>>> SearchCompanies([FromQuery] string searchText, CancellationToken cancellationToken)
    {
        var data = await service.SearchCompaniesAsync(searchText, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CompanySearchDto>>.Ok(data));
    }

    [HttpGet("companies")]
    public async Task<ActionResult<ApiResponse<PagedResultDto<CompanyListItemDto>>>> GetCompanies(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] string? status,
        [FromQuery] string? paymentStatus,
        [FromQuery] string? subscriptionType,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var request = new CompanyListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            Status = status,
            PaymentStatus = paymentStatus,
            SubscriptionType = subscriptionType,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = page,
            PageSize = pageSize
        };

        var data = await service.GetCompaniesAsync(request, cancellationToken);
        return Ok(ApiResponse<PagedResultDto<CompanyListItemDto>>.Ok(data));
    }

    [HttpGet("companies/export")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CompanyListItemDto>>>> ExportCompanies(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] string? status,
        [FromQuery] string? paymentStatus,
        [FromQuery] string? subscriptionType,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken = default)
    {
        var request = new CompanyListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            Status = status,
            PaymentStatus = paymentStatus,
            SubscriptionType = subscriptionType,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            SortBy = sortBy,
            SortDirection = sortDirection
        };

        var data = await service.ExportCompaniesAsync(request, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CompanyListItemDto>>.Ok(data));
    }

    [HttpPost("companies")]
    public async Task<ActionResult<ApiResponse<CompanyListItemDto>>> CreateCompany([FromBody] CreateCompanyWithSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CreateCompanyWithSubscriptionAsync(request, cancellationToken);
            return Ok(ApiResponse<CompanyListItemDto>.Ok(data, "Company and subscription created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CompanyListItemDto>.Fail(ex.Message));
        }
    }

    [HttpPut("companies/{companyId:int}")]
    public async Task<ActionResult<ApiResponse<CompanyListItemDto>>> UpdateCompany(int companyId, [FromBody] UpdateCompanyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.UpdateCompanyAsync(companyId, request, cancellationToken);
            return Ok(ApiResponse<CompanyListItemDto>.Ok(data, "Company updated."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CompanyListItemDto>.Fail(ex.Message));
        }
    }

    [HttpPost("entry/check-company")]
    public async Task<ActionResult<ApiResponse<CompanyGateStatusDto>>> CheckCompany([FromBody] CheckCompanyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CheckCompanyAsync(request, cancellationToken);
            return Ok(ApiResponse<CompanyGateStatusDto>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CompanyGateStatusDto>.Fail(ex.Message));
        }
    }

    [HttpPost("entry/generate-barcode")]
    public async Task<ActionResult<ApiResponse<GenerateBarcodeResponseDto>>> GenerateBarcode([FromBody] GenerateBarcodeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.GenerateBarcodeAsync(request, cancellationToken);
            return Ok(ApiResponse<GenerateBarcodeResponseDto>.Ok(data, "Barcode generated. Print sticker and allow entry."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<GenerateBarcodeResponseDto>.Fail(ex.Message));
        }
    }

    [HttpPost("entry/allow")]
    public async Task<ActionResult<ApiResponse<EntryResultDto>>> AllowEntry([FromBody] AllowEntryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.AllowEntryAsync(request, cancellationToken);
            return Ok(ApiResponse<EntryResultDto>.Ok(data, "Entry allowed."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<EntryResultDto>.Fail(ex.Message));
        }
    }

    [HttpPost("entry/reject")]
    public async Task<ActionResult<ApiResponse<EntryResultDto>>> RejectEntry([FromBody] RejectEntryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.RejectEntryAsync(request, cancellationToken);
            return Ok(ApiResponse<EntryResultDto>.Ok(data, "Entry rejected."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<EntryResultDto>.Fail(ex.Message));
        }
    }

    [HttpPost("exit/scan")]
    public async Task<ActionResult<ApiResponse<ExitScanResultDto>>> ScanExit([FromBody] ScanExitRequest request, CancellationToken cancellationToken)
    {
        var data = await service.ScanExitBarcodeAsync(request, cancellationToken);
        return Ok(ApiResponse<ExitScanResultDto>.Ok(data, data.Message));
    }

    [HttpPost("exit/allow")]
    public async Task<ActionResult<ApiResponse<ExitResultDto>>> AllowExit([FromBody] AllowExitRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.AllowExitAsync(request, cancellationToken);
            return Ok(ApiResponse<ExitResultDto>.Ok(data, "Exit completed."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ExitResultDto>.Fail(ex.Message));
        }
    }

    [HttpPost("payment/collect")]
    public async Task<ActionResult<ApiResponse<PaymentResultDto>>> CollectPayment([FromBody] CollectPaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CollectPaymentAsync(request, cancellationToken);
            return Ok(ApiResponse<PaymentResultDto>.Ok(data, "Payment collected."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<PaymentResultDto>.Fail(ex.Message));
        }
    }

    [HttpGet("extra-slots/rates")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExtraSlotRateDto>>>> GetExtraSlotRates(CancellationToken cancellationToken)
    {
        var data = await service.GetExtraSlotRatesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ExtraSlotRateDto>>.Ok(data));
    }

    [HttpPost("extra-slots/invoice")]
    public async Task<ActionResult<ApiResponse<ExtraSlotInvoiceResultDto>>> CreateExtraSlotInvoice([FromBody] CreateExtraSlotInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CreateExtraSlotInvoiceAsync(request, cancellationToken);
            return Ok(ApiResponse<ExtraSlotInvoiceResultDto>.Ok(data, "Extra slot invoice created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ExtraSlotInvoiceResultDto>.Fail(ex.Message));
        }
    }

    [HttpGet("invoices/company/{companyId:int}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CompanyInvoiceDto>>>> GetCompanyInvoices(int companyId, CancellationToken cancellationToken)
    {
        if (companyId <= 0)
            return BadRequest(ApiResponse<IReadOnlyList<CompanyInvoiceDto>>.Fail("Company id is required."));

        try
        {
            var data = await service.GetCompanyInvoicesAsync(companyId, cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<CompanyInvoiceDto>>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<IReadOnlyList<CompanyInvoiceDto>>.Fail(ex.Message));
        }
    }

    [HttpGet("recent-activity")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RecentActivityDto>>>> GetRecentActivity([FromQuery] int take = 25, CancellationToken cancellationToken = default)
    {
        var data = await service.GetRecentActivityAsync(take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<RecentActivityDto>>.Ok(data));
    }

    [HttpGet("live-parking")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LiveParkingDto>>>> GetLiveParking([FromQuery] string? searchText = null, CancellationToken cancellationToken = default)
    {
        var data = await service.GetLiveParkingAsync(searchText, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<LiveParkingDto>>.Ok(data));
    }

    [HttpGet("outside-display/latest")]
    public async Task<ActionResult<ApiResponse<OutsideDisplayDto?>>> GetLatestOutsideDisplay(CancellationToken cancellationToken)
    {
        var data = await service.GetLatestOutsideDisplayAsync(cancellationToken);
        return Ok(ApiResponse<OutsideDisplayDto?>.Ok(data));
    }

    [HttpGet("outside-display/recent-scans")]

    public async Task<ActionResult<ApiResponse<IReadOnlyList<OutsideDisplayDto>>>> GetRecentOutsideDisplayScans(
    [FromQuery] int take = 30,
    CancellationToken cancellationToken = default)

    {

        var data = await service.GetRecentOutsideDisplayScansAsync(take, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<OutsideDisplayDto>>.Ok(data));

    }
}
