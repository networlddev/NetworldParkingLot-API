using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetworldParkingLot.Api.Common;
using NetworldParkingLot.Api.Common.Security;
using NetworldParkingLot.Api.Domain.Constants;
using NetworldParkingLot.Api.Features.GateOperations.Dtos;
using NetworldParkingLot.Api.Features.GateOperations.Services;
using NetworldParkingLot.Api.Features.SystemActivity.Services;
using NetworldParkingLot.Api.Features.UserAccess.Filters;

namespace NetworldParkingLot.Api.Features.GateOperations.Controllers;

[Authorize]
[ApiController]
[Route("api/gate-operation")]
public sealed class GateOperationsController(IGateOperationService service, ISystemActivityService activityService) : ControllerBase
{
    [RequireParkingPermission("dashboard", "view")]
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<GateSummaryDto>>> GetSummary(CancellationToken cancellationToken)
    {
        var data = await service.GetSummaryAsync(cancellationToken);
        return Ok(ApiResponse<GateSummaryDto>.Ok(data));
    }

    [RequireParkingPermission("companies", "view")]
    [HttpGet("companies/search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CompanySearchDto>>>> SearchCompanies([FromQuery] string searchText, CancellationToken cancellationToken)
    {
        var data = await service.SearchCompaniesAsync(searchText, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CompanySearchDto>>.Ok(data));
    }

    [RequireParkingPermission("companies", "view")]
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

    [RequireParkingPermission("companies", "export")]
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

    [RequireParkingPermission("companies", "create")]
    [HttpPost("companies")]
    public async Task<ActionResult<ApiResponse<CompanyListItemDto>>> CreateCompany([FromBody] CreateCompanyWithSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CreateCompanyWithSubscriptionAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<CompanyListItemDto>.Ok(data, "Company and subscription created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CompanyListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("companies", "edit")]
    [HttpPut("companies/{companyId:int}")]
    public async Task<ActionResult<ApiResponse<CompanyListItemDto>>> UpdateCompany(int companyId, [FromBody] UpdateCompanyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.UpdateCompanyAsync(companyId, StampOperator(request), cancellationToken);
            return Ok(ApiResponse<CompanyListItemDto>.Ok(data, "Company updated."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CompanyListItemDto>.Fail(ex.Message));
        }
    }



    [RequireParkingPermission("subscriptions", "view")]
    [HttpGet("subscriptions/rates")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExtraSlotRateDto>>>> GetSubscriptionRates(CancellationToken cancellationToken)
    {
        var data = await service.GetExtraSlotRatesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ExtraSlotRateDto>>.Ok(data));
    }

    [RequireParkingPermission("subscriptions", "view")]
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

    [RequireParkingPermission("subscriptions", "export")]
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

    [RequireParkingPermission("subscriptions", "view")]
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

    [RequireParkingPermission("subscriptions", "create")]
    [HttpPost("subscriptions")]
    public async Task<ActionResult<ApiResponse<SubscriptionListItemDto>>> CreateSubscription([FromBody] CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CreateSubscriptionAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<SubscriptionListItemDto>.Ok(data, "Subscription invoice created."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordSubscriptionFailureAsync("create", "ParkingSubscription", request.CompanyId > 0 ? request.CompanyId.ToString() : null, "Subscription creation failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<SubscriptionListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("subscriptions", "edit")]
    [HttpPut("subscriptions/{subscriptionId:int}")]
    public async Task<ActionResult<ApiResponse<SubscriptionListItemDto>>> UpdateSubscription(int subscriptionId, [FromBody] UpdateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.UpdateSubscriptionAsync(subscriptionId, StampOperator(request), cancellationToken);
            return Ok(ApiResponse<SubscriptionListItemDto>.Ok(data, "Subscription updated."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordSubscriptionFailureAsync("edit", "ParkingSubscription", subscriptionId.ToString(), "Subscription update failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<SubscriptionListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("subscriptions", "delete")]
    [HttpPost("subscriptions/{subscriptionId:int}/cancel")]
    public async Task<ActionResult<ApiResponse<SubscriptionListItemDto>>> CancelSubscription(int subscriptionId, [FromBody] CancelSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CancelSubscriptionAsync(subscriptionId, StampOperator(request), cancellationToken);
            return Ok(ApiResponse<SubscriptionListItemDto>.Ok(data, "Subscription cancelled."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordSubscriptionFailureAsync("cancel", "ParkingSubscription", subscriptionId.ToString(), "Subscription cancellation failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<SubscriptionListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("subscriptions", "create")]
    [HttpPost("subscriptions/{subscriptionId:int}/renew")]
    public async Task<ActionResult<ApiResponse<SubscriptionListItemDto>>> RenewSubscription(int subscriptionId, [FromBody] RenewSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.RenewSubscriptionAsync(subscriptionId, StampOperator(request), cancellationToken);
            return Ok(ApiResponse<SubscriptionListItemDto>.Ok(data, "Subscription renewed."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordSubscriptionFailureAsync("renew", "ParkingSubscription", subscriptionId.ToString(), "Subscription renewal failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<SubscriptionListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("gate_operation", "entry")]
    [HttpPost("entry/check-company")]
    public async Task<ActionResult<ApiResponse<CompanyGateStatusDto>>> CheckCompany([FromBody] CheckCompanyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CheckCompanyAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<CompanyGateStatusDto>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CompanyGateStatusDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("gate_operation", "entry")]
    [HttpPost("entry/generate-barcode")]
    public async Task<ActionResult<ApiResponse<GenerateBarcodeResponseDto>>> GenerateBarcode([FromBody] GenerateBarcodeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.GenerateBarcodeAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<GenerateBarcodeResponseDto>.Ok(data, "Barcode generated. Print sticker and allow entry."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordGateFailureAsync(ParkingConstants.GateActionType.BarcodeGenerated, "ParkingSession", null, "Barcode generation failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<GenerateBarcodeResponseDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("gate_operation", "entry")]
    [HttpPost("entry/allow")]
    public async Task<ActionResult<ApiResponse<EntryResultDto>>> AllowEntry([FromBody] AllowEntryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.AllowEntryAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<EntryResultDto>.Ok(data, "Entry allowed."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordGateFailureAsync(ParkingConstants.GateActionType.EntryAllowed, "ParkingSession", request.SessionId.ToString(), "Entry failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<EntryResultDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("gate_operation", "entry")]
    [HttpPost("entry/reject")]
    public async Task<ActionResult<ApiResponse<EntryResultDto>>> RejectEntry([FromBody] RejectEntryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.RejectEntryAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<EntryResultDto>.Ok(data, "Entry rejected."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordGateFailureAsync(ParkingConstants.GateActionType.EntryRejected, "ParkingSession", request.SessionId.ToString(), "Entry rejection failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<EntryResultDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("gate_operation", "exit")]
    [HttpPost("exit/scan")]
    public async Task<ActionResult<ApiResponse<ExitScanResultDto>>> ScanExit([FromBody] ScanExitRequest request, CancellationToken cancellationToken)
    {
        var data = await service.ScanExitBarcodeAsync(StampOperator(request), cancellationToken);
        return Ok(ApiResponse<ExitScanResultDto>.Ok(data, data.Message));
    }

    [RequireParkingPermission("gate_operation", "exit")]
    [HttpPost("exit/allow")]
    public async Task<ActionResult<ApiResponse<ExitResultDto>>> AllowExit([FromBody] AllowExitRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.AllowExitAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<ExitResultDto>.Ok(data, "Exit completed."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordGateFailureAsync(ParkingConstants.GateActionType.ExitAllowed, "ParkingSession", request.SessionId?.ToString() ?? request.BarcodeNo, "Exit failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<ExitResultDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("payments", "collect")]
    [HttpPost("payment/collect")]
    public async Task<ActionResult<ApiResponse<PaymentResultDto>>> CollectPayment([FromBody] CollectPaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CollectPaymentAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<PaymentResultDto>.Ok(data, "Payment collected."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordGateFailureAsync(ParkingConstants.GateActionType.PaymentCollected, request.InvoiceId.HasValue ? "ParkingInvoice" : "ParkingPayment", request.InvoiceId?.ToString() ?? request.SessionId?.ToString(), "Payment collection failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<PaymentResultDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("gate_operation", "entry")]
    [HttpGet("extra-slots/rates")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExtraSlotRateDto>>>> GetExtraSlotRates(CancellationToken cancellationToken)
    {
        var data = await service.GetExtraSlotRatesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ExtraSlotRateDto>>.Ok(data));
    }

    [RequireParkingPermission("invoices", "create")]
    [HttpPost("extra-slots/invoice")]
    public async Task<ActionResult<ApiResponse<ExtraSlotInvoiceResultDto>>> CreateExtraSlotInvoice([FromBody] CreateExtraSlotInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CreateExtraSlotInvoiceAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<ExtraSlotInvoiceResultDto>.Ok(data, "Extra slot invoice created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ExtraSlotInvoiceResultDto>.Fail(ex.Message));
        }
    }



    [RequireParkingPermission("invoices", "view")]
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

    [RequireParkingPermission("invoices", "export")]
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

    [RequireParkingPermission("invoices", "view")]
    [HttpGet("invoices/reconciliation")]
    public async Task<ActionResult<ApiResponse<InvoicePaymentReconciliationResultDto>>> GetInvoicePaymentReconciliation(
        [FromQuery] string? searchText,
        [FromQuery] int? companyId,
        [FromQuery] string? invoiceType,
        [FromQuery] bool mismatchOnly = true,
        [FromQuery] bool includeCancelled = false,
        [FromQuery] DateTime? invoiceFrom = null,
        [FromQuery] DateTime? invoiceTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var request = new InvoicePaymentReconciliationQueryRequest
        {
            SearchText = searchText,
            CompanyId = companyId,
            InvoiceType = invoiceType,
            MismatchOnly = mismatchOnly,
            IncludeCancelled = includeCancelled,
            InvoiceFrom = invoiceFrom,
            InvoiceTo = invoiceTo,
            Page = page,
            PageSize = pageSize
        };

        var data = await service.GetInvoicePaymentReconciliationAsync(request, cancellationToken);
        return Ok(ApiResponse<InvoicePaymentReconciliationResultDto>.Ok(data));
    }

    [RequireParkingPermission("invoices", "view")]
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

    [RequireParkingPermission("invoices", "view")]
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

    [RequireParkingPermission("invoices", "create")]
    [HttpPost("invoices")]
    public async Task<ActionResult<ApiResponse<InvoiceListItemDto>>> CreateInvoice([FromBody] CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CreateInvoiceAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<InvoiceListItemDto>.Ok(data, "Invoice created."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<InvoiceListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("invoices", "edit")]
    [HttpPut("invoices/{invoiceId:int}")]
    public async Task<ActionResult<ApiResponse<InvoiceListItemDto>>> UpdateInvoice(int invoiceId, [FromBody] UpdateInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.UpdateInvoiceAsync(invoiceId, StampOperator(request), cancellationToken);
            return Ok(ApiResponse<InvoiceListItemDto>.Ok(data, "Invoice updated."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<InvoiceListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("invoices", "delete")]
    [HttpPost("invoices/{invoiceId:int}/cancel")]
    public async Task<ActionResult<ApiResponse<InvoiceListItemDto>>> CancelInvoice(int invoiceId, [FromBody] CancelInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.CancelInvoiceAsync(invoiceId, StampOperator(request), cancellationToken);
            return Ok(ApiResponse<InvoiceListItemDto>.Ok(data, "Invoice cancelled."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<InvoiceListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("invoices", "view")]
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

    [RequireParkingPermission("dashboard", "view")]
    [HttpGet("recent-activity")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RecentActivityDto>>>> GetRecentActivity([FromQuery] int take = 25, CancellationToken cancellationToken = default)
    {
        var data = await service.GetRecentActivityAsync(take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<RecentActivityDto>>.Ok(data));
    }

    [RequireParkingPermission("live_parking", "view")]
    [HttpGet("live-parking")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LiveParkingDto>>>> GetLiveParking([FromQuery] string? searchText = null, CancellationToken cancellationToken = default)
    {
        var data = await service.GetLiveParkingAsync(searchText, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<LiveParkingDto>>.Ok(data));
    }



    [RequireParkingPermission("live_parking", "view")]
    [HttpGet("live-parking/list")]
    public async Task<ActionResult<ApiResponse<PagedLiveParkingResultDto>>> GetLiveParkingPaged(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] string? status,
        [FromQuery] string? paymentStatus,
        [FromQuery] bool? overstayOnly,
        [FromQuery] DateTime? entryFrom,
        [FromQuery] DateTime? entryTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var request = new LiveParkingListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            Status = status,
            PaymentStatus = paymentStatus,
            OverstayOnly = overstayOnly,
            EntryFrom = entryFrom,
            EntryTo = entryTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = page,
            PageSize = pageSize
        };

        var data = await service.GetLiveParkingPagedAsync(request, cancellationToken);
        return Ok(ApiResponse<PagedLiveParkingResultDto>.Ok(data));
    }

    [RequireParkingPermission("live_parking", "export")]
    [HttpGet("live-parking/export")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LiveParkingListItemDto>>>> ExportLiveParking(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] string? status,
        [FromQuery] string? paymentStatus,
        [FromQuery] bool? overstayOnly,
        [FromQuery] DateTime? entryFrom,
        [FromQuery] DateTime? entryTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken = default)
    {
        var request = new LiveParkingListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            Status = status,
            PaymentStatus = paymentStatus,
            OverstayOnly = overstayOnly,
            EntryFrom = entryFrom,
            EntryTo = entryTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = 1,
            PageSize = 5000
        };

        var data = await service.ExportLiveParkingAsync(request, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<LiveParkingListItemDto>>.Ok(data));
    }

    [RequireParkingPermission("live_parking", "view")]
    [HttpGet("live-parking/{sessionId:int}")]
    public async Task<ActionResult<ApiResponse<VehicleBarcodeDetailDto>>> GetLiveParkingDetail(int sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.GetVehicleBarcodeDetailAsync(sessionId, cancellationToken);
            return Ok(ApiResponse<VehicleBarcodeDetailDto>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<VehicleBarcodeDetailDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("gate_operation", "exit")]
    [HttpPost("live-parking/{sessionId:int}/force-exit")]
    public async Task<ActionResult<ApiResponse<ExitResultDto>>> ForceExitLiveParking(int sessionId, [FromBody] AllowExitRequest request, CancellationToken cancellationToken)
    {
        try
        {
            request.SessionId = sessionId;
            request.ForceAllow = true;
            var data = await service.AllowExitAsync(StampOperator(request), cancellationToken);
            return Ok(ApiResponse<ExitResultDto>.Ok(data, "Force exit completed."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordGateFailureAsync(ParkingConstants.GateActionType.ExitAllowed, "ParkingSession", sessionId.ToString(), "Force exit failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<ExitResultDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("payments", "view")]
    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<PagedPaymentResultDto>>> GetPayments(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] int? invoiceId,
        [FromQuery] int? sessionId,
        [FromQuery] string? paymentMode,
        [FromQuery] string? paymentType,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var request = new PaymentListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            InvoiceId = invoiceId,
            SessionId = sessionId,
            PaymentMode = paymentMode,
            PaymentType = paymentType,
            DateFrom = dateFrom,
            DateTo = dateTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = page,
            PageSize = pageSize
        };

        var data = await service.GetPaymentsAsync(request, cancellationToken);
        return Ok(ApiResponse<PagedPaymentResultDto>.Ok(data));
    }

    [RequireParkingPermission("payments", "export")]
    [HttpGet("payments/export")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PaymentListItemDto>>>> ExportPayments(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] int? invoiceId,
        [FromQuery] int? sessionId,
        [FromQuery] string? paymentMode,
        [FromQuery] string? paymentType,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken = default)
    {
        var request = new PaymentListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            InvoiceId = invoiceId,
            SessionId = sessionId,
            PaymentMode = paymentMode,
            PaymentType = paymentType,
            DateFrom = dateFrom,
            DateTo = dateTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = 1,
            PageSize = 5000
        };

        var data = await service.ExportPaymentsAsync(request, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PaymentListItemDto>>.Ok(data));
    }

    [RequireParkingPermission("payments", "view")]
    [HttpGet("payments/{paymentId:int}")]
    public async Task<ActionResult<ApiResponse<PaymentListItemDto>>> GetPayment(int paymentId, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.GetPaymentByIdAsync(paymentId, cancellationToken);
            return Ok(ApiResponse<PaymentListItemDto>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<PaymentListItemDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("payments", "collect")]
    [HttpPost("payments/collect")]
    public async Task<ActionResult<ApiResponse<PaymentResultDto>>> CollectPaymentFromPayments([FromBody] CollectPaymentRequest request, CancellationToken cancellationToken)
    {
        return await CollectPayment(request, cancellationToken);
    }

    [RequireParkingPermission("vehicles", "view")]
    [HttpGet("vehicles/barcodes")]
    public async Task<ActionResult<ApiResponse<PagedVehicleBarcodeResultDto>>> GetVehicleBarcodes(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] string? status,
        [FromQuery] string? barcodeStatus,
        [FromQuery] string? vehicleType,
        [FromQuery] DateTime? entryFrom,
        [FromQuery] DateTime? entryTo,
        [FromQuery] DateTime? exitFrom,
        [FromQuery] DateTime? exitTo,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var request = new VehicleBarcodeListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            Status = status,
            BarcodeStatus = barcodeStatus,
            VehicleType = vehicleType,
            EntryFrom = entryFrom,
            EntryTo = entryTo,
            ExitFrom = exitFrom,
            ExitTo = exitTo,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = page,
            PageSize = pageSize
        };

        var data = await service.GetVehicleBarcodesAsync(request, cancellationToken);
        return Ok(ApiResponse<PagedVehicleBarcodeResultDto>.Ok(data));
    }

    [RequireParkingPermission("vehicles", "export")]
    [HttpGet("vehicles/barcodes/export")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VehicleBarcodeListItemDto>>>> ExportVehicleBarcodes(
        [FromQuery] string? searchText,
        [FromQuery] string? tab,
        [FromQuery] int? companyId,
        [FromQuery] string? status,
        [FromQuery] string? barcodeStatus,
        [FromQuery] string? vehicleType,
        [FromQuery] DateTime? entryFrom,
        [FromQuery] DateTime? entryTo,
        [FromQuery] DateTime? exitFrom,
        [FromQuery] DateTime? exitTo,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken = default)
    {
        var request = new VehicleBarcodeListQueryRequest
        {
            SearchText = searchText,
            Tab = tab,
            CompanyId = companyId,
            Status = status,
            BarcodeStatus = barcodeStatus,
            VehicleType = vehicleType,
            EntryFrom = entryFrom,
            EntryTo = entryTo,
            ExitFrom = exitFrom,
            ExitTo = exitTo,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            SortBy = sortBy,
            SortDirection = sortDirection,
            Page = 1,
            PageSize = 5000
        };

        var data = await service.ExportVehicleBarcodesAsync(request, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<VehicleBarcodeListItemDto>>.Ok(data));
    }

    [RequireParkingPermission("vehicles", "view")]
    [HttpGet("vehicles/barcodes/{sessionId:int}")]
    public async Task<ActionResult<ApiResponse<VehicleBarcodeDetailDto>>> GetVehicleBarcode(int sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.GetVehicleBarcodeDetailAsync(sessionId, cancellationToken);
            return Ok(ApiResponse<VehicleBarcodeDetailDto>.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<VehicleBarcodeDetailDto>.Fail(ex.Message));
        }
    }

    [RequireParkingPermission("vehicles", "edit")]
    [HttpPost("vehicles/barcodes/{sessionId:int}/mark-invalid")]
    public async Task<ActionResult<ApiResponse<VehicleBarcodeDetailDto>>> MarkVehicleBarcodeInvalid(int sessionId, [FromBody] MarkBarcodeInvalidRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = await service.MarkVehicleBarcodeInvalidAsync(sessionId, StampOperator(request), cancellationToken);
            return Ok(ApiResponse<VehicleBarcodeDetailDto>.Ok(data, "Barcode marked invalid."));
        }
        catch (InvalidOperationException ex)
        {
            await RecordGateFailureAsync("BarcodeInvalidated", "ParkingSession", sessionId.ToString(), "Barcode invalidation failed", ex.Message, cancellationToken);
            return BadRequest(ApiResponse<VehicleBarcodeDetailDto>.Fail(ex.Message));
        }
    }

    [AllowAnonymous]
    [HttpGet("outside-display/latest")]
    public async Task<ActionResult<ApiResponse<OutsideDisplayDto?>>> GetLatestOutsideDisplay(CancellationToken cancellationToken)
    {
        var data = await service.GetLatestOutsideDisplayAsync(cancellationToken);
        return Ok(ApiResponse<OutsideDisplayDto?>.Ok(data));
    }

    [AllowAnonymous]
    [HttpGet("outside-display/recent-scans")]

    public async Task<ActionResult<ApiResponse<IReadOnlyList<OutsideDisplayDto>>>> GetRecentOutsideDisplayScans(
    [FromQuery] int take = 30,
    CancellationToken cancellationToken = default)

    {

        var data = await service.GetRecentOutsideDisplayScansAsync(take, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<OutsideDisplayDto>>.Ok(data));

    }

    private T StampOperator<T>(T request) where T : class
    {
        var operatorId = this.CurrentUserId();
        var property = typeof(T).GetProperty("OperatorId");
        if (property is { CanWrite: true } && property.PropertyType == typeof(int))
            property.SetValue(request, operatorId);
        return request;
    }

    private Task RecordGateFailureAsync(string actionKey, string entityType, string? entityId, string title, string message, CancellationToken cancellationToken)
    {
        return activityService.RecordAsync(this.BuildActivity(
            "gate_operation",
            actionKey,
            "Failure",
            entityType,
            entityId,
            title,
            message), cancellationToken);
    }

    private Task RecordSubscriptionFailureAsync(string actionKey, string entityType, string? entityId, string title, string message, CancellationToken cancellationToken)
    {
        return activityService.RecordAsync(this.BuildActivity(
            "subscriptions",
            actionKey,
            "Failure",
            entityType,
            entityId,
            title,
            message), cancellationToken);
    }
}
