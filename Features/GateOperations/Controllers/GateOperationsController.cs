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



    [HttpGet("subscriptions/rates")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExtraSlotRateDto>>>> GetSubscriptionRates(CancellationToken cancellationToken)
    {
        var data = await service.GetExtraSlotRatesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ExtraSlotRateDto>>.Ok(data));
    }

    [HttpGet("subscriptions")]
    public async Task<ActionResult<ApiResponse<PagedSubscriptionResultDto>>> GetSubscriptions(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] string? planType,
        [FromQuery] string? status,
        [FromQuery] string? paymentStatus,
        [FromQuery] bool? isExtraSlot,
        [FromQuery] DateTime? startFrom,
        [FromQuery] DateTime? startTo,
        [FromQuery] DateTime? endFrom,
        [FromQuery] DateTime? endTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var request = new SubscriptionListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            PlanType = planType,
            Status = status,
            PaymentStatus = paymentStatus,
            IsExtraSlot = isExtraSlot,
            StartFrom = startFrom,
            StartTo = startTo,
            EndFrom = endFrom,
            EndTo = endTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = page,
            PageSize = pageSize
        };

        var data = await service.GetSubscriptionsAsync(request, cancellationToken);
        return Ok(ApiResponse<PagedSubscriptionResultDto>.Ok(data));
    }

    [HttpGet("subscriptions/export")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SubscriptionListItemDto>>>> ExportSubscriptions(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] string? planType,
        [FromQuery] string? status,
        [FromQuery] string? paymentStatus,
        [FromQuery] bool? isExtraSlot,
        [FromQuery] DateTime? startFrom,
        [FromQuery] DateTime? startTo,
        [FromQuery] DateTime? endFrom,
        [FromQuery] DateTime? endTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken = default)
    {
        var request = new SubscriptionListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            PlanType = planType,
            Status = status,
            PaymentStatus = paymentStatus,
            IsExtraSlot = isExtraSlot,
            StartFrom = startFrom,
            StartTo = startTo,
            EndFrom = endFrom,
            EndTo = endTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = 1,
            PageSize = 5000
        };

        var data = await service.ExportSubscriptionsAsync(request, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SubscriptionListItemDto>>.Ok(data));
    }

    [HttpGet("subscriptions/{subscriptionId:int}")]
    public async Task<ActionResult<ApiResponse<SubscriptionListItemDto>>> GetSubscription(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.GetSubscriptionByIdAsync(subscriptionId, cancellationToken);
            return Ok(ApiResponse<SubscriptionListItemDto>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<SubscriptionListItemDto>.Fail(ex.Message));
        }
    }

    [HttpPost("subscriptions")]
    public async Task<ActionResult<ApiResponse<SubscriptionListItemDto>>> CreateSubscription([FromBody] CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CreateSubscriptionAsync(request, cancellationToken);
            return Ok(ApiResponse<SubscriptionListItemDto>.Ok(data, "Subscription invoice created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SubscriptionListItemDto>.Fail(ex.Message));
        }
    }

    [HttpPut("subscriptions/{subscriptionId:int}")]
    public async Task<ActionResult<ApiResponse<SubscriptionListItemDto>>> UpdateSubscription(int subscriptionId, [FromBody] UpdateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.UpdateSubscriptionAsync(subscriptionId, request, cancellationToken);
            return Ok(ApiResponse<SubscriptionListItemDto>.Ok(data, "Subscription updated."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SubscriptionListItemDto>.Fail(ex.Message));
        }
    }

    [HttpPost("subscriptions/{subscriptionId:int}/cancel")]
    public async Task<ActionResult<ApiResponse<SubscriptionListItemDto>>> CancelSubscription(int subscriptionId, [FromBody] CancelSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CancelSubscriptionAsync(subscriptionId, request, cancellationToken);
            return Ok(ApiResponse<SubscriptionListItemDto>.Ok(data, "Subscription cancelled."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SubscriptionListItemDto>.Fail(ex.Message));
        }
    }

    [HttpPost("subscriptions/{subscriptionId:int}/renew")]
    public async Task<ActionResult<ApiResponse<SubscriptionListItemDto>>> RenewSubscription(int subscriptionId, [FromBody] RenewSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.RenewSubscriptionAsync(subscriptionId, request, cancellationToken);
            return Ok(ApiResponse<SubscriptionListItemDto>.Ok(data, "Subscription renewed."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SubscriptionListItemDto>.Fail(ex.Message));
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



    [HttpGet("invoices")]
    public async Task<ActionResult<ApiResponse<PagedInvoiceResultDto>>> GetInvoices(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] string? invoiceType,
        [FromQuery] string? status,
        [FromQuery] DateTime? invoiceFrom,
        [FromQuery] DateTime? invoiceTo,
        [FromQuery] DateTime? dueFrom,
        [FromQuery] DateTime? dueTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var request = new InvoiceListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            InvoiceType = invoiceType,
            Status = status,
            InvoiceFrom = invoiceFrom,
            InvoiceTo = invoiceTo,
            DueFrom = dueFrom,
            DueTo = dueTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = page,
            PageSize = pageSize
        };

        var data = await service.GetInvoicesAsync(request, cancellationToken);
        return Ok(ApiResponse<PagedInvoiceResultDto>.Ok(data));
    }

    [HttpGet("invoices/export")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<InvoiceListItemDto>>>> ExportInvoices(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] string? invoiceType,
        [FromQuery] string? status,
        [FromQuery] DateTime? invoiceFrom,
        [FromQuery] DateTime? invoiceTo,
        [FromQuery] DateTime? dueFrom,
        [FromQuery] DateTime? dueTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken = default)
    {
        var request = new InvoiceListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            InvoiceType = invoiceType,
            Status = status,
            InvoiceFrom = invoiceFrom,
            InvoiceTo = invoiceTo,
            DueFrom = dueFrom,
            DueTo = dueTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = 1,
            PageSize = 5000
        };

        var data = await service.ExportInvoicesAsync(request, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<InvoiceListItemDto>>.Ok(data));
    }

    [HttpGet("invoices/{invoiceId:int}")]
    public async Task<ActionResult<ApiResponse<InvoiceListItemDto>>> GetInvoice(int invoiceId, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.GetInvoiceByIdAsync(invoiceId, cancellationToken);
            return Ok(ApiResponse<InvoiceListItemDto>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<InvoiceListItemDto>.Fail(ex.Message));
        }
    }

    [HttpGet("invoices/{invoiceId:int}/payments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<InvoicePaymentDto>>>> GetInvoicePayments(int invoiceId, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.GetInvoicePaymentsAsync(invoiceId, cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<InvoicePaymentDto>>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<IReadOnlyList<InvoicePaymentDto>>.Fail(ex.Message));
        }
    }

    [HttpPost("invoices")]
    public async Task<ActionResult<ApiResponse<InvoiceListItemDto>>> CreateInvoice([FromBody] CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CreateInvoiceAsync(request, cancellationToken);
            return Ok(ApiResponse<InvoiceListItemDto>.Ok(data, "Invoice created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<InvoiceListItemDto>.Fail(ex.Message));
        }
    }

    [HttpPut("invoices/{invoiceId:int}")]
    public async Task<ActionResult<ApiResponse<InvoiceListItemDto>>> UpdateInvoice(int invoiceId, [FromBody] UpdateInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.UpdateInvoiceAsync(invoiceId, request, cancellationToken);
            return Ok(ApiResponse<InvoiceListItemDto>.Ok(data, "Invoice updated."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<InvoiceListItemDto>.Fail(ex.Message));
        }
    }

    [HttpPost("invoices/{invoiceId:int}/cancel")]
    public async Task<ActionResult<ApiResponse<InvoiceListItemDto>>> CancelInvoice(int invoiceId, [FromBody] CancelInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CancelInvoiceAsync(invoiceId, request, cancellationToken);
            return Ok(ApiResponse<InvoiceListItemDto>.Ok(data, "Invoice cancelled."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<InvoiceListItemDto>.Fail(ex.Message));
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
