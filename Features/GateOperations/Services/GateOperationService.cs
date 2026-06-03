using System.Data;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Domain.Constants;
using NetworldParkingLot.Api.Domain.Entities;
using NetworldParkingLot.Api.Features.GateOperations.Dtos;
using NetworldParkingLot.Api.Features.GateOperations.Repositories;
using NetworldParkingLot.Api.Features.SystemActivity.Services;

namespace NetworldParkingLot.Api.Features.GateOperations.Services;

public sealed class GateOperationService(NetworldParkingDbContext db, IGateRepository repository) : IGateOperationService
{
    public async Task<GateSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var totalCapacity = GetIntSetting(settings, "TotalParkingCapacity", 500);
        var now = DateTime.Now;
        var today = now.Date;

        await ReassignInsideSessionsToActiveSubscriptionsAsync(null, 0, cancellationToken);

        var inside = await db.ParkingSessions.CountAsync(x => x.Status == ParkingConstants.SessionStatus.Inside, cancellationToken);
        var todayEntries = await db.ParkingSessions.CountAsync(x => x.EntryTime != null && x.EntryTime.Value.Date == today, cancellationToken);
        var todayExits = await db.ParkingSessions.CountAsync(x => x.ExitTime != null && x.ExitTime.Value.Date == today, cancellationToken);
        var overstay = await db.ParkingSessions
            .Include(x => x.Subscription)
            .CountAsync(x => x.Status == ParkingConstants.SessionStatus.Inside &&
                             x.Subscription != null &&
                             x.Subscription.EndDate.Date < today, cancellationToken);

        var companiesWithPending = await db.ParkingInvoices
            .Where(x => x.BalanceAmount > 0)
            .Select(x => x.CompanyId)
            .Distinct()
            .CountAsync(cancellationToken);

        return new GateSummaryDto(
            totalCapacity,
            inside,
            Math.Max(totalCapacity - inside, 0),
            todayEntries,
            todayExits,
            companiesWithPending,
            overstay);
    }

    public async Task<IReadOnlyList<CompanySearchDto>> SearchCompaniesAsync(string searchText, CancellationToken cancellationToken = default)
    {
        searchText = (searchText ?? string.Empty).Trim();
        if (searchText.Length == 0)
            return [];

        var companies = await db.ParkingCompanies
            .Where(x => x.CompanyName.Contains(searchText) ||
                        x.CompanyCode.Contains(searchText) ||
                        (x.Mobile != null && x.Mobile.Contains(searchText)) ||
                        db.ParkingSessions.Any(s => s.CompanyId == x.CompanyId && s.PlateNo != null && s.PlateNo.Contains(searchText)))
            .OrderBy(x => x.CompanyName)
            .Take(20)
            .ToListAsync(cancellationToken);

        var result = new List<CompanySearchDto>();
        foreach (var company in companies)
        {
            var status = await BuildCompanyStatusAsync(company, 0, cancellationToken);
            result.Add(new CompanySearchDto(
                status.CompanyId,
                status.CompanyCode,
                status.CompanyName,
                status.ContactPerson,
                status.Mobile,
                status.CompanyStatus,
                status.PurchasedSlots,
                status.VehiclesInside,
                status.AvailableSlots,
                status.PendingAmount,
                status.PaymentStatus,
                status.SubscriptionStatus,
                status.SubscriptionStartDate,
                status.SubscriptionEndDate));
        }

        return result;
    }

    public async Task<PagedResultDto<CompanyListItemDto>> GetCompaniesAsync(CompanyListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = Math.Max(request.Page, 1);
        request.PageSize = Math.Clamp(request.PageSize, 10, 100);

        var all = await BuildCompanyListItemsAsync(request, cancellationToken);
        var sorted = SortCompanies(all, request.SortBy, request.SortDirection).ToList();
        var total = sorted.Count;
        var totalPages = Math.Max((int)Math.Ceiling(total / (double)request.PageSize), 1);
        var page = Math.Min(request.Page, totalPages);
        var items = sorted
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new PagedResultDto<CompanyListItemDto>(
            items,
            page,
            request.PageSize,
            total,
            totalPages,
            sorted.Sum(x => x.PendingAmount),
            sorted.Sum(x => x.PurchasedSlots),
            sorted.Sum(x => x.VehiclesInside),
            sorted.Sum(x => x.AvailableSlots));
    }

    public async Task<IReadOnlyList<CompanyListItemDto>> ExportCompaniesAsync(CompanyListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = 1;
        request.PageSize = 5000;
        var all = await BuildCompanyListItemsAsync(request, cancellationToken);
        return SortCompanies(all, request.SortBy, request.SortDirection).Take(5000).ToList();
    }

    public async Task<CompanyListItemDto> CreateCompanyWithSubscriptionAsync(CreateCompanyWithSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var companyName = (request.CompanyName ?? string.Empty).Trim();
        if (companyName.Length == 0)
            throw new InvalidOperationException("Company name is required.");

        ValidateCompanyContact(request.Mobile, request.Email);

        var planType = NormalizeExtraSlotPlanType(request.PlanType);
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var ratePerSlot = GetExtraSlotRate(settings, planType);
        if (ratePerSlot <= 0)
            throw new InvalidOperationException($"{planType} subscription rate is not configured. Please update settings first.");

        var companyCode = (request.CompanyCode ?? string.Empty).Trim().ToUpperInvariant();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        if (companyCode.Length == 0)
            companyCode = await GenerateNextCompanyCodeAsync(cancellationToken);

        var duplicateCode = await db.ParkingCompanies.AnyAsync(x => x.CompanyCode == companyCode, cancellationToken);
        if (duplicateCode)
            throw new InvalidOperationException($"Company code {companyCode} already exists.");

        var duplicateName = await db.ParkingCompanies.AnyAsync(x => x.CompanyName == companyName, cancellationToken);
        if (duplicateName)
            throw new InvalidOperationException($"Company name {companyName} already exists.");

        var startDate = request.StartDate == default ? DateTime.Today : request.StartDate.Date;
        var endDate = CalculateExtraSlotEndDate(planType, startDate);
        var subTotal = request.SlotsPurchased * ratePerSlot;
        var total = subTotal - request.DiscountAmount + request.VatAmount;
        if (total < 0)
            throw new InvalidOperationException("Invoice total cannot be negative.");

        var paid = ValidateInitialPaidAmount(request.PaidAmount, total);
        var balance = total - paid;

        var company = new ParkingCompany
        {
            CompanyCode = companyCode,
            CompanyName = companyName,
            ContactPerson = TrimOrNull(request.ContactPerson),
            Mobile = TrimOrNull(request.Mobile),
            Email = TrimOrNull(request.Email),
            Address = TrimOrNull(request.Address),
            TradeLicenseNo = TrimOrNull(request.TradeLicenseNo),
            Trn = TrimOrNull(request.Trn),
            Status = NormalizeCompanyStatus(request.Status),
            OpeningBalance = Math.Max(request.OpeningBalance, 0),
            BillingName = TrimOrNull(request.BillingName),
            PaymentTerms = TrimOrNull(request.PaymentTerms),
            CreditLimit = Math.Max(request.CreditLimit, 0),
            Remarks = TrimOrNull(request.Remarks),
            InternalNotes = TrimOrNull(request.InternalNotes),
            CreatedBy = request.OperatorId,
            CreatedDate = DateTime.Now
        };

        await db.ParkingCompanies.AddAsync(company, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var subscription = new ParkingSubscription
        {
            CompanyId = company.CompanyId,
            PlanType = planType,
            SlotsPurchased = request.SlotsPurchased,
            RatePerSlot = ratePerSlot,
            StartDate = startDate,
            EndDate = endDate,
            DiscountAmount = request.DiscountAmount,
            VatAmount = request.VatAmount,
            TotalAmount = total,
            PaidAmount = paid,
            BalanceAmount = balance,
            Status = ParkingConstants.SubscriptionStatus.Active,
            IsExtraSlot = false,
            CreatedBy = request.OperatorId,
            CreatedDate = DateTime.Now
        };

        await db.ParkingSubscriptions.AddAsync(subscription, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var invoiceNo = await GenerateNextInvoiceNoAsync(cancellationToken);
        var invoice = new ParkingInvoice
        {
            InvoiceNo = invoiceNo,
            CompanyId = company.CompanyId,
            SubscriptionId = subscription.SubscriptionId,
            InvoiceType = "Subscription",
            InvoiceDate = DateTime.Now,
            DueDate = endDate,
            PlanType = planType,
            Slots = request.SlotsPurchased,
            SubTotal = subTotal,
            DiscountAmount = request.DiscountAmount,
            VatAmount = request.VatAmount,
            TotalAmount = total,
            PaidAmount = paid,
            BalanceAmount = balance,
            Status = GetInvoiceStatus(balance, paid),
            Remarks = $"Initial {planType} subscription created with company.",
            CreatedBy = request.OperatorId,
            CreatedDate = DateTime.Now
        };

        await db.ParkingInvoices.AddAsync(invoice, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        subscription.SourceInvoiceId = invoice.InvoiceId;

        if (paid > 0)
        {
            var receiptNo = await GenerateNextReceiptNoAsync(cancellationToken);
            await db.ParkingPayments.AddAsync(new ParkingPayment
            {
                ReceiptNo = receiptNo,
                CompanyId = company.CompanyId,
                InvoiceId = invoice.InvoiceId,
                PaymentType = "Subscription",
                Amount = paid,
                PaymentMode = NormalizePaymentMode(request.PaymentMode),
                ReferenceNo = TrimOrNull(request.ReferenceNo),
                ReceivedBy = request.OperatorId,
                PaymentDate = DateTime.Now,
                Remarks = "Payment collected while creating company subscription."
            }, cancellationToken);
        }

        await AddActivityAsync("CompanyCreated", company.CompanyId, null, null, null, "Company Created", $"Company {company.CompanyName} created with {planType} subscription.", request.OperatorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var filter = new CompanyListQueryRequest { SearchText = company.CompanyCode, Page = 1, PageSize = 1 };
        return (await BuildCompanyListItemsAsync(filter, cancellationToken)).First();
    }

    public async Task<CompanyListItemDto> UpdateCompanyAsync(int companyId, UpdateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var company = await db.ParkingCompanies.FirstOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken)
            ?? throw new InvalidOperationException("Company not found.");

        var companyName = (request.CompanyName ?? string.Empty).Trim();
        if (companyName.Length == 0)
            throw new InvalidOperationException("Company name is required.");

        ValidateCompanyContact(request.Mobile, request.Email);

        var duplicateName = await db.ParkingCompanies.AnyAsync(x => x.CompanyId != companyId && x.CompanyName == companyName, cancellationToken);
        if (duplicateName)
            throw new InvalidOperationException($"Company name {companyName} already exists.");

        company.CompanyName = companyName;
        company.ContactPerson = TrimOrNull(request.ContactPerson);
        company.Mobile = TrimOrNull(request.Mobile);
        company.Email = TrimOrNull(request.Email);
        company.Address = TrimOrNull(request.Address);
        company.TradeLicenseNo = TrimOrNull(request.TradeLicenseNo);
        company.Trn = TrimOrNull(request.Trn);
        company.Status = NormalizeCompanyStatus(request.Status);
        company.OpeningBalance = Math.Max(request.OpeningBalance, 0);
        company.BillingName = TrimOrNull(request.BillingName);
        company.PaymentTerms = TrimOrNull(request.PaymentTerms);
        company.CreditLimit = Math.Max(request.CreditLimit, 0);
        company.Remarks = TrimOrNull(request.Remarks);
        company.InternalNotes = TrimOrNull(request.InternalNotes);
        company.ModifiedBy = request.OperatorId;
        company.ModifiedDate = DateTime.Now;

        await AddActivityAsync("CompanyUpdated", company.CompanyId, null, null, null, "Company Updated", $"Company {company.CompanyName} updated.", request.OperatorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var filter = new CompanyListQueryRequest { SearchText = company.CompanyCode, Page = 1, PageSize = 1 };
        return (await BuildCompanyListItemsAsync(filter, cancellationToken)).First();
    }



    public async Task<PagedSubscriptionResultDto> GetSubscriptionsAsync(SubscriptionListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = Math.Max(request.Page, 1);
        request.PageSize = Math.Clamp(request.PageSize, 10, 100);

        var all = await BuildSubscriptionListItemsAsync(request, cancellationToken);
        var sorted = SortSubscriptions(all, request.SortBy, request.SortDirection).ToList();
        var total = sorted.Count;
        var totalPages = Math.Max((int)Math.Ceiling(total / (double)request.PageSize), 1);
        var page = Math.Min(request.Page, totalPages);
        var items = sorted
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var today = DateTime.Today;
        return new PagedSubscriptionResultDto(
            items,
            page,
            request.PageSize,
            total,
            totalPages,
            sorted.Sum(x => x.TotalAmount),
            sorted.Sum(x => x.BalanceAmount),
            sorted.Sum(x => x.SlotsPurchased),
            sorted.Where(x => x.Status.Equals(ParkingConstants.SubscriptionStatus.Active, StringComparison.OrdinalIgnoreCase) && x.StartDate.Date <= today && x.EndDate.Date >= today).Sum(x => x.SlotsPurchased),
            sorted.Count(x => x.Status.Equals(ParkingConstants.SubscriptionStatus.Expired, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.Status.Equals(ParkingConstants.SubscriptionStatus.Active, StringComparison.OrdinalIgnoreCase) && x.EndDate.Date >= today && x.EndDate.Date <= today.AddDays(7)));
    }

    public async Task<IReadOnlyList<SubscriptionListItemDto>> ExportSubscriptionsAsync(SubscriptionListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = 1;
        request.PageSize = 5000;
        var all = await BuildSubscriptionListItemsAsync(request, cancellationToken);
        return SortSubscriptions(all, request.SortBy, request.SortDirection).Take(5000).ToList();
    }

    public async Task<SubscriptionListItemDto> GetSubscriptionByIdAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var item = (await BuildSubscriptionListItemsAsync(new SubscriptionListQueryRequest(), cancellationToken))
            .FirstOrDefault(x => x.SubscriptionId == subscriptionId);
        return item ?? throw new InvalidOperationException("Subscription not found.");
    }

    public async Task<SubscriptionListItemDto> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var company = await db.ParkingCompanies.FirstOrDefaultAsync(x => x.CompanyId == request.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("Company not found.");

        if (!company.Status.Equals(ParkingConstants.CompanyStatus.Active, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot create subscription for inactive or blocked company.");

        var settings = await repository.GetSettingsAsync(cancellationToken);
        var planType = NormalizeExtraSlotPlanType(request.PlanType);
        var ratePerSlot = GetExtraSlotRate(settings, planType);
        if (ratePerSlot <= 0)
            throw new InvalidOperationException($"{planType} subscription rate is not configured. Please update settings first.");

        if (request.SlotsPurchased <= 0)
            throw new InvalidOperationException("Slots purchased must be greater than zero.");

        var startDate = request.StartDate == default ? DateTime.Today : request.StartDate.Date;
        var endDate = request.EndDate.HasValue && request.EndDate.Value.Date > startDate
            ? request.EndDate.Value.Date
            : CalculateExtraSlotEndDate(planType, startDate);

        await EnsureRegularSubscriptionDoesNotOverlapAsync(
            company.CompanyId,
            null,
            request.IsExtraSlot,
            startDate,
            endDate,
            ParkingConstants.SubscriptionStatus.Active,
            cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var subscription = await CreateSubscriptionInvoiceInternalAsync(
            company,
            planType,
            request.SlotsPurchased,
            ratePerSlot,
            startDate,
            endDate,
            request.DiscountAmount,
            request.VatAmount,
            request.PaidAmount,
            request.PaymentMode,
            request.ReferenceNo,
            request.Remarks,
            request.IsExtraSlot,
            request.OperatorId,
            request.IsExtraSlot ? "ExtraSlot" : "Subscription",
            cancellationToken);

        await AddActivityAsync(
            request.IsExtraSlot ? ParkingConstants.GateActionType.ExtraSlotInvoice : "SubscriptionCreated",
            company.CompanyId,
            null,
            null,
            null,
            request.IsExtraSlot ? "Extra Slot Subscription" : "Subscription Created",
            $"{planType} subscription created with {request.SlotsPurchased} slot(s).",
            request.OperatorId,
            cancellationToken);

        await ReassignInsideSessionsToActiveSubscriptionsAsync(company.CompanyId, request.OperatorId, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetSubscriptionByIdAsync(subscription.SubscriptionId, cancellationToken);
    }

    public async Task<SubscriptionListItemDto> UpdateSubscriptionAsync(int subscriptionId, UpdateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var subscription = await db.ParkingSubscriptions
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId, cancellationToken)
            ?? throw new InvalidOperationException("Subscription not found.");

        if (subscription.Status.Equals(ParkingConstants.SubscriptionStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cancelled subscription cannot be edited. Create a new subscription or renew instead.");

        var invoice = await GetSubscriptionInvoiceForUpdateAsync(subscription.SubscriptionId, cancellationToken);
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var planType = NormalizeExtraSlotPlanType(request.PlanType);
        var ratePerSlot = GetExtraSlotRate(settings, planType);
        if (ratePerSlot <= 0)
            throw new InvalidOperationException($"{planType} subscription rate is not configured. Please update settings first.");

        if (request.SlotsPurchased <= 0)
            throw new InvalidOperationException("Slots purchased must be greater than zero.");

        var startDate = request.StartDate == default ? subscription.StartDate.Date : request.StartDate.Date;
        var endDate = request.EndDate.HasValue && request.EndDate.Value.Date > startDate
            ? request.EndDate.Value.Date
            : CalculateExtraSlotEndDate(planType, startDate);
        var normalizedStatus = NormalizeSubscriptionStatus(request.Status, startDate, endDate);
        var subTotal = request.SlotsPurchased * ratePerSlot;
        var total = subTotal - request.DiscountAmount + request.VatAmount;
        if (total < 0)
            throw new InvalidOperationException("Invoice total cannot be negative.");

        var paid = invoice?.PaidAmount ?? subscription.PaidAmount;
        if (paid > total)
            throw new InvalidOperationException($"New total AED {total:n2} cannot be less than already paid AED {paid:n2}.");

        await EnsureRegularSubscriptionDoesNotOverlapAsync(
            subscription.CompanyId,
            subscription.SubscriptionId,
            request.IsExtraSlot,
            startDate,
            endDate,
            normalizedStatus,
            cancellationToken);

        await EnsureSubscriptionEditKeepsInsideVehiclesCoveredAsync(
            subscription,
            request.SlotsPurchased,
            startDate,
            endDate,
            normalizedStatus,
            cancellationToken);

        var changes = new List<SystemActivityChange>
        {
            new("PlanType", subscription.PlanType, planType),
            new("SlotsPurchased", AuditValue(subscription.SlotsPurchased), AuditValue(request.SlotsPurchased)),
            new("RatePerSlot", AuditValue(subscription.RatePerSlot), AuditValue(ratePerSlot)),
            new("StartDate", AuditValue(subscription.StartDate), AuditValue(startDate)),
            new("EndDate", AuditValue(subscription.EndDate), AuditValue(endDate)),
            new("DiscountAmount", AuditValue(subscription.DiscountAmount), AuditValue(request.DiscountAmount)),
            new("VatAmount", AuditValue(subscription.VatAmount), AuditValue(request.VatAmount)),
            new("TotalAmount", AuditValue(subscription.TotalAmount), AuditValue(total)),
            new("BalanceAmount", AuditValue(subscription.BalanceAmount), AuditValue(total - paid)),
            new("IsExtraSlot", AuditValue(subscription.IsExtraSlot), AuditValue(request.IsExtraSlot)),
            new("Status", subscription.Status, normalizedStatus),
            new("Remarks", subscription.Remarks, TrimOrNull(request.Remarks))
        };

        if (invoice != null)
        {
            changes.Add(new("InvoiceType", invoice.InvoiceType, request.IsExtraSlot ? "ExtraSlot" : "Subscription"));
            changes.Add(new("InvoiceBalanceAmount", AuditValue(invoice.BalanceAmount), AuditValue(total - paid)));
            changes.Add(new("InvoiceStatus", invoice.Status, GetInvoiceStatus(total - paid, paid)));
        }

        subscription.PlanType = planType;
        subscription.SlotsPurchased = request.SlotsPurchased;
        subscription.RatePerSlot = ratePerSlot;
        subscription.StartDate = startDate;
        subscription.EndDate = endDate;
        subscription.DiscountAmount = request.DiscountAmount;
        subscription.VatAmount = request.VatAmount;
        subscription.TotalAmount = total;
        subscription.PaidAmount = paid;
        subscription.BalanceAmount = total - paid;
        subscription.IsExtraSlot = request.IsExtraSlot;
        subscription.Status = normalizedStatus;
        subscription.Remarks = TrimOrNull(request.Remarks);
        subscription.ModifiedBy = request.OperatorId;
        subscription.ModifiedDate = DateTime.Now;

        if (invoice != null)
        {
            invoice.InvoiceType = request.IsExtraSlot ? "ExtraSlot" : "Subscription";
            invoice.DueDate = endDate;
            invoice.PlanType = planType;
            invoice.Slots = request.SlotsPurchased;
            invoice.SubTotal = subTotal;
            invoice.DiscountAmount = request.DiscountAmount;
            invoice.VatAmount = request.VatAmount;
            invoice.TotalAmount = total;
            invoice.PaidAmount = paid;
            invoice.BalanceAmount = total - paid;
            invoice.Status = GetInvoiceStatus(invoice.BalanceAmount, invoice.PaidAmount);
            invoice.Remarks = TrimOrNull(request.Remarks);
        }

        await AddActivityAsync("SubscriptionUpdated", subscription.CompanyId, null, null, null, "Subscription Updated", $"Subscription {subscription.SubscriptionId} updated.", request.OperatorId, cancellationToken, changes, "ParkingSubscription", subscription.SubscriptionId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        await ReassignInsideSessionsToActiveSubscriptionsAsync(subscription.CompanyId, request.OperatorId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetSubscriptionByIdAsync(subscription.SubscriptionId, cancellationToken);
    }

    public async Task<SubscriptionListItemDto> CancelSubscriptionAsync(int subscriptionId, CancelSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var subscription = await db.ParkingSubscriptions.FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId, cancellationToken)
            ?? throw new InvalidOperationException("Subscription not found.");

        var hasVehiclesInside = await db.ParkingSessions.AnyAsync(x => x.SubscriptionId == subscriptionId && x.Status == ParkingConstants.SessionStatus.Inside, cancellationToken);
        if (hasVehiclesInside)
            throw new InvalidOperationException("Cannot cancel subscription while vehicles are still inside using this subscription.");

        var oldStatus = subscription.Status;
        var oldBalanceAmount = subscription.BalanceAmount;

        subscription.Status = ParkingConstants.SubscriptionStatus.Cancelled;
        subscription.CancelledBy = request.OperatorId;
        subscription.CancelledDate = DateTime.Now;
        subscription.CancellationReason = TrimOrNull(request.Reason);
        subscription.ModifiedBy = request.OperatorId;
        subscription.ModifiedDate = DateTime.Now;

        var invoice = await GetSubscriptionInvoiceForUpdateAsync(subscription.SubscriptionId, cancellationToken);
        var oldInvoiceStatus = invoice?.Status;
        var oldInvoiceBalanceAmount = invoice?.BalanceAmount;
        if (request.ClearPendingInvoiceBalance && invoice != null && invoice.BalanceAmount > 0)
        {
            invoice.BalanceAmount = 0;
            invoice.Status = "Cancelled";
            invoice.Remarks = AppendRemarks(invoice.Remarks, "Pending balance cleared due to subscription cancellation.");
            subscription.BalanceAmount = 0;
        }

        var changes = new List<SystemActivityChange>
        {
            new("Status", oldStatus, subscription.Status),
            new("BalanceAmount", AuditValue(oldBalanceAmount), AuditValue(subscription.BalanceAmount)),
            new("CancellationReason", null, subscription.CancellationReason)
        };
        if (invoice != null)
        {
            changes.Add(new("InvoiceStatus", oldInvoiceStatus, invoice.Status));
            changes.Add(new("InvoiceBalanceAmount", AuditValue(oldInvoiceBalanceAmount), AuditValue(invoice.BalanceAmount)));
        }

        await AddActivityAsync("SubscriptionCancelled", subscription.CompanyId, null, null, null, "Subscription Cancelled", request.Reason, request.OperatorId, cancellationToken, changes, "ParkingSubscription", subscription.SubscriptionId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return await GetSubscriptionByIdAsync(subscription.SubscriptionId, cancellationToken);
    }

    public async Task<SubscriptionListItemDto> RenewSubscriptionAsync(int subscriptionId, RenewSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await db.ParkingSubscriptions
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId, cancellationToken)
            ?? throw new InvalidOperationException("Subscription not found.");

        var planType = NormalizeExtraSlotPlanType(string.IsNullOrWhiteSpace(request.PlanType) ? existing.PlanType : request.PlanType);
        var startDate = request.StartDate?.Date ?? (existing.EndDate.Date >= DateTime.Today ? existing.EndDate.Date.AddDays(1) : DateTime.Today);
        var endDate = request.EndDate.HasValue && request.EndDate.Value.Date > startDate
            ? request.EndDate.Value.Date
            : CalculateExtraSlotEndDate(planType, startDate);
        var slots = request.SlotsPurchased.HasValue && request.SlotsPurchased.Value > 0 ? request.SlotsPurchased.Value : existing.SlotsPurchased;

        await EnsureRegularSubscriptionDoesNotOverlapAsync(
            existing.CompanyId,
            existing.SubscriptionId,
            existing.IsExtraSlot,
            startDate,
            endDate,
            ParkingConstants.SubscriptionStatus.Active,
            cancellationToken);

        var create = new CreateSubscriptionRequest
        {
            CompanyId = existing.CompanyId,
            PlanType = planType,
            SlotsPurchased = slots,
            IsExtraSlot = existing.IsExtraSlot,
            StartDate = startDate,
            EndDate = endDate,
            DiscountAmount = request.DiscountAmount,
            VatAmount = request.VatAmount,
            PaidAmount = request.PaidAmount,
            PaymentMode = request.PaymentMode,
            ReferenceNo = request.ReferenceNo,
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? $"Renewal of subscription #{existing.SubscriptionId}" : request.Remarks,
            OperatorId = request.OperatorId
        };

        var renewed = await CreateSubscriptionAsync(create, cancellationToken);
        await AddActivityAsync("SubscriptionRenewed", existing.CompanyId, null, null, null, "Subscription Renewed", $"Renewed subscription {existing.SubscriptionId} as {renewed.SubscriptionId}.", request.OperatorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return renewed;
    }

    private async Task<ParkingSubscription> CreateSubscriptionInvoiceInternalAsync(
        ParkingCompany company,
        string planType,
        int slotsPurchased,
        decimal ratePerSlot,
        DateTime startDate,
        DateTime endDate,
        decimal discountAmount,
        decimal vatAmount,
        decimal paidAmount,
        string paymentMode,
        string? referenceNo,
        string? remarks,
        bool isExtraSlot,
        int operatorId,
        string invoiceType,
        CancellationToken cancellationToken)
    {
        var subTotal = slotsPurchased * ratePerSlot;
        var total = subTotal - discountAmount + vatAmount;
        if (total < 0)
            throw new InvalidOperationException("Invoice total cannot be negative.");

        var paid = ValidateInitialPaidAmount(paidAmount, total);
        var balance = total - paid;
        var invoiceNo = await GenerateNextInvoiceNoAsync(cancellationToken);

        var subscription = new ParkingSubscription
        {
            CompanyId = company.CompanyId,
            PlanType = planType,
            SlotsPurchased = slotsPurchased,
            RatePerSlot = ratePerSlot,
            StartDate = startDate,
            EndDate = endDate,
            DiscountAmount = discountAmount,
            VatAmount = vatAmount,
            TotalAmount = total,
            PaidAmount = paid,
            BalanceAmount = balance,
            Status = ParkingConstants.SubscriptionStatus.Active,
            IsExtraSlot = isExtraSlot,
            Remarks = TrimOrNull(remarks),
            CreatedBy = operatorId,
            CreatedDate = DateTime.Now
        };
        await db.ParkingSubscriptions.AddAsync(subscription, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var invoice = new ParkingInvoice
        {
            InvoiceNo = invoiceNo,
            CompanyId = company.CompanyId,
            SubscriptionId = subscription.SubscriptionId,
            InvoiceType = invoiceType,
            InvoiceDate = DateTime.Now,
            DueDate = endDate,
            PlanType = planType,
            Slots = slotsPurchased,
            SubTotal = subTotal,
            DiscountAmount = discountAmount,
            VatAmount = vatAmount,
            TotalAmount = total,
            PaidAmount = paid,
            BalanceAmount = balance,
            Status = GetInvoiceStatus(balance, paid),
            Remarks = remarks,
            CreatedBy = operatorId,
            CreatedDate = DateTime.Now
        };
        await db.ParkingInvoices.AddAsync(invoice, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        subscription.SourceInvoiceId = invoice.InvoiceId;

        if (paid > 0)
        {
            var receiptNo = await GenerateNextReceiptNoAsync(cancellationToken);
            await db.ParkingPayments.AddAsync(new ParkingPayment
            {
                ReceiptNo = receiptNo,
                CompanyId = company.CompanyId,
                InvoiceId = invoice.InvoiceId,
                PaymentType = invoiceType,
                Amount = paid,
                PaymentMode = string.IsNullOrWhiteSpace(paymentMode) ? "Cash" : paymentMode.Trim(),
                ReferenceNo = referenceNo,
                ReceivedBy = operatorId,
                PaymentDate = DateTime.Now,
                Remarks = $"Payment collected while creating {invoiceType} subscription."
            }, cancellationToken);
        }

        return subscription;
    }

    private async Task<ParkingInvoice?> GetSubscriptionInvoiceForUpdateAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        return await db.ParkingInvoices
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.InvoiceId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<SubscriptionListItemDto>> BuildSubscriptionListItemsAsync(SubscriptionListQueryRequest request, CancellationToken cancellationToken)
    {
        var query = db.ParkingSubscriptions
            .AsNoTracking()
            .Include(x => x.Company)
            .AsQueryable();

        if (request.CompanyId.HasValue && request.CompanyId.Value > 0)
            query = query.Where(x => x.CompanyId == request.CompanyId.Value);

        var search = (request.SearchText ?? string.Empty).Trim();
        if (search.Length > 0)
        {
            query = query.Where(x => x.Company.CompanyName.Contains(search) ||
                                     x.Company.CompanyCode.Contains(search) ||
                                     x.PlanType.Contains(search));
        }

        var planType = (request.PlanType ?? string.Empty).Trim();
        if (planType.Length > 0 && !planType.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.PlanType == planType);

        if (request.IsExtraSlot.HasValue)
            query = query.Where(x => x.IsExtraSlot == request.IsExtraSlot.Value);

        if (request.StartFrom.HasValue)
            query = query.Where(x => x.StartDate >= request.StartFrom.Value.Date);
        if (request.StartTo.HasValue)
            query = query.Where(x => x.StartDate <= request.StartTo.Value.Date);
        if (request.EndFrom.HasValue)
            query = query.Where(x => x.EndDate >= request.EndFrom.Value.Date);
        if (request.EndTo.HasValue)
            query = query.Where(x => x.EndDate <= request.EndTo.Value.Date);

        var subscriptions = await query.ToListAsync(cancellationToken);
        if (subscriptions.Count == 0)
            return [];

        var subscriptionIds = subscriptions.Select(x => x.SubscriptionId).ToList();
        var companyIds = subscriptions.Select(x => x.CompanyId).Distinct().ToList();

        var invoices = await db.ParkingInvoices
            .AsNoTracking()
            .Where(x => x.SubscriptionId.HasValue && subscriptionIds.Contains(x.SubscriptionId.Value))
            .ToListAsync(cancellationToken);

        var invoiceBySubscription = invoices
            .GroupBy(x => x.SubscriptionId!.Value)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.InvoiceDate).ThenByDescending(y => y.InvoiceId).First());

        var insideByCompany = await db.ParkingSessions
            .AsNoTracking()
            .Where(x => companyIds.Contains(x.CompanyId) && x.Status == ParkingConstants.SessionStatus.Inside)
            .GroupBy(x => x.CompanyId)
            .Select(x => new { CompanyId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count, cancellationToken);

        var today = DateTime.Today;
        var items = new List<SubscriptionListItemDto>();
        foreach (var subscription in subscriptions)
        {
            invoiceBySubscription.TryGetValue(subscription.SubscriptionId, out var invoice);
            insideByCompany.TryGetValue(subscription.CompanyId, out var insideCount);
            var displayStatus = GetRuntimeSubscriptionStatus(subscription, today);
            var paymentStatus = invoice == null ? GetInvoiceStatus(subscription.BalanceAmount, subscription.PaidAmount) : invoice.Status;
            var durationDays = Math.Max(1, (subscription.EndDate.Date - subscription.StartDate.Date).Days);

            items.Add(new SubscriptionListItemDto(
                subscription.SubscriptionId,
                subscription.CompanyId,
                subscription.Company.CompanyCode,
                subscription.Company.CompanyName,
                subscription.PlanType,
                subscription.SlotsPurchased,
                subscription.RatePerSlot,
                subscription.StartDate,
                subscription.EndDate,
                durationDays,
                subscription.DiscountAmount,
                subscription.VatAmount,
                invoice?.TotalAmount ?? subscription.TotalAmount,
                invoice?.PaidAmount ?? subscription.PaidAmount,
                invoice?.BalanceAmount ?? subscription.BalanceAmount,
                paymentStatus,
                displayStatus,
                subscription.IsExtraSlot,
                invoice?.InvoiceId,
                invoice?.InvoiceNo,
                invoice?.Status,
                insideCount,
                subscription.CreatedDate,
                subscription.Remarks,
                subscription.CancellationReason));
        }

        return ApplySubscriptionListPostFilters(items, request).ToList();
    }

    private static IEnumerable<SubscriptionListItemDto> ApplySubscriptionListPostFilters(IEnumerable<SubscriptionListItemDto> items, SubscriptionListQueryRequest request)
    {
        var status = (request.Status ?? string.Empty).Trim();
        if (status.Length > 0 && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

        var paymentStatus = (request.PaymentStatus ?? string.Empty).Trim();
        if (paymentStatus.Length > 0 && !paymentStatus.Equals("All", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.PaymentStatus.Equals(paymentStatus, StringComparison.OrdinalIgnoreCase));

        var tab = (request.Tab ?? "All").Trim();
        var today = DateTime.Today;
        if (tab.Equals("Active", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.Status.Equals(ParkingConstants.SubscriptionStatus.Active, StringComparison.OrdinalIgnoreCase));
        else if (tab.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.BalanceAmount > 0);
        else if (tab.Equals("Expired", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.Status.Equals(ParkingConstants.SubscriptionStatus.Expired, StringComparison.OrdinalIgnoreCase));
        else if (tab.Equals("ExpiringSoon", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.Status.Equals(ParkingConstants.SubscriptionStatus.Active, StringComparison.OrdinalIgnoreCase) && x.EndDate.Date >= today && x.EndDate.Date <= today.AddDays(7));
        else if (tab.Equals("ExtraSlots", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.IsExtraSlot);
        else if (tab.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.Status.Equals(ParkingConstants.SubscriptionStatus.Cancelled, StringComparison.OrdinalIgnoreCase));

        return items;
    }

    private static IEnumerable<SubscriptionListItemDto> SortSubscriptions(IEnumerable<SubscriptionListItemDto> items, string? sortBy, string? sortDirection)
    {
        var desc = (sortDirection ?? string.Empty).Equals("Desc", StringComparison.OrdinalIgnoreCase);
        sortBy = (sortBy ?? string.Empty).Trim().ToLowerInvariant();

        return sortBy switch
        {
            "companyname" => desc ? items.OrderByDescending(x => x.CompanyName) : items.OrderBy(x => x.CompanyName),
            "plantype" => desc ? items.OrderByDescending(x => x.PlanType) : items.OrderBy(x => x.PlanType),
            "slotspurchased" => desc ? items.OrderByDescending(x => x.SlotsPurchased) : items.OrderBy(x => x.SlotsPurchased),
            "totalamount" => desc ? items.OrderByDescending(x => x.TotalAmount) : items.OrderBy(x => x.TotalAmount),
            "balanceamount" => desc ? items.OrderByDescending(x => x.BalanceAmount) : items.OrderBy(x => x.BalanceAmount),
            "startdate" => desc ? items.OrderByDescending(x => x.StartDate) : items.OrderBy(x => x.StartDate),
            "createddate" => desc ? items.OrderByDescending(x => x.CreatedDate) : items.OrderBy(x => x.CreatedDate),
            _ => desc ? items.OrderByDescending(x => x.EndDate) : items.OrderBy(x => x.EndDate),
        };
    }

    private static string GetRuntimeSubscriptionStatus(ParkingSubscription subscription, DateTime today)
    {
        if (subscription.Status.Equals(ParkingConstants.SubscriptionStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            return ParkingConstants.SubscriptionStatus.Cancelled;
        if (subscription.EndDate.Date < today)
            return ParkingConstants.SubscriptionStatus.Expired;
        return subscription.Status;
    }

    private static string NormalizeSubscriptionStatus(string? status, DateTime startDate, DateTime endDate)
    {
        var value = (status ?? string.Empty).Trim();
        if (value.Equals(ParkingConstants.SubscriptionStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            return ParkingConstants.SubscriptionStatus.Cancelled;
        if (endDate.Date < DateTime.Today)
            return ParkingConstants.SubscriptionStatus.Expired;
        return ParkingConstants.SubscriptionStatus.Active;
    }

    public async Task<CompanyGateStatusDto> CheckCompanyAsync(CheckCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var search = request.SearchText.Trim();
        var company = await db.ParkingCompanies
            .FirstOrDefaultAsync(x => x.CompanyCode == search ||
                                      x.CompanyName.Contains(search) ||
                                      (x.Mobile != null && x.Mobile.Contains(search)), cancellationToken);

        if (company == null)
            throw new InvalidOperationException("Company not found.");

        await ReassignInsideSessionsToActiveSubscriptionsAsync(company.CompanyId, request.OperatorId, cancellationToken);

        var status = await BuildCompanyStatusAsync(company, request.OperatorId, cancellationToken);
        await AddActivityAsync(ParkingConstants.GateActionType.EntryCheck, company.CompanyId, null, null, null, status.EntryStatus, status.Message, request.OperatorId, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return status;
    }

    public async Task<GenerateBarcodeResponseDto> GenerateBarcodeAsync(GenerateBarcodeRequest request, CancellationToken cancellationToken = default)
    {
        var company = await repository.GetCompanyAsync(request.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("Company not found.");

        await ReassignInsideSessionsToActiveSubscriptionsAsync(company.CompanyId, request.OperatorId, cancellationToken);

        var status = await BuildCompanyStatusAsync(company, request.OperatorId, cancellationToken);
        if (!status.CanGenerateBarcode && !(status.EntryStatus == "PaymentDue" && request.AllowPaymentDueWarning))
            throw new InvalidOperationException(status.Message);

        await EnsureParkingCapacityAvailableAsync(cancellationToken);

        var plateNo = string.IsNullOrWhiteSpace(request.PlateNo)
            ? null
            : request.PlateNo.Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(plateNo))
        {
            var alreadyInside = await db.ParkingSessions.AnyAsync(x => x.PlateNo == plateNo && x.Status == ParkingConstants.SessionStatus.Inside, cancellationToken);
            if (alreadyInside)
                throw new InvalidOperationException("This vehicle reference is already inside. Please check before creating a new barcode.");
        }

        var subscription = await repository.GetBestActiveSubscriptionAsync(company.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("No active subscription found for this company.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var barcodeNo = await GenerateNextBarcodeNoAsync(cancellationToken);

        var session = new ParkingSession
        {
            BarcodeNo = barcodeNo,
            CompanyId = company.CompanyId,
            SubscriptionId = subscription.SubscriptionId,
            PlateNo = plateNo,
            VehicleType = string.IsNullOrWhiteSpace(request.VehicleType) ? "Car" : request.VehicleType.Trim(),
            DriverName = request.DriverName,
            DriverMobile = request.DriverMobile,
            Remarks = request.Remarks,
            Status = ParkingConstants.SessionStatus.BarcodeGenerated,
            BarcodeStatus = ParkingConstants.BarcodeStatus.Generated,
            CreatedBy = request.OperatorId,
            CreatedDate = DateTime.Now
        };

        await db.ParkingSessions.AddAsync(session, cancellationToken);
        await AddActivityAsync(ParkingConstants.GateActionType.BarcodeGenerated, company.CompanyId, null, barcodeNo, plateNo, "Barcode Generated", "Barcode generated. Print sticker and allow entry.", request.OperatorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new GenerateBarcodeResponseDto(session.SessionId, barcodeNo, company.CompanyName, plateNo, session.VehicleType, session.CreatedDate, session.Status, session.BarcodeStatus);
    }

    public async Task<EntryResultDto> AllowEntryAsync(AllowEntryRequest request, CancellationToken cancellationToken = default)
    {
        var session = await repository.GetSessionAsync(request.SessionId, cancellationToken)
            ?? throw new InvalidOperationException("Parking session not found.");

        if (session.Status != ParkingConstants.SessionStatus.BarcodeGenerated || session.BarcodeStatus != ParkingConstants.BarcodeStatus.Generated)
            throw new InvalidOperationException("Only a generated barcode can be allowed for entry.");

        await ReassignInsideSessionsToActiveSubscriptionsAsync(session.CompanyId, request.OperatorId, cancellationToken);

        var status = await BuildCompanyStatusAsync(session.Company, request.OperatorId, cancellationToken);
        if (!status.CanGenerateBarcode && !(status.EntryStatus == "PaymentDue" && request.AllowPaymentDueWarning))
            throw new InvalidOperationException(status.Message);

        await EnsureParkingCapacityAvailableAsync(cancellationToken);

        var oldStatus = session.Status;
        var oldBarcodeStatus = session.BarcodeStatus;
        var oldEntryTime = session.EntryTime;
        var oldEntryOperatorId = session.EntryOperatorId;

        session.EntryTime = DateTime.Now;
        session.Status = ParkingConstants.SessionStatus.Inside;
        session.BarcodeStatus = ParkingConstants.BarcodeStatus.Active;
        session.EntryOperatorId = request.OperatorId;

        await AddActivityAsync(ParkingConstants.GateActionType.EntryAllowed, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, "Entry Allowed", "Vehicle entry allowed successfully.", request.OperatorId, cancellationToken, new List<SystemActivityChange>
        {
            new("Status", oldStatus, session.Status),
            new("BarcodeStatus", oldBarcodeStatus, session.BarcodeStatus),
            new("EntryTime", AuditValue(oldEntryTime), AuditValue(session.EntryTime)),
            new("EntryOperatorId", AuditValue(oldEntryOperatorId), AuditValue(session.EntryOperatorId))
        });
        await AddOutsideDisplayAsync(session, ParkingConstants.OutsideDisplayStatus.EntryAllowed, "ENTRY ALLOWED", "Please proceed inside.", 0, 0, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new EntryResultDto(session.SessionId, session.BarcodeNo, session.PlateNo, session.Company.CompanyName, session.EntryTime.Value, session.Status, "Entry allowed successfully.");
    }

    public async Task<EntryResultDto> RejectEntryAsync(RejectEntryRequest request, CancellationToken cancellationToken = default)
    {
        var session = await repository.GetSessionAsync(request.SessionId, cancellationToken)
            ?? throw new InvalidOperationException("Parking session not found.");

        if (session.Status == ParkingConstants.SessionStatus.Inside)
            throw new InvalidOperationException("Cannot reject. Vehicle is already inside.");

        var oldStatus = session.Status;
        var oldBarcodeStatus = session.BarcodeStatus;
        var oldRemarks = session.Remarks;

        session.Status = ParkingConstants.SessionStatus.Rejected;
        session.BarcodeStatus = ParkingConstants.BarcodeStatus.Invalid;
        session.Remarks = AppendRemarks(session.Remarks, "Rejected: " + request.Reason);

        await AddActivityAsync(ParkingConstants.GateActionType.EntryRejected, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, "Entry Rejected", request.Reason ?? "Entry rejected.", request.OperatorId, cancellationToken, new List<SystemActivityChange>
        {
            new("Status", oldStatus, session.Status),
            new("BarcodeStatus", oldBarcodeStatus, session.BarcodeStatus),
            new("Remarks", oldRemarks, session.Remarks)
        });
        await repository.SaveChangesAsync(cancellationToken);

        return new EntryResultDto(session.SessionId, session.BarcodeNo, session.PlateNo, session.Company.CompanyName, DateTime.Now, session.Status, "Entry rejected and barcode invalidated.");
    }

    public async Task<ExitScanResultDto> ScanExitBarcodeAsync(ScanExitRequest request, CancellationToken cancellationToken = default)
    {
        var barcode = request.BarcodeNo.Trim().ToUpperInvariant();
        var result = await BuildExitScanResultAsync(barcode, request.OperatorId, true, cancellationToken);
        return result;
    }

    public async Task<ExitResultDto> AllowExitAsync(AllowExitRequest request, CancellationToken cancellationToken = default)
    {
        ParkingSession? session = null;
        if (request.SessionId.HasValue)
            session = await repository.GetSessionAsync(request.SessionId.Value, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(request.BarcodeNo))
            session = await repository.GetSessionByBarcodeAsync(request.BarcodeNo.Trim().ToUpperInvariant(), cancellationToken);

        if (session == null)
            throw new InvalidOperationException("Parking session not found.");

        var scan = await BuildExitScanResultAsync(session.BarcodeNo, request.OperatorId, false, cancellationToken);
        if (!scan.IsValid)
            throw new InvalidOperationException(scan.Message);

        if (scan.TotalPayable > 0 && !request.ForceAllow)
            throw new InvalidOperationException("Payment is required before exit. Use collect payment or force allow with reason.");

        if (scan.TotalPayable > 0 && request.ForceAllow && string.IsNullOrWhiteSpace(request.ForceReason))
            throw new InvalidOperationException("Force exit reason is required.");

        var oldStatus = session.Status;
        var oldBarcodeStatus = session.BarcodeStatus;
        var oldExitTime = session.ExitTime;
        var oldExitOperatorId = session.ExitOperatorId;
        var oldForceExit = session.ForceExit;
        var oldForceExitReason = session.ForceExitReason;
        var oldOverstayDays = session.OverstayDays;
        var oldOverstayAmount = session.OverstayAmount;

        session.ExitTime = DateTime.Now;
        session.ExitOperatorId = request.OperatorId;
        session.Status = ParkingConstants.SessionStatus.Exited;
        session.BarcodeStatus = ParkingConstants.BarcodeStatus.Used;
        session.ForceExit = scan.TotalPayable > 0 && request.ForceAllow;
        session.ForceExitReason = request.ForceReason;
        session.OverstayDays = scan.OverstayDays;
        session.OverstayAmount = scan.OverstayAmount;

        var status = session.ForceExit ? "Exit Allowed With Warning" : "Clear To Exit";
        await AddActivityAsync(ParkingConstants.GateActionType.ExitAllowed, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, status, status, request.OperatorId, cancellationToken, new List<SystemActivityChange>
        {
            new("Status", oldStatus, session.Status),
            new("BarcodeStatus", oldBarcodeStatus, session.BarcodeStatus),
            new("ExitTime", AuditValue(oldExitTime), AuditValue(session.ExitTime)),
            new("ExitOperatorId", AuditValue(oldExitOperatorId), AuditValue(session.ExitOperatorId)),
            new("ForceExit", AuditValue(oldForceExit), AuditValue(session.ForceExit)),
            new("ForceExitReason", oldForceExitReason, session.ForceExitReason),
            new("OverstayDays", AuditValue(oldOverstayDays), AuditValue(session.OverstayDays)),
            new("OverstayAmount", AuditValue(oldOverstayAmount), AuditValue(session.OverstayAmount))
        });
        await AddOutsideDisplayAsync(session, ParkingConstants.OutsideDisplayStatus.ClearToExit, "CLEAR TO EXIT", "Thank you. Please proceed.", 0, 0, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new ExitResultDto(session.SessionId, session.BarcodeNo, session.PlateNo, session.Company.CompanyName, session.ExitTime.Value, session.BarcodeStatus, session.Status, "Exit completed. Barcode is now used and cannot be reused.");
    }

    public async Task<PaymentResultDto> CollectPaymentAsync(CollectPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var amount = ValidatePositivePaymentAmount(request.Amount);
        var paymentMode = NormalizePaymentMode(request.PaymentMode);
        var company = await repository.GetCompanyAsync(request.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("Company not found.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        if (request.SessionId.HasValue)
            await EnsureOverstayInvoiceIfNeededAsync(request.SessionId.Value, request.OperatorId, cancellationToken);

        decimal currentPending;
        string? paidInvoiceNo = null;
        decimal? invoiceBalanceAfterPayment = null;
        decimal? invoiceBalanceBeforePayment = null;
        string? invoiceStatusBeforePayment = null;
        string? invoiceStatusAfterPayment = null;
        var allocations = new List<PaymentAllocation>();

        if (request.InvoiceId.HasValue)
        {
            var invoice = await db.ParkingInvoices
                .FirstOrDefaultAsync(x => x.InvoiceId == request.InvoiceId.Value && x.CompanyId == company.CompanyId, cancellationToken)
                ?? throw new InvalidOperationException("Invoice not found.");

            currentPending = invoice.BalanceAmount;
            invoiceBalanceBeforePayment = invoice.BalanceAmount;
            invoiceStatusBeforePayment = invoice.Status;

            if (invoice.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cancelled invoice cannot receive payment.");

            if (currentPending <= 0)
                throw new InvalidOperationException("No pending amount found for this invoice.");

            if (amount > currentPending)
                throw new InvalidOperationException($"Payment amount cannot be more than current invoice balance AED {currentPending:n2}.");

            ApplyAmountToInvoice(invoice, amount);
            paidInvoiceNo = invoice.InvoiceNo;
            invoiceBalanceAfterPayment = invoice.BalanceAmount;
            invoiceStatusAfterPayment = invoice.Status;
            allocations.Add(new PaymentAllocation(invoice.InvoiceId, invoice.InvoiceNo, amount, invoiceBalanceBeforePayment.Value, invoiceBalanceAfterPayment.Value, invoiceStatusBeforePayment, invoiceStatusAfterPayment));
        }
        else
        {
            currentPending = await repository.GetPendingAmountAsync(company.CompanyId, cancellationToken);

            if (currentPending <= 0)
                throw new InvalidOperationException("No pending amount found for this company.");

            if (amount > currentPending)
                throw new InvalidOperationException($"Payment amount cannot be more than current pending amount AED {currentPending:n2}.");

            allocations.AddRange(await ApplyPaymentToOldestInvoicesAsync(company.CompanyId, amount, cancellationToken));
        }

        if (allocations.Count == 0)
            throw new InvalidOperationException("No payable invoice balance was found for this payment.");

        var payments = new List<ParkingPayment>();
        foreach (var allocation in allocations)
        {
            var receiptNo = await GenerateNextReceiptNoAsync(cancellationToken);
            var payment = new ParkingPayment
            {
                ReceiptNo = receiptNo,
                CompanyId = company.CompanyId,
                InvoiceId = allocation.InvoiceId,
                SessionId = request.SessionId,
                PaymentType = request.SessionId.HasValue ? "GateCollection" : "Invoice",
                Amount = allocation.Amount,
                PaymentMode = paymentMode,
                ReferenceNo = TrimOrNull(request.ReferenceNo),
                ReceivedBy = request.OperatorId,
                PaymentDate = DateTime.Now,
                Remarks = allocations.Count == 1
                    ? TrimOrNull(request.Remarks)
                    : AppendRemarks(request.Remarks, $"Applied AED {allocation.Amount:n2} to invoice {allocation.InvoiceNo}.")
            };

            payments.Add(payment);
            await db.ParkingPayments.AddAsync(payment, cancellationToken);
        }

        var primaryPayment = payments[0];
        var receiptSummary = string.Join(", ", payments.Select(x => x.ReceiptNo));
        var paymentChanges = new List<SystemActivityChange>
        {
            new("ReceiptNo", null, receiptSummary),
            new("Amount", null, AuditValue(amount)),
            new("PaymentMode", null, primaryPayment.PaymentMode),
            new("ReferenceNo", null, primaryPayment.ReferenceNo),
            new("CurrentPendingAmount", AuditValue(currentPending), null),
            new("AppliedInvoices", null, BuildPaymentAllocationSummary(allocations))
        };
        if (request.InvoiceId.HasValue)
        {
            paymentChanges.Add(new("InvoiceId", null, AuditValue(request.InvoiceId)));
            paymentChanges.Add(new("InvoiceBalanceAmount", AuditValue(invoiceBalanceBeforePayment), AuditValue(invoiceBalanceAfterPayment)));
            paymentChanges.Add(new("InvoiceStatus", invoiceStatusBeforePayment, invoiceStatusAfterPayment));
        }

        await AddActivityAsync(ParkingConstants.GateActionType.PaymentCollected, company.CompanyId, request.SessionId, null, null, "Payment Collected", $"Received AED {amount:n2}", request.OperatorId, cancellationToken, paymentChanges, "ParkingPayment", receiptSummary);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var pending = await repository.GetPendingAmountAsync(company.CompanyId, cancellationToken);
        var finalMessage = invoiceBalanceAfterPayment.HasValue
            ? invoiceBalanceAfterPayment.Value <= 0
                ? $"Invoice {paidInvoiceNo} fully collected."
                : $"Partial payment saved for invoice {paidInvoiceNo}. Remaining invoice balance AED {invoiceBalanceAfterPayment.Value:n2}."
            : "Payment saved successfully.";

        if (request.SavePaymentAndAllowExit && request.SessionId.HasValue)
        {
            var session = await repository.GetSessionAsync(request.SessionId.Value, cancellationToken);
            if (session != null)
            {
                var scan = await BuildExitScanResultAsync(session.BarcodeNo, request.OperatorId, false, cancellationToken);
                if (scan.IsValid && scan.TotalPayable <= 0)
                {
                    var oldStatus = session.Status;
                    var oldBarcodeStatus = session.BarcodeStatus;
                    var oldExitTime = session.ExitTime;
                    var oldExitOperatorId = session.ExitOperatorId;
                    var oldForceExit = session.ForceExit;
                    var oldOverstayDays = session.OverstayDays;
                    var oldOverstayAmount = session.OverstayAmount;

                    session.ExitTime = DateTime.Now;
                    session.ExitOperatorId = request.OperatorId;
                    session.Status = ParkingConstants.SessionStatus.Exited;
                    session.BarcodeStatus = ParkingConstants.BarcodeStatus.Used;
                    session.ForceExit = false;
                    session.ForceExitReason = null;
                    session.OverstayDays = scan.OverstayDays;
                    session.OverstayAmount = scan.OverstayAmount;

                    await AddActivityAsync(
                        ParkingConstants.GateActionType.ExitAllowed,
                        session.CompanyId,
                        session.SessionId,
                        session.BarcodeNo,
                        session.PlateNo,
                        "Paid And Exited",
                        "Payment collected and exit completed.",
                        request.OperatorId,
                        cancellationToken,
                        new List<SystemActivityChange>
                        {
                            new("Status", oldStatus, session.Status),
                            new("BarcodeStatus", oldBarcodeStatus, session.BarcodeStatus),
                            new("ExitTime", AuditValue(oldExitTime), AuditValue(session.ExitTime)),
                            new("ExitOperatorId", AuditValue(oldExitOperatorId), AuditValue(session.ExitOperatorId)),
                            new("ForceExit", AuditValue(oldForceExit), AuditValue(session.ForceExit)),
                            new("OverstayDays", AuditValue(oldOverstayDays), AuditValue(session.OverstayDays)),
                            new("OverstayAmount", AuditValue(oldOverstayAmount), AuditValue(session.OverstayAmount))
                        });

                    await AddOutsideDisplayAsync(
                        session,
                        ParkingConstants.OutsideDisplayStatus.ClearToExit,
                        "CLEAR TO EXIT",
                        "Payment cleared. Please proceed.",
                        0,
                        0,
                        cancellationToken);

                    await repository.SaveChangesAsync(cancellationToken);
                    pending = await repository.GetPendingAmountAsync(company.CompanyId, cancellationToken);
                    finalMessage = "Payment saved and exit completed.";
                }
                else
                {
                    finalMessage = $"Payment saved. Remaining payable AED {scan.TotalPayable:n2}.";
                }
            }
        }

        return new PaymentResultDto(
            primaryPayment.PaymentId,
            primaryPayment.ReceiptNo,
            company.CompanyId,
            amount,
            primaryPayment.PaymentMode,
            pending,
            finalMessage);
    }


    public async Task<IReadOnlyList<ExtraSlotRateDto>> GetExtraSlotRatesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        return BuildExtraSlotRatePlans(settings);
    }

    public async Task<ExtraSlotInvoiceResultDto> CreateExtraSlotInvoiceAsync(CreateExtraSlotInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var company = await repository.GetCompanyAsync(request.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("Company not found.");

        var settings = await repository.GetSettingsAsync(cancellationToken);
        var planType = NormalizeExtraSlotPlanType(request.PlanType);
        var ratePerSlot = GetExtraSlotRate(settings, planType);
        if (ratePerSlot <= 0)
            throw new InvalidOperationException($"{planType} extra slot rate is not configured. Please update rate settings first.");

        var startDate = request.StartDate == default ? DateTime.Today : request.StartDate.Date;
        var endDate = CalculateExtraSlotEndDate(planType, startDate);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var subTotal = request.AdditionalSlots * ratePerSlot;
        var total = subTotal - request.DiscountAmount + request.VatAmount;
        if (total < 0)
            throw new InvalidOperationException("Invoice total cannot be negative.");

        var paid = ValidateInitialPaidAmount(request.PaidAmount, total);
        var balance = total - paid;
        var invoiceNo = await GenerateNextInvoiceNoAsync(cancellationToken);

        var subscription = new ParkingSubscription
        {
            CompanyId = company.CompanyId,
            PlanType = planType,
            SlotsPurchased = request.AdditionalSlots,
            RatePerSlot = ratePerSlot,
            StartDate = startDate,
            EndDate = endDate,
            DiscountAmount = request.DiscountAmount,
            VatAmount = request.VatAmount,
            TotalAmount = total,
            PaidAmount = paid,
            BalanceAmount = balance,
            Status = ParkingConstants.SubscriptionStatus.Active,
            IsExtraSlot = true,
            CreatedBy = request.OperatorId,
            CreatedDate = DateTime.Now
        };
        await db.ParkingSubscriptions.AddAsync(subscription, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var invoice = new ParkingInvoice
        {
            InvoiceNo = invoiceNo,
            CompanyId = company.CompanyId,
            SubscriptionId = subscription.SubscriptionId,
            InvoiceType = "ExtraSlot",
            InvoiceDate = DateTime.Now,
            DueDate = endDate,
            PlanType = planType,
            Slots = request.AdditionalSlots,
            SubTotal = subTotal,
            DiscountAmount = request.DiscountAmount,
            VatAmount = request.VatAmount,
            TotalAmount = total,
            PaidAmount = paid,
            BalanceAmount = balance,
            Status = GetInvoiceStatus(balance, paid),
            Remarks = request.Remarks,
            CreatedBy = request.OperatorId,
            CreatedDate = DateTime.Now
        };
        await db.ParkingInvoices.AddAsync(invoice, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        subscription.SourceInvoiceId = invoice.InvoiceId;

        if (paid > 0)
        {
            var receiptNo = await GenerateNextReceiptNoAsync(cancellationToken);
            await db.ParkingPayments.AddAsync(new ParkingPayment
            {
                ReceiptNo = receiptNo,
                CompanyId = company.CompanyId,
                InvoiceId = invoice.InvoiceId,
                PaymentType = "ExtraSlot",
                Amount = paid,
                PaymentMode = NormalizePaymentMode(request.PaymentMode),
                ReferenceNo = TrimOrNull(request.ReferenceNo),
                ReceivedBy = request.OperatorId,
                PaymentDate = DateTime.Now,
                Remarks = "Payment collected with extra slot invoice."
            }, cancellationToken);
        }

        await AddActivityAsync(ParkingConstants.GateActionType.ExtraSlotInvoice, company.CompanyId, null, null, null, "Extra Slot Invoice", $"Added {request.AdditionalSlots} {planType} extra slots.", request.OperatorId, cancellationToken);
        await ReassignInsideSessionsToActiveSubscriptionsAsync(company.CompanyId, request.OperatorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ExtraSlotInvoiceResultDto(subscription.SubscriptionId, invoice.InvoiceId, invoice.InvoiceNo, company.CompanyId, request.AdditionalSlots, planType, total, balance, invoice.Status, "Extra slots added and invoice generated.");
    }


    public async Task<PagedInvoiceResultDto> GetInvoicesAsync(InvoiceListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = Math.Max(request.Page, 1);
        request.PageSize = Math.Clamp(request.PageSize, 10, 100);

        var all = await BuildInvoiceListItemsAsync(request, cancellationToken);
        var sorted = SortInvoices(all, request.SortBy, request.SortDirection).ToList();
        var total = sorted.Count;
        var totalPages = Math.Max((int)Math.Ceiling(total / (double)request.PageSize), 1);
        var page = Math.Min(request.Page, totalPages);
        var items = sorted
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var today = DateTime.Today;
        return new PagedInvoiceResultDto(
            items,
            page,
            request.PageSize,
            total,
            totalPages,
            sorted.Sum(x => x.TotalAmount),
            sorted.Sum(x => x.PaidAmount),
            sorted.Sum(x => x.BalanceAmount),
            sorted.Count(x => x.Status.Equals(ParkingConstants.PaymentStatus.Paid, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.Status.Equals(ParkingConstants.PaymentStatus.Partial, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.Status.Equals(ParkingConstants.PaymentStatus.Unpaid, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.BalanceAmount > 0 && x.DueDate.HasValue && x.DueDate.Value.Date < today && !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<IReadOnlyList<InvoiceListItemDto>> ExportInvoicesAsync(InvoiceListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = 1;
        request.PageSize = 5000;
        var all = await BuildInvoiceListItemsAsync(request, cancellationToken);
        return SortInvoices(all, request.SortBy, request.SortDirection).Take(5000).ToList();
    }

    public async Task<InvoicePaymentReconciliationResultDto> GetInvoicePaymentReconciliationAsync(InvoicePaymentReconciliationQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = Math.Max(request.Page, 1);
        request.PageSize = Math.Clamp(request.PageSize, 10, 100);

        var query = db.ParkingInvoices
            .AsNoTracking()
            .Include(x => x.Company)
            .AsQueryable();

        var search = (request.SearchText ?? string.Empty).Trim();
        if (search.Length > 0)
        {
            query = query.Where(x => x.InvoiceNo.Contains(search) ||
                                     x.Company.CompanyName.Contains(search) ||
                                     x.Company.CompanyCode.Contains(search));
        }

        if (request.CompanyId.HasValue && request.CompanyId.Value > 0)
            query = query.Where(x => x.CompanyId == request.CompanyId.Value);

        var invoiceType = NormalizeInvoiceTypeFilter(request.InvoiceType);
        if (!string.IsNullOrWhiteSpace(invoiceType))
            query = query.Where(x => x.InvoiceType == invoiceType);

        if (!request.IncludeCancelled)
            query = query.Where(x => x.Status != "Cancelled");

        if (request.InvoiceFrom.HasValue)
            query = query.Where(x => x.InvoiceDate.Date >= request.InvoiceFrom.Value.Date);
        if (request.InvoiceTo.HasValue)
            query = query.Where(x => x.InvoiceDate.Date <= request.InvoiceTo.Value.Date);

        var invoices = await query
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.InvoiceId)
            .Take(5000)
            .ToListAsync(cancellationToken);

        var invoiceIds = invoices.Select(x => x.InvoiceId).ToList();
        var paymentSums = invoiceIds.Count == 0
            ? new Dictionary<int, PaymentSummary>()
            : await db.ParkingPayments
                .AsNoTracking()
                .Where(x => x.InvoiceId.HasValue && invoiceIds.Contains(x.InvoiceId.Value))
                .GroupBy(x => x.InvoiceId!.Value)
                .Select(x => new { InvoiceId = x.Key, Amount = x.Sum(y => y.Amount), Count = x.Count() })
                .ToDictionaryAsync(x => x.InvoiceId, x => new PaymentSummary(x.InvoiceId, x.Amount, x.Count), cancellationToken);

        var all = invoices.Select(invoice =>
        {
            paymentSums.TryGetValue(invoice.InvoiceId, out var paymentSummary);
            var linkedPaymentAmount = paymentSummary?.Amount ?? 0m;
            var linkedPaymentCount = paymentSummary?.Count ?? 0;
            var expectedBalance = invoice.TotalAmount - linkedPaymentAmount;
            var paidDifference = invoice.PaidAmount - linkedPaymentAmount;
            var balanceDifference = invoice.BalanceAmount - expectedBalance;
            var storedMathDifference = invoice.TotalAmount - invoice.PaidAmount - invoice.BalanceAmount;
            var expectedStatus = invoice.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                ? "Cancelled"
                : GetInvoiceStatus(expectedBalance, linkedPaymentAmount);

            var issues = new List<string>();
            if (HasMoneyDifference(paidDifference)) issues.Add("Paid amount does not match linked payments");
            if (HasMoneyDifference(balanceDifference)) issues.Add("Balance amount does not match linked payments");
            if (HasMoneyDifference(storedMathDifference)) issues.Add("Stored total/paid/balance math is inconsistent");
            if (invoice.TotalAmount < 0 || invoice.PaidAmount < 0 || invoice.BalanceAmount < 0 || linkedPaymentAmount < 0) issues.Add("Negative amount found");
            if (!invoice.Status.Equals(expectedStatus, StringComparison.OrdinalIgnoreCase)) issues.Add("Invoice status does not match expected payment status");

            return new InvoicePaymentReconciliationItemDto(
                invoice.InvoiceId,
                invoice.InvoiceNo,
                invoice.CompanyId,
                invoice.Company.CompanyCode,
                invoice.Company.CompanyName,
                invoice.InvoiceType,
                invoice.InvoiceDate,
                invoice.TotalAmount,
                invoice.PaidAmount,
                invoice.BalanceAmount,
                linkedPaymentAmount,
                linkedPaymentCount,
                expectedBalance,
                paidDifference,
                balanceDifference,
                storedMathDifference,
                invoice.Status,
                expectedStatus,
                issues.Count > 0,
                issues.Count == 0 ? "OK" : string.Join("; ", issues));
        }).ToList();

        if (request.MismatchOnly)
            all = all.Where(x => x.HasMismatch).ToList();

        var total = all.Count;
        var totalPages = Math.Max((int)Math.Ceiling(total / (double)request.PageSize), 1);
        var page = Math.Min(request.Page, totalPages);
        var items = all.Skip((page - 1) * request.PageSize).Take(request.PageSize).ToList();

        return new InvoicePaymentReconciliationResultDto(
            items,
            page,
            request.PageSize,
            total,
            totalPages,
            all.Count(x => x.HasMismatch),
            all.Sum(x => x.StoredPaidAmount),
            all.Sum(x => x.LinkedPaymentAmount),
            all.Sum(x => x.PaidDifference),
            all.Sum(x => x.StoredBalanceAmount),
            all.Sum(x => x.ExpectedBalanceAmount),
            all.Sum(x => x.BalanceDifference));
    }

    public async Task<InvoiceListItemDto> GetInvoiceByIdAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.ParkingInvoices
            .AsNoTracking()
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

        return ToInvoiceListItem(invoice);
    }

    public async Task<IReadOnlyList<InvoicePaymentDto>> GetInvoicePaymentsAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.ParkingInvoices
            .AsNoTracking()
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

        var payments = await db.ParkingPayments
            .AsNoTracking()
            .Where(x => x.InvoiceId == invoiceId)
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.PaymentId)
            .ToListAsync(cancellationToken);

        return payments.Select(x => new InvoicePaymentDto(
            x.PaymentId,
            x.ReceiptNo,
            x.CompanyId,
            x.InvoiceId,
            invoice.Company.CompanyName,
            x.Amount,
            x.PaymentMode,
            x.ReferenceNo,
            x.PaymentDate,
            x.Remarks,
            x.ReceivedBy)).ToList();
    }

    public async Task<InvoiceListItemDto> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var company = await db.ParkingCompanies.FirstOrDefaultAsync(x => x.CompanyId == request.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("Company not found.");

        var invoiceType = NormalizeInvoiceType(request.InvoiceType);
        var invoiceDate = request.InvoiceDate == default ? DateTime.Now : request.InvoiceDate;
        var subTotal = Math.Max(request.SubTotal, 0);
        var discount = Math.Max(request.DiscountAmount, 0);
        var vat = Math.Max(request.VatAmount, 0);
        var total = request.TotalAmount > 0 ? request.TotalAmount : subTotal - discount + vat;
        if (total < 0)
            throw new InvalidOperationException("Invoice total cannot be negative.");

        var paid = ValidateInitialPaidAmount(request.PaidAmount, total);

        var invoiceNo = TrimOrNull(request.InvoiceNo) ?? await GenerateNextInvoiceNoAsync(cancellationToken);
        var duplicate = await db.ParkingInvoices.AnyAsync(x => x.InvoiceNo == invoiceNo, cancellationToken);
        if (duplicate)
            throw new InvalidOperationException("Invoice number already exists.");

        var balance = total - paid;
        var invoice = new ParkingInvoice
        {
            InvoiceNo = invoiceNo,
            CompanyId = company.CompanyId,
            InvoiceType = invoiceType,
            InvoiceDate = invoiceDate,
            DueDate = request.DueDate,
            PlanType = TrimOrNull(request.PlanType),
            Slots = Math.Max(request.Slots, 0),
            SubTotal = subTotal,
            DiscountAmount = discount,
            VatAmount = vat,
            TotalAmount = total,
            PaidAmount = paid,
            BalanceAmount = balance,
            Status = GetInvoiceStatus(balance, paid),
            Remarks = TrimOrNull(request.Remarks),
            CreatedDate = DateTime.Now,
            CreatedBy = request.OperatorId
        };

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await db.ParkingInvoices.AddAsync(invoice, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (paid > 0)
        {
            var receiptNo = await GenerateNextReceiptNoAsync(cancellationToken);
            await db.ParkingPayments.AddAsync(new ParkingPayment
            {
                ReceiptNo = receiptNo,
                CompanyId = company.CompanyId,
                InvoiceId = invoice.InvoiceId,
                PaymentType = "Invoice",
                Amount = paid,
                PaymentMode = string.IsNullOrWhiteSpace(request.PaymentMode) ? "Cash" : request.PaymentMode.Trim(),
                ReferenceNo = TrimOrNull(request.ReferenceNo),
                ReceivedBy = request.OperatorId,
                PaymentDate = DateTime.Now,
                Remarks = "Initial payment collected while creating invoice."
            }, cancellationToken);
        }

        await AddActivityAsync("InvoiceCreated", company.CompanyId, null, null, null, "Invoice Created", $"Invoice {invoice.InvoiceNo} created for AED {invoice.TotalAmount:n2}.", request.OperatorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetInvoiceByIdAsync(invoice.InvoiceId, cancellationToken);
    }

    public async Task<InvoiceListItemDto> UpdateInvoiceAsync(int invoiceId, UpdateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var invoice = await db.ParkingInvoices.Include(x => x.Company).FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

        if (invoice.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cancelled invoice cannot be edited.");

        var subTotal = Math.Max(request.SubTotal, 0);
        var discount = Math.Max(request.DiscountAmount, 0);
        var vat = Math.Max(request.VatAmount, 0);
        var total = request.TotalAmount > 0 ? request.TotalAmount : subTotal - discount + vat;
        if (total < 0)
            throw new InvalidOperationException("Invoice total cannot be negative.");
        if (total < invoice.PaidAmount)
            throw new InvalidOperationException($"Invoice total cannot be less than already paid amount AED {invoice.PaidAmount:n2}.");

        var newInvoiceType = NormalizeInvoiceType(request.InvoiceType);
        var newInvoiceDate = request.InvoiceDate == default ? invoice.InvoiceDate : request.InvoiceDate;
        var newPlanType = TrimOrNull(request.PlanType);
        var newSlots = Math.Max(request.Slots, 0);
        var newBalance = total - invoice.PaidAmount;
        var newStatus = GetInvoiceStatus(newBalance, invoice.PaidAmount);
        var newRemarks = TrimOrNull(request.Remarks);
        var changes = new List<SystemActivityChange>
        {
            new("InvoiceType", invoice.InvoiceType, newInvoiceType),
            new("InvoiceDate", AuditValue(invoice.InvoiceDate), AuditValue(newInvoiceDate)),
            new("DueDate", AuditValue(invoice.DueDate), AuditValue(request.DueDate)),
            new("PlanType", invoice.PlanType, newPlanType),
            new("Slots", AuditValue(invoice.Slots), AuditValue(newSlots)),
            new("SubTotal", AuditValue(invoice.SubTotal), AuditValue(subTotal)),
            new("DiscountAmount", AuditValue(invoice.DiscountAmount), AuditValue(discount)),
            new("VatAmount", AuditValue(invoice.VatAmount), AuditValue(vat)),
            new("TotalAmount", AuditValue(invoice.TotalAmount), AuditValue(total)),
            new("BalanceAmount", AuditValue(invoice.BalanceAmount), AuditValue(newBalance)),
            new("Status", invoice.Status, newStatus),
            new("Remarks", invoice.Remarks, newRemarks)
        };

        invoice.InvoiceType = newInvoiceType;
        invoice.InvoiceDate = newInvoiceDate;
        invoice.DueDate = request.DueDate;
        invoice.PlanType = newPlanType;
        invoice.Slots = newSlots;
        invoice.SubTotal = subTotal;
        invoice.DiscountAmount = discount;
        invoice.VatAmount = vat;
        invoice.TotalAmount = total;
        invoice.BalanceAmount = newBalance;
        invoice.Status = newStatus;
        invoice.Remarks = newRemarks;
        invoice.ModifiedDate = DateTime.Now;
        invoice.ModifiedBy = request.OperatorId;

        await AddActivityAsync("InvoiceUpdated", invoice.CompanyId, invoice.SessionId, null, null, "Invoice Updated", $"Invoice {invoice.InvoiceNo} updated.", request.OperatorId, cancellationToken, changes, "ParkingInvoice", invoice.InvoiceId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return ToInvoiceListItem(invoice);
    }

    public async Task<InvoiceListItemDto> CancelInvoiceAsync(int invoiceId, CancelInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var invoice = await db.ParkingInvoices.Include(x => x.Company).FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

        if (invoice.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            return ToInvoiceListItem(invoice);

        if (invoice.PaidAmount > 0 && !request.ClearPendingBalance)
            throw new InvalidOperationException("Invoice has payment history. Enable clear pending balance to cancel it without deleting old payments.");

        var oldStatus = invoice.Status;
        var oldBalanceAmount = invoice.BalanceAmount;
        var oldCancellationReason = invoice.CancellationReason;
        var oldRemarks = invoice.Remarks;

        invoice.Status = "Cancelled";
        if (request.ClearPendingBalance)
            invoice.BalanceAmount = 0;
        invoice.CancelledDate = DateTime.Now;
        invoice.CancelledBy = request.OperatorId;
        invoice.CancellationReason = TrimOrNull(request.Reason) ?? "Cancelled from invoice module.";
        invoice.ModifiedDate = DateTime.Now;
        invoice.ModifiedBy = request.OperatorId;
        invoice.Remarks = AppendRemarks(invoice.Remarks, "Cancelled: " + invoice.CancellationReason);

        await AddActivityAsync("InvoiceCancelled", invoice.CompanyId, invoice.SessionId, null, null, "Invoice Cancelled", $"Invoice {invoice.InvoiceNo} cancelled. {invoice.CancellationReason}", request.OperatorId, cancellationToken, new List<SystemActivityChange>
        {
            new("Status", oldStatus, invoice.Status),
            new("BalanceAmount", AuditValue(oldBalanceAmount), AuditValue(invoice.BalanceAmount)),
            new("CancellationReason", oldCancellationReason, invoice.CancellationReason),
            new("Remarks", oldRemarks, invoice.Remarks)
        }, "ParkingInvoice", invoice.InvoiceId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return ToInvoiceListItem(invoice);
    }

    public async Task<IReadOnlyList<CompanyInvoiceDto>> GetCompanyInvoicesAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var exists = await db.ParkingCompanies.AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        if (!exists)
            throw new InvalidOperationException("Company not found.");

        var invoices = await db.ParkingInvoices
            .AsNoTracking()
            .Include(x => x.Company)
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.InvoiceId)
            .Take(500)
            .ToListAsync(cancellationToken);

        return invoices.Select(x => new CompanyInvoiceDto(
            x.InvoiceId,
            x.InvoiceNo,
            x.CompanyId,
            x.Company.CompanyName,
            x.InvoiceType,
            x.InvoiceDate,
            x.DueDate,
            x.PlanType,
            x.Slots,
            x.SubTotal,
            x.DiscountAmount,
            x.VatAmount,
            x.TotalAmount,
            x.PaidAmount,
            x.BalanceAmount,
            x.Status,
            x.Remarks)).ToList();
    }

    public async Task<IReadOnlyList<RecentActivityDto>> GetRecentActivityAsync(int take = 25, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var logs = await db.GateActivityLogs
            .AsNoTracking()
            .OrderByDescending(x => x.ActionDate)
            .Take(take)
            .ToListAsync(cancellationToken);

        var companyIds = logs.Where(x => x.CompanyId.HasValue).Select(x => x.CompanyId!.Value).Distinct().ToList();
        var companies = await db.ParkingCompanies
            .Where(x => companyIds.Contains(x.CompanyId))
            .ToDictionaryAsync(x => x.CompanyId, x => x.CompanyName, cancellationToken);

        return logs.Select(x => new RecentActivityDto(
            x.ActionDate,
            x.ActionType.Contains("Exit") ? "Exit" : "Entry",
            x.PlateNo,
            x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : null,
            x.BarcodeNo,
            x.Status,
            x.Message,
            x.OperatorId)).ToList();
    }

    public async Task<IReadOnlyList<LiveParkingDto>> GetLiveParkingAsync(string? searchText = null, CancellationToken cancellationToken = default)
    {
        await ReassignInsideSessionsToActiveSubscriptionsAsync(null, 0, cancellationToken);

        searchText = searchText?.Trim();
        var query = db.ParkingSessions
            .Include(x => x.Company)
            .Include(x => x.Subscription)
            .AsNoTracking()
            .Where(x => x.Status == ParkingConstants.SessionStatus.Inside);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(x => x.BarcodeNo.Contains(searchText) || (x.PlateNo != null && x.PlateNo.Contains(searchText)) || x.Company.CompanyName.Contains(searchText));
        }

        var sessions = await query.OrderByDescending(x => x.EntryTime).Take(500).ToListAsync(cancellationToken);
        return sessions.Select(s => new LiveParkingDto(
            s.SessionId,
            s.BarcodeNo,
            s.PlateNo,
            s.Company.CompanyName,
            s.EntryTime ?? s.CreatedDate,
            FormatDuration(DateTime.Now - (s.EntryTime ?? s.CreatedDate)),
            s.Subscription?.EndDate,
            s.Status)).ToList();
    }



    public async Task<PagedLiveParkingResultDto> GetLiveParkingPagedAsync(LiveParkingListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = Math.Max(request.Page, 1);
        request.PageSize = Math.Clamp(request.PageSize, 10, 100);

        var all = await BuildLiveParkingListItemsAsync(request, cancellationToken);
        var sorted = SortLiveParking(all, request.SortBy, request.SortDirection).ToList();
        var total = sorted.Count;
        var totalPages = Math.Max((int)Math.Ceiling(total / (double)request.PageSize), 1);
        var page = Math.Min(request.Page, totalPages);
        var items = sorted.Skip((page - 1) * request.PageSize).Take(request.PageSize).ToList();

        return new PagedLiveParkingResultDto(
            items,
            page,
            request.PageSize,
            total,
            totalPages,
            sorted.Count(x => x.Status.Equals(ParkingConstants.SessionStatus.Inside, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.OverstayDays > 0),
            CountLiveParkingCompaniesWithPending(sorted),
            sorted.Count(x => x.Status.Equals(ParkingConstants.SessionStatus.BarcodeGenerated, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.Status.Equals(ParkingConstants.SessionStatus.Exited, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<IReadOnlyList<LiveParkingListItemDto>> ExportLiveParkingAsync(LiveParkingListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = 1;
        request.PageSize = 5000;
        var all = await BuildLiveParkingListItemsAsync(request, cancellationToken);
        return SortLiveParking(all, request.SortBy, request.SortDirection).Take(5000).ToList();
    }

    public async Task<PagedPaymentResultDto> GetPaymentsAsync(PaymentListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = Math.Max(request.Page, 1);
        request.PageSize = Math.Clamp(request.PageSize, 10, 100);

        var all = await BuildPaymentListItemsAsync(request, cancellationToken);
        var sorted = SortPayments(all, request.SortBy, request.SortDirection).ToList();
        var total = sorted.Count;
        var totalPages = Math.Max((int)Math.Ceiling(total / (double)request.PageSize), 1);
        var page = Math.Min(request.Page, totalPages);
        var items = sorted.Skip((page - 1) * request.PageSize).Take(request.PageSize).ToList();

        var paymentModeGroups = sorted
            .GroupBy(x => NormalizePaymentModeKey(x.PaymentMode))
            .ToDictionary(x => x.Key, x => new { Count = x.Count(), Amount = x.Sum(y => y.Amount) });

        var cash = paymentModeGroups.GetValueOrDefault("cash");
        var card = paymentModeGroups.GetValueOrDefault("card");
        var bank = paymentModeGroups.GetValueOrDefault("banktransfer");
        var cheque = paymentModeGroups.GetValueOrDefault("cheque");
        var otherModes = paymentModeGroups.Where(x => !PaymentModeKpiKeys.Contains(x.Key)).ToList();

        return new PagedPaymentResultDto(
            items,
            page,
            request.PageSize,
            total,
            totalPages,
            sorted.Sum(x => x.Amount),
            cash?.Count ?? 0,
            card?.Count ?? 0,
            bank?.Count ?? 0,
            cheque?.Count ?? 0,
            otherModes.Sum(x => x.Value.Count),
            cash?.Amount ?? 0m,
            card?.Amount ?? 0m,
            bank?.Amount ?? 0m,
            cheque?.Amount ?? 0m,
            otherModes.Sum(x => x.Value.Amount));
    }

    public async Task<IReadOnlyList<PaymentListItemDto>> ExportPaymentsAsync(PaymentListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = 1;
        request.PageSize = 5000;
        var all = await BuildPaymentListItemsAsync(request, cancellationToken);
        return SortPayments(all, request.SortBy, request.SortDirection).Take(5000).ToList();
    }

    public async Task<PaymentListItemDto> GetPaymentByIdAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        var rows = await BuildPaymentListItemsAsync(new PaymentListQueryRequest { PaymentId = paymentId, Page = 1, PageSize = 1 }, cancellationToken);
        return rows.FirstOrDefault() ?? throw new InvalidOperationException("Payment not found.");
    }

    public async Task<PagedVehicleBarcodeResultDto> GetVehicleBarcodesAsync(VehicleBarcodeListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = Math.Max(request.Page, 1);
        request.PageSize = Math.Clamp(request.PageSize, 10, 100);

        var all = await BuildVehicleBarcodeListItemsAsync(request, cancellationToken);
        var sorted = SortVehicleBarcodes(all, request.SortBy, request.SortDirection).ToList();
        var total = sorted.Count;
        var totalPages = Math.Max((int)Math.Ceiling(total / (double)request.PageSize), 1);
        var page = Math.Min(request.Page, totalPages);
        var items = sorted.Skip((page - 1) * request.PageSize).Take(request.PageSize).ToList();

        return new PagedVehicleBarcodeResultDto(
            items,
            page,
            request.PageSize,
            total,
            totalPages,
            sorted.Count(x => x.Status.Equals(ParkingConstants.SessionStatus.BarcodeGenerated, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.Status.Equals(ParkingConstants.SessionStatus.Inside, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.Status.Equals(ParkingConstants.SessionStatus.Exited, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.BarcodeStatus.Equals(ParkingConstants.BarcodeStatus.Invalid, StringComparison.OrdinalIgnoreCase)),
            sorted.Count(x => x.OverstayDays > 0));
    }

    public async Task<IReadOnlyList<VehicleBarcodeListItemDto>> ExportVehicleBarcodesAsync(VehicleBarcodeListQueryRequest request, CancellationToken cancellationToken = default)
    {
        request.Page = 1;
        request.PageSize = 5000;
        var all = await BuildVehicleBarcodeListItemsAsync(request, cancellationToken);
        return SortVehicleBarcodes(all, request.SortBy, request.SortDirection).Take(5000).ToList();
    }

    public async Task<VehicleBarcodeDetailDto> GetVehicleBarcodeDetailAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        var companyId = await db.ParkingSessions
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .Select(x => (int?)x.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Vehicle/barcode record not found.");

        await ReassignInsideSessionsToActiveSubscriptionsAsync(companyId, 0, cancellationToken);

        var session = await db.ParkingSessions
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Subscription)
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Vehicle/barcode record not found.");

        var pending = await GetSubscriptionPendingAmountAsync(session.CompanyId, cancellationToken);
        var item = ToVehicleBarcodeListItem(session, pending);

        var invoices = await db.ParkingInvoices
            .AsNoTracking()
            .Include(x => x.Company)
            .Where(x => x.SessionId == session.SessionId)
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);
        var invoiceItems = invoices.Select(ToInvoiceListItem).ToList();
        var invoiceIds = invoices.Select(x => x.InvoiceId).ToList();

        var payments = await db.ParkingPayments
            .AsNoTracking()
            .Where(x => x.SessionId == session.SessionId || (x.InvoiceId.HasValue && invoiceIds.Contains(x.InvoiceId.Value)))
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.PaymentId)
            .ToListAsync(cancellationToken);

        var paymentItems = payments.Select(x => new InvoicePaymentDto(
            x.PaymentId,
            x.ReceiptNo,
            x.CompanyId,
            x.InvoiceId,
            session.Company.CompanyName,
            x.Amount,
            x.PaymentMode,
            x.ReferenceNo,
            x.PaymentDate,
            x.Remarks,
            x.ReceivedBy)).ToList();

        var history = await db.GateActivityLogs
            .AsNoTracking()
            .Where(x => x.SessionId == session.SessionId || x.BarcodeNo == session.BarcodeNo)
            .OrderByDescending(x => x.ActionDate)
            .Take(100)
            .Select(x => new VehicleBarcodeHistoryDto(x.ActionDate, x.ActionType, x.Status, x.Message, x.OperatorId))
            .ToListAsync(cancellationToken);

        return new VehicleBarcodeDetailDto(item, invoiceItems, paymentItems, history);
    }

    public async Task<VehicleBarcodeDetailDto> MarkVehicleBarcodeInvalidAsync(int sessionId, MarkBarcodeInvalidRequest request, CancellationToken cancellationToken = default)
    {
        var session = await repository.GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Vehicle/barcode record not found.");

        if (session.Status == ParkingConstants.SessionStatus.Inside || session.BarcodeStatus == ParkingConstants.BarcodeStatus.Active)
            throw new InvalidOperationException("This barcode is currently inside. Use exit or force exit before invalidating it.");

        if (session.Status == ParkingConstants.SessionStatus.Exited || session.BarcodeStatus == ParkingConstants.BarcodeStatus.Used)
            throw new InvalidOperationException("Used/exited barcode cannot be invalidated.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("Invalidation reason is required.");

        var oldStatus = session.Status;
        var oldBarcodeStatus = session.BarcodeStatus;
        var oldRemarks = session.Remarks;

        session.Status = ParkingConstants.SessionStatus.Cancelled;
        session.BarcodeStatus = ParkingConstants.BarcodeStatus.Invalid;
        session.Remarks = AppendRemarks(session.Remarks, "Invalidated: " + request.Reason.Trim());

        await AddActivityAsync("BarcodeInvalidated", session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, "Barcode Invalidated", request.Reason.Trim(), request.OperatorId, cancellationToken, new List<SystemActivityChange>
        {
            new("Status", oldStatus, session.Status),
            new("BarcodeStatus", oldBarcodeStatus, session.BarcodeStatus),
            new("Remarks", oldRemarks, session.Remarks)
        });
        await repository.SaveChangesAsync(cancellationToken);

        return await GetVehicleBarcodeDetailAsync(sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<OutsideDisplayDto>> GetRecentOutsideDisplayScansAsync(int take = 30, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);

        var items = await db.OutsideDisplayEvents
            .AsNoTracking()
            .Where(x => x.BarcodeNo != null && x.BarcodeNo != "")
            .OrderByDescending(x => x.DisplayEventId)
            .Take(take)
            .ToListAsync(cancellationToken);

        return items.Select(item => new OutsideDisplayDto(
            item.DisplayEventId,
            item.BarcodeNo,
            item.PlateNo,
            item.CompanyName,
            item.DisplayStatus,
            item.MainMessage,
            item.SubMessage,
            item.AmountDue,
            item.OverstayDays,
            item.CreatedDate
        )).ToList();
    }

    public async Task<OutsideDisplayDto?> GetLatestOutsideDisplayAsync(CancellationToken cancellationToken = default)
    {
        var item = await db.OutsideDisplayEvents
            .AsNoTracking()
            .OrderByDescending(x => x.DisplayEventId)
            .FirstOrDefaultAsync(cancellationToken);

        return item == null ? null : new OutsideDisplayDto(item.DisplayEventId, item.BarcodeNo, item.PlateNo, item.CompanyName, item.DisplayStatus, item.MainMessage, item.SubMessage, item.AmountDue, item.OverstayDays, item.CreatedDate);
    }

    private async Task<IReadOnlyList<CompanyListItemDto>> BuildCompanyListItemsAsync(CompanyListQueryRequest request, CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var search = (request.SearchText ?? string.Empty).Trim();
        var query = db.ParkingCompanies.AsNoTracking().AsQueryable();

        if (search.Length > 0)
        {
            query = query.Where(x =>
                x.CompanyCode.Contains(search) ||
                x.CompanyName.Contains(search) ||
                (x.ContactPerson != null && x.ContactPerson.Contains(search)) ||
                (x.Mobile != null && x.Mobile.Contains(search)) ||
                (x.Email != null && x.Email.Contains(search)) ||
                (x.TradeLicenseNo != null && x.TradeLicenseNo.Contains(search)) ||
                (x.Trn != null && x.Trn.Contains(search)));
        }

        var status = (request.Status ?? string.Empty).Trim();
        if (status.Length > 0 && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == status);

        if (request.CreatedFrom.HasValue)
            query = query.Where(x => x.CreatedDate >= request.CreatedFrom.Value.Date);
        if (request.CreatedTo.HasValue)
            query = query.Where(x => x.CreatedDate < request.CreatedTo.Value.Date.AddDays(1));

        var companies = await query
            .OrderBy(x => x.CompanyName)
            .Take(5000)
            .ToListAsync(cancellationToken);

        if (companies.Count == 0)
            return [];

        var ids = companies.Select(x => x.CompanyId).ToList();

        var subscriptions = await db.ParkingSubscriptions
            .AsNoTracking()
            .Where(x => ids.Contains(x.CompanyId))
            .ToListAsync(cancellationToken);

        var activeSubscriptions = subscriptions
            .Where(x => x.Status == ParkingConstants.SubscriptionStatus.Active && x.StartDate.Date <= today && x.EndDate.Date >= today)
            .ToList();

        var slotsByCompany = activeSubscriptions
            .GroupBy(x => x.CompanyId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.SlotsPurchased));

        var bestSubByCompany = subscriptions
            .GroupBy(x => x.CompanyId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.EndDate).ThenByDescending(y => y.SubscriptionId).First());

        var activeSubByCompany = activeSubscriptions
            .GroupBy(x => x.CompanyId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.EndDate).ThenByDescending(y => y.SubscriptionId).First());

        var insideByCompany = await db.ParkingSessions
            .AsNoTracking()
            .Where(x => ids.Contains(x.CompanyId) && x.Status == ParkingConstants.SessionStatus.Inside)
            .GroupBy(x => x.CompanyId)
            .Select(x => new { CompanyId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count, cancellationToken);

        var invoiceStats = await db.ParkingInvoices
            .AsNoTracking()
            .Where(x => ids.Contains(x.CompanyId))
            .GroupBy(x => x.CompanyId)
            .Select(x => new
            {
                CompanyId = x.Key,
                PendingAmount = x.Sum(y => y.BalanceAmount > 0 ? y.BalanceAmount : 0),
                HasOverdue = x.Any(y => y.BalanceAmount > 0 && y.DueDate.HasValue && y.DueDate.Value < today),
                HasPartial = x.Any(y => y.BalanceAmount > 0 && y.PaidAmount > 0),
                LastInvoiceDate = x.Max(y => (DateTime?)y.InvoiceDate)
            })
            .ToDictionaryAsync(x => x.CompanyId, cancellationToken);

        var items = new List<CompanyListItemDto>();
        foreach (var company in companies)
        {
            activeSubByCompany.TryGetValue(company.CompanyId, out var activeSub);
            bestSubByCompany.TryGetValue(company.CompanyId, out var bestSub);
            invoiceStats.TryGetValue(company.CompanyId, out var stat);

            var selectedSub = activeSub ?? bestSub;
            var slots = slotsByCompany.TryGetValue(company.CompanyId, out var slotCount) ? slotCount : 0;
            var inside = insideByCompany.TryGetValue(company.CompanyId, out var insideCount) ? insideCount : 0;
            var pending = stat?.PendingAmount ?? 0m;
            var paymentStatus = pending <= 0
                ? ParkingConstants.PaymentStatus.Paid
                : stat?.HasOverdue == true
                    ? ParkingConstants.PaymentStatus.Overdue
                    : stat?.HasPartial == true
                        ? ParkingConstants.PaymentStatus.Partial
                        : ParkingConstants.PaymentStatus.Unpaid;

            var subscriptionStatus = activeSub != null
                ? ParkingConstants.SubscriptionStatus.Active
                : bestSub == null
                    ? "NoSubscription"
                    : bestSub.EndDate.Date < today
                        ? ParkingConstants.SubscriptionStatus.Expired
                        : bestSub.Status;

            items.Add(new CompanyListItemDto(
                company.CompanyId,
                company.CompanyCode,
                company.CompanyName,
                company.ContactPerson,
                company.Mobile,
                company.Email,
                company.Address,
                company.TradeLicenseNo,
                company.Trn,
                company.Status,
                company.OpeningBalance,
                company.BillingName,
                company.PaymentTerms,
                company.CreditLimit,
                company.Remarks,
                company.InternalNotes,
                slots,
                inside,
                Math.Max(slots - inside, 0),
                pending,
                paymentStatus,
                subscriptionStatus,
                selectedSub?.PlanType,
                selectedSub?.StartDate,
                selectedSub?.EndDate,
                stat?.LastInvoiceDate,
                company.CreatedDate));
        }

        return ApplyCompanyListPostFilters(items, request).ToList();
    }

    private static IEnumerable<CompanyListItemDto> ApplyCompanyListPostFilters(IEnumerable<CompanyListItemDto> items, CompanyListQueryRequest request)
    {
        var paymentStatus = (request.PaymentStatus ?? string.Empty).Trim();
        if (paymentStatus.Length > 0 && !paymentStatus.Equals("All", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.PaymentStatus.Equals(paymentStatus, StringComparison.OrdinalIgnoreCase));

        var subscriptionType = (request.SubscriptionType ?? string.Empty).Trim();
        if (subscriptionType.Length > 0 && !subscriptionType.Equals("All", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => (x.SubscriptionType ?? string.Empty).Equals(subscriptionType, StringComparison.OrdinalIgnoreCase));

        var tab = (request.Tab ?? "All").Trim();
        if (tab.Equals("Active", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.Status.Equals(ParkingConstants.CompanyStatus.Active, StringComparison.OrdinalIgnoreCase));
        else if (tab.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => !x.Status.Equals(ParkingConstants.CompanyStatus.Active, StringComparison.OrdinalIgnoreCase));
        else if (tab.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.PendingAmount > 0);
        else if (tab.Equals("Overdue", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.PaymentStatus.Equals(ParkingConstants.PaymentStatus.Overdue, StringComparison.OrdinalIgnoreCase));
        else if (tab.Equals("Expired", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.SubscriptionStatus.Equals(ParkingConstants.SubscriptionStatus.Expired, StringComparison.OrdinalIgnoreCase) || x.SubscriptionStatus.Equals("NoSubscription", StringComparison.OrdinalIgnoreCase));
        else if (tab.Equals("Inside", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.VehiclesInside > 0);

        return items;
    }

    private static IEnumerable<CompanyListItemDto> SortCompanies(IEnumerable<CompanyListItemDto> items, string? sortBy, string? sortDirection)
    {
        var desc = (sortDirection ?? string.Empty).Equals("Desc", StringComparison.OrdinalIgnoreCase);
        sortBy = (sortBy ?? string.Empty).Trim();

        return sortBy.ToLowerInvariant() switch
        {
            "companycode" => desc ? items.OrderByDescending(x => x.CompanyCode) : items.OrderBy(x => x.CompanyCode),
            "pendingamount" => desc ? items.OrderByDescending(x => x.PendingAmount) : items.OrderBy(x => x.PendingAmount),
            "vehiclesinside" => desc ? items.OrderByDescending(x => x.VehiclesInside) : items.OrderBy(x => x.VehiclesInside),
            "availableslots" => desc ? items.OrderByDescending(x => x.AvailableSlots) : items.OrderBy(x => x.AvailableSlots),
            "createddate" => desc ? items.OrderByDescending(x => x.CreatedDate) : items.OrderBy(x => x.CreatedDate),
            "subscriptionenddate" => desc ? items.OrderByDescending(x => x.SubscriptionEndDate) : items.OrderBy(x => x.SubscriptionEndDate),
            _ => desc ? items.OrderByDescending(x => x.CompanyName) : items.OrderBy(x => x.CompanyName),
        };
    }

    private async Task<CompanyGateStatusDto> BuildCompanyStatusAsync(ParkingCompany company, int operatorId, CancellationToken cancellationToken)
    {
        var slots = await repository.GetActiveSlotCountAsync(company.CompanyId, cancellationToken);
        var inside = await repository.GetInsideVehicleCountAsync(company.CompanyId, cancellationToken);
        var pending = await repository.GetPendingAmountAsync(company.CompanyId, cancellationToken);
        var subscription = await repository.GetBestActiveSubscriptionAsync(company.CompanyId, cancellationToken);
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var blockPaymentDue = GetBoolSetting(settings, "BlockEntryIfPaymentDue", false);
        var blockWhenFull = GetBoolSetting(settings, "BlockEntryWhenParkingFull", true);
        var totalCapacity = GetIntSetting(settings, "TotalParkingCapacity", 500);
        var totalInside = blockWhenFull
            ? await db.ParkingSessions.CountAsync(x => x.Status == ParkingConstants.SessionStatus.Inside, cancellationToken)
            : 0;

        var available = Math.Max(slots - inside, 0);
        var paymentStatus = pending <= 0 ? ParkingConstants.PaymentStatus.Paid : ParkingConstants.PaymentStatus.Unpaid;
        var subscriptionStatus = subscription == null ? "NoActiveSubscription" : ParkingConstants.SubscriptionStatus.Active;
        string entryStatus;
        bool canGenerate;
        string message;

        if (company.Status != ParkingConstants.CompanyStatus.Active)
        {
            entryStatus = "CompanyBlocked";
            canGenerate = false;
            message = "Company is not active. Entry cannot be allowed.";
        }
        else if (subscription == null || slots <= 0)
        {
            entryStatus = "SubscriptionExpired";
            canGenerate = false;
            message = "No active subscription found. Create or renew subscription.";
        }
        else if (blockWhenFull && totalInside >= totalCapacity)
        {
            entryStatus = "ParkingFull";
            canGenerate = false;
            message = $"Parking capacity is full ({totalInside}/{totalCapacity}). Allow exit or increase parking capacity in settings.";
        }
        else if (available <= 0)
        {
            entryStatus = "SlotLimitReached";
            canGenerate = false;
            message = "Slot limit reached. Create extra slot invoice before allowing entry.";
        }
        else if (pending > 0 && blockPaymentDue)
        {
            entryStatus = "PaymentDue";
            canGenerate = false;
            message = $"Payment due AED {pending:n2}. Collect payment before allowing entry.";
        }
        else if (pending > 0)
        {
            entryStatus = "PaymentDue";
            canGenerate = true;
            message = $"Payment due AED {pending:n2}. Entry can continue only with warning approval.";
        }
        else
        {
            entryStatus = "EntryAllowed";
            canGenerate = true;
            message = "Company is active, payment is clear, and slots are available.";
        }

        return new CompanyGateStatusDto(
            company.CompanyId,
            company.CompanyCode,
            company.CompanyName,
            company.ContactPerson,
            company.Mobile,
            company.Status,
            slots,
            inside,
            available,
            pending,
            paymentStatus,
            subscriptionStatus,
            subscription?.StartDate,
            subscription?.EndDate,
            entryStatus,
            canGenerate,
            message);
    }

    private sealed record SubscriptionSlotSnapshot(
        int SubscriptionId,
        int CompanyId,
        int SlotsPurchased,
        DateTime StartDate,
        DateTime EndDate,
        string Status);

    private async Task EnsureRegularSubscriptionDoesNotOverlapAsync(
        int companyId,
        int? currentSubscriptionId,
        bool isExtraSlot,
        DateTime startDate,
        DateTime endDate,
        string proposedStatus,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || isExtraSlot)
            return;

        if (!proposedStatus.Equals(ParkingConstants.SubscriptionStatus.Active, StringComparison.OrdinalIgnoreCase))
            return;

        if (endDate.Date < DateTime.Today)
            return;

        var overlap = await db.ParkingSubscriptions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        !x.IsExtraSlot &&
                        x.Status == ParkingConstants.SubscriptionStatus.Active &&
                        x.EndDate.Date >= DateTime.Today &&
                        x.StartDate.Date <= endDate.Date &&
                        x.EndDate.Date >= startDate.Date)
            .Where(x => !currentSubscriptionId.HasValue || x.SubscriptionId != currentSubscriptionId.Value)
            .OrderBy(x => x.StartDate)
            .ThenBy(x => x.SubscriptionId)
            .Select(x => new
            {
                x.SubscriptionId,
                x.StartDate,
                x.EndDate,
                x.SlotsPurchased
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (overlap == null)
            return;

        throw new InvalidOperationException(
            $"Cannot save this regular subscription because it overlaps active regular subscription #{overlap.SubscriptionId} " +
            $"({overlap.StartDate:yyyy-MM-dd} to {overlap.EndDate:yyyy-MM-dd}, {overlap.SlotsPurchased} slot(s)). " +
            "Cancel, expire, or edit the existing regular subscription first. Extra-slot subscriptions can still be created for additional capacity.");
    }

    private async Task EnsureSubscriptionEditKeepsInsideVehiclesCoveredAsync(
        ParkingSubscription currentSubscription,
        int proposedSlotsPurchased,
        DateTime proposedStartDate,
        DateTime proposedEndDate,
        string proposedStatus,
        CancellationToken cancellationToken)
    {
        var insideSessions = await db.ParkingSessions
            .AsNoTracking()
            .Where(x => x.CompanyId == currentSubscription.CompanyId &&
                        x.Status == ParkingConstants.SessionStatus.Inside)
            .OrderBy(x => x.EntryTime ?? x.CreatedDate)
            .ThenBy(x => x.SessionId)
            .ToListAsync(cancellationToken);

        if (insideSessions.Count == 0)
            return;

        var subscriptions = await db.ParkingSubscriptions
            .AsNoTracking()
            .Where(x => x.CompanyId == currentSubscription.CompanyId &&
                        (x.Status != ParkingConstants.SubscriptionStatus.Cancelled || x.SubscriptionId == currentSubscription.SubscriptionId))
            .Select(x => new SubscriptionSlotSnapshot(
                x.SubscriptionId,
                x.CompanyId,
                x.SlotsPurchased,
                x.StartDate,
                x.EndDate,
                x.Status))
            .ToListAsync(cancellationToken);

        var beforeCoveredSessionIds = GetCoveredInsideSessionIds(insideSessions, subscriptions, DateTime.Today);

        var proposedSubscriptions = subscriptions
            .Select(x => x.SubscriptionId == currentSubscription.SubscriptionId
                ? x with
                {
                    SlotsPurchased = proposedSlotsPurchased,
                    StartDate = proposedStartDate.Date,
                    EndDate = proposedEndDate.Date,
                    Status = proposedStatus
                }
                : x)
            .ToList();

        if (proposedSubscriptions.All(x => x.SubscriptionId != currentSubscription.SubscriptionId))
        {
            proposedSubscriptions.Add(new SubscriptionSlotSnapshot(
                currentSubscription.SubscriptionId,
                currentSubscription.CompanyId,
                proposedSlotsPurchased,
                proposedStartDate.Date,
                proposedEndDate.Date,
                proposedStatus));
        }

        var afterCoveredSessionIds = GetCoveredInsideSessionIds(insideSessions, proposedSubscriptions, DateTime.Today);
        var newlyUncoveredCount = beforeCoveredSessionIds.Count(x => !afterCoveredSessionIds.Contains(x));
        if (newlyUncoveredCount == 0)
            return;

        var vehiclesLinkedToEditedSubscription = insideSessions.Count(x => x.SubscriptionId == currentSubscription.SubscriptionId);
        var beforeActiveSlots = CountActiveSubscriptionSlots(subscriptions, DateTime.Today);
        var afterActiveSlots = CountActiveSubscriptionSlots(proposedSubscriptions, DateTime.Today);

        throw new InvalidOperationException(
            $"Cannot update this subscription because {newlyUncoveredCount} inside vehicle(s) would lose active slot coverage. " +
            $"{vehiclesLinkedToEditedSubscription} inside vehicle(s) are currently linked to this subscription. " +
            $"Active slots for this company would change from {beforeActiveSlots} to {afterActiveSlots}. " +
            "Exit the vehicle(s), add enough active slots, or move them to another active subscription before changing the date, status, or slot count.");
    }

    private static HashSet<int> GetCoveredInsideSessionIds(
        IReadOnlyList<ParkingSession> insideSessions,
        IReadOnlyList<SubscriptionSlotSnapshot> subscriptions,
        DateTime coverageDate)
    {
        var activeByCompany = subscriptions
            .Where(x => IsSubscriptionActiveForSlotCoverage(x, coverageDate))
            .OrderBy(x => x.CompanyId)
            .ThenBy(x => x.EndDate)
            .ThenBy(x => x.SubscriptionId)
            .GroupBy(x => x.CompanyId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var covered = new HashSet<int>();

        foreach (var companySessions in insideSessions.GroupBy(x => x.CompanyId))
        {
            if (!activeByCompany.TryGetValue(companySessions.Key, out var companySubscriptions))
                continue;

            var remainingSlots = companySubscriptions.ToDictionary(x => x.SubscriptionId, x => Math.Max(x.SlotsPurchased, 0));
            var sessionsNeedingCoverage = new List<ParkingSession>();

            foreach (var session in companySessions.OrderBy(x => x.EntryTime ?? x.CreatedDate).ThenBy(x => x.SessionId))
            {
                if (session.SubscriptionId.HasValue &&
                    remainingSlots.TryGetValue(session.SubscriptionId.Value, out var slotsLeft) &&
                    slotsLeft > 0)
                {
                    remainingSlots[session.SubscriptionId.Value] = slotsLeft - 1;
                    covered.Add(session.SessionId);
                    continue;
                }

                sessionsNeedingCoverage.Add(session);
            }

            foreach (var session in sessionsNeedingCoverage)
            {
                var targetSubscription = companySubscriptions
                    .FirstOrDefault(x => remainingSlots.TryGetValue(x.SubscriptionId, out var slotsLeft) && slotsLeft > 0);

                if (targetSubscription == null)
                    continue;

                remainingSlots[targetSubscription.SubscriptionId]--;
                covered.Add(session.SessionId);
            }
        }

        return covered;
    }

    private static int CountActiveSubscriptionSlots(IReadOnlyList<SubscriptionSlotSnapshot> subscriptions, DateTime coverageDate)
    {
        return subscriptions
            .Where(x => IsSubscriptionActiveForSlotCoverage(x, coverageDate))
            .Sum(x => Math.Max(x.SlotsPurchased, 0));
    }

    private static bool IsSubscriptionActiveForSlotCoverage(SubscriptionSlotSnapshot subscription, DateTime coverageDate)
    {
        return subscription.Status.Equals(ParkingConstants.SubscriptionStatus.Active, StringComparison.OrdinalIgnoreCase) &&
               subscription.StartDate.Date <= coverageDate.Date &&
               subscription.EndDate.Date >= coverageDate.Date &&
               subscription.SlotsPurchased > 0;
    }

    private async Task ReassignInsideSessionsToActiveSubscriptionsAsync(int? companyId, int operatorId, CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var insideQuery = db.ParkingSessions
            .Include(x => x.Subscription)
            .Where(x => x.Status == ParkingConstants.SessionStatus.Inside);

        if (companyId.HasValue && companyId.Value > 0)
            insideQuery = insideQuery.Where(x => x.CompanyId == companyId.Value);

        var insideSessions = await insideQuery
            .OrderBy(x => x.CompanyId)
            .ThenBy(x => x.EntryTime ?? x.CreatedDate)
            .ThenBy(x => x.SessionId)
            .ToListAsync(cancellationToken);

        if (insideSessions.Count == 0)
            return;

        var companyIds = insideSessions.Select(x => x.CompanyId).Distinct().ToList();
        var activeSubscriptions = await db.ParkingSubscriptions
            .Where(x => companyIds.Contains(x.CompanyId) &&
                        x.Status == ParkingConstants.SubscriptionStatus.Active &&
                        x.StartDate.Date <= today &&
                        x.EndDate.Date >= today &&
                        x.SlotsPurchased > 0)
            .OrderBy(x => x.CompanyId)
            .ThenBy(x => x.EndDate)
            .ThenBy(x => x.SubscriptionId)
            .ToListAsync(cancellationToken);

        if (activeSubscriptions.Count == 0)
            return;

        var activeByCompany = activeSubscriptions
            .GroupBy(x => x.CompanyId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var changed = false;

        foreach (var companySessions in insideSessions.GroupBy(x => x.CompanyId))
        {
            if (!activeByCompany.TryGetValue(companySessions.Key, out var companyActiveSubscriptions))
                continue;

            var remainingSlots = companyActiveSubscriptions.ToDictionary(x => x.SubscriptionId, x => Math.Max(x.SlotsPurchased, 0));
            var sessionsNeedingCoverage = new List<ParkingSession>();

            foreach (var session in companySessions.OrderBy(x => x.EntryTime ?? x.CreatedDate).ThenBy(x => x.SessionId))
            {
                if (session.SubscriptionId.HasValue &&
                    remainingSlots.TryGetValue(session.SubscriptionId.Value, out var slotsLeft) &&
                    slotsLeft > 0)
                {
                    remainingSlots[session.SubscriptionId.Value] = slotsLeft - 1;

                    if (session.OverstayDays != 0 || session.OverstayAmount != 0)
                    {
                        session.OverstayDays = 0;
                        session.OverstayAmount = 0;
                        changed = true;
                    }

                    continue;
                }

                sessionsNeedingCoverage.Add(session);
            }

            foreach (var session in sessionsNeedingCoverage)
            {
                var targetSubscription = companyActiveSubscriptions
                    .FirstOrDefault(x => remainingSlots.TryGetValue(x.SubscriptionId, out var slotsLeft) && slotsLeft > 0);

                if (targetSubscription == null)
                    continue;

                var oldSubscriptionId = session.SubscriptionId;
                session.SubscriptionId = targetSubscription.SubscriptionId;
                session.Subscription = targetSubscription;
                session.OverstayDays = 0;
                session.OverstayAmount = 0;
                remainingSlots[targetSubscription.SubscriptionId]--;
                changed = true;

                await AddActivityAsync(
                    "SubscriptionAutoMoved",
                    session.CompanyId,
                    session.SessionId,
                    session.BarcodeNo,
                    session.PlateNo,
                    "Subscription Reassigned",
                    $"Inside vehicle moved from subscription #{oldSubscriptionId?.ToString() ?? "N/A"} to active subscription #{targetSubscription.SubscriptionId} because an active company slot was available.",
                    operatorId,
                    cancellationToken);
            }
        }

        if (changed)
            await repository.SaveChangesAsync(cancellationToken);
    }

    private async Task<ExitScanResultDto> BuildExitScanResultAsync(string barcodeNo, int operatorId, bool saveDisplayEvent, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var session = await repository.GetSessionByBarcodeAsync(barcodeNo, cancellationToken);
        if (session == null)
        {
            var invalid = new ExitScanResultDto(false, null, 0, barcodeNo, null, null, null, now, null, null, ParkingConstants.PaymentStatus.Unpaid, 0, 0, 0, 0, ParkingConstants.ExitStatus.InvalidBarcode, "Barcode not found.");
            if (saveDisplayEvent) await AddInvalidDisplayAsync(barcodeNo, "INVALID BARCODE", "Barcode not found. Please contact staff.", operatorId, cancellationToken);
            return invalid;
        }

        if (session.Status == ParkingConstants.SessionStatus.Exited || session.BarcodeStatus == ParkingConstants.BarcodeStatus.Used)
        {
            var invalid = new ExitScanResultDto(false, session.SessionId, session.CompanyId, session.BarcodeNo, session.PlateNo, session.Company.CompanyName, session.EntryTime, now, null, session.Subscription?.EndDate, ParkingConstants.PaymentStatus.Unpaid, 0, 0, 0, 0, ParkingConstants.ExitStatus.AlreadyExited, "Barcode already used. Vehicle has already exited.");
            if (saveDisplayEvent)
            {
                await AddOutsideDisplayAsync(session, ParkingConstants.OutsideDisplayStatus.InvalidBarcode, "INVALID BARCODE", "Barcode already used. Please contact staff.", 0, 0, cancellationToken);
                await AddActivityAsync(ParkingConstants.GateActionType.ExitScanned, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, ParkingConstants.ExitStatus.AlreadyExited, "Barcode already used. Vehicle has already exited.", operatorId, cancellationToken, result: "Failure");
                await repository.SaveChangesAsync(cancellationToken);
            }
            return invalid;
        }

        if (session.Status != ParkingConstants.SessionStatus.Inside || session.BarcodeStatus != ParkingConstants.BarcodeStatus.Active)
        {
            var invalid = new ExitScanResultDto(false, session.SessionId, session.CompanyId, session.BarcodeNo, session.PlateNo, session.Company.CompanyName, session.EntryTime, now, null, session.Subscription?.EndDate, ParkingConstants.PaymentStatus.Unpaid, 0, 0, 0, 0, ParkingConstants.ExitStatus.InvalidBarcode, "Barcode is not active for exit.");
            if (saveDisplayEvent)
            {
                await AddOutsideDisplayAsync(session, ParkingConstants.OutsideDisplayStatus.InvalidBarcode, "INVALID BARCODE", "Barcode is not active for exit.", 0, 0, cancellationToken);
                await AddActivityAsync(ParkingConstants.GateActionType.ExitScanned, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, ParkingConstants.ExitStatus.InvalidBarcode, "Barcode is not active for exit.", operatorId, cancellationToken, result: "Failure");
                await repository.SaveChangesAsync(cancellationToken);
            }
            return invalid;
        }

        await ReassignInsideSessionsToActiveSubscriptionsAsync(session.CompanyId, operatorId, cancellationToken);

        var settings = await repository.GetSettingsAsync(cancellationToken);
        var overstayDailyCharge = GetDecimalSetting(settings, "OverstayDailyCharge", 50m);
        var overstayDays = 0;
        if (session.Subscription?.EndDate.Date < now.Date)
            overstayDays = (now.Date - session.Subscription.EndDate.Date).Days;

        var overstayAmount = overstayDays * overstayDailyCharge;

        session.OverstayDays = overstayDays;
        session.OverstayAmount = overstayAmount;

        if (saveDisplayEvent && overstayAmount > 0)
        {
            await EnsureOverstayInvoiceIfNeededAsync(session.SessionId, operatorId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var overstayInvoice = overstayAmount > 0
            ? await db.ParkingInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SessionId == session.SessionId && x.InvoiceType == "Overstay", cancellationToken)
            : null;

        var pending = await repository.GetPendingAmountAsync(session.CompanyId, cancellationToken);
        var unpaidOverstayBalance = overstayInvoice?.BalanceAmount ?? 0m;
        var totalPayable = pending + (overstayAmount > 0 && overstayInvoice == null ? overstayAmount : 0);

        // A paid overstay invoice may still have historical overstay days on the session.
        // Exit status must be decided from payable balance, not from the overstay day count.
        if (overstayInvoice != null && unpaidOverstayBalance <= 0 && pending <= 0)
            totalPayable = 0;

        var paymentStatus = totalPayable <= 0 ? ParkingConstants.PaymentStatus.Paid : ParkingConstants.PaymentStatus.Unpaid;
        var finalStatus = totalPayable <= 0 ? ParkingConstants.ExitStatus.ClearToExit : ParkingConstants.ExitStatus.PaymentRequired;
        var message = totalPayable <= 0
            ? "Clear to exit. No pending amount."
            : $"Payment required AED {totalPayable:n2}. Open Collect Payment and collect the invoice before exit.";

        await AddActivityAsync(ParkingConstants.GateActionType.ExitScanned, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, finalStatus, message, operatorId, cancellationToken);

        if (saveDisplayEvent)
        {
            if (totalPayable <= 0)
                await AddOutsideDisplayAsync(session, ParkingConstants.OutsideDisplayStatus.ClearToExit, "CLEAR TO EXIT", "Please wait for gate operator approval.", 0, 0, cancellationToken);
            else if (overstayAmount > 0)
                await AddOutsideDisplayAsync(session, ParkingConstants.OutsideDisplayStatus.OverstayDetected, "OVERSTAY DETECTED", "Please park aside and clear payment.", totalPayable, overstayDays, cancellationToken);
            else
                await AddOutsideDisplayAsync(session, ParkingConstants.OutsideDisplayStatus.PaymentRequired, "PAYMENT REQUIRED", "Please park aside and clear pending amount.", totalPayable, overstayDays, cancellationToken);

            await repository.SaveChangesAsync(cancellationToken);
        }

        return new ExitScanResultDto(true, session.SessionId, session.CompanyId, session.BarcodeNo, session.PlateNo, session.Company.CompanyName, session.EntryTime, now, session.EntryTime == null ? null : FormatDuration(now - session.EntryTime.Value), session.Subscription?.EndDate, paymentStatus, pending, overstayDays, overstayAmount, totalPayable, finalStatus, message);
    }

    private async Task EnsureOverstayInvoiceIfNeededAsync(int sessionId, int operatorId, CancellationToken cancellationToken)
    {
        var session = await repository.GetSessionAsync(sessionId, cancellationToken);
        if (session == null || session.OverstayAmount <= 0)
            return;

        var existing = await db.ParkingInvoices
            .FirstOrDefaultAsync(x => x.SessionId == sessionId && x.InvoiceType == "Overstay", cancellationToken);

        if (existing != null)
        {
            // If the vehicle is scanned again before payment and the overstay days changed,
            // keep the unpaid overstay invoice synchronized with the latest calculated amount.
            if (existing.PaidAmount <= 0 && existing.BalanceAmount > 0 && existing.TotalAmount != session.OverstayAmount)
            {
                existing.SubTotal = session.OverstayAmount;
                existing.TotalAmount = session.OverstayAmount;
                existing.BalanceAmount = session.OverstayAmount;
                existing.Status = ParkingConstants.PaymentStatus.Unpaid;
                existing.Remarks = $"Overstay charge for barcode {session.BarcodeNo} - {session.OverstayDays} day(s)";
            }

            return;
        }

        var invoiceNo = await GenerateNextInvoiceNoAsync(cancellationToken);
        await db.ParkingInvoices.AddAsync(new ParkingInvoice
        {
            InvoiceNo = invoiceNo,
            CompanyId = session.CompanyId,
            SessionId = session.SessionId,
            InvoiceType = "Overstay",
            InvoiceDate = DateTime.Now,
            DueDate = DateTime.Today,
            Slots = 0,
            SubTotal = session.OverstayAmount,
            TotalAmount = session.OverstayAmount,
            BalanceAmount = session.OverstayAmount,
            Status = ParkingConstants.PaymentStatus.Unpaid,
            Remarks = $"Overstay charge for barcode {session.BarcodeNo} - {session.OverstayDays} day(s)",
            CreatedBy = operatorId,
            CreatedDate = DateTime.Now
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<PaymentAllocation>> ApplyPaymentToOldestInvoicesAsync(int companyId, decimal amount, CancellationToken cancellationToken)
    {
        var invoices = await db.ParkingInvoices
            .Where(x => x.CompanyId == companyId && x.BalanceAmount > 0 && x.Status != "Cancelled")
            .OrderBy(x => x.InvoiceDate)
            .ThenBy(x => x.InvoiceId)
            .ToListAsync(cancellationToken);

        var allocations = new List<PaymentAllocation>();
        foreach (var invoice in invoices)
        {
            if (amount <= 0) break;
            var apply = Math.Min(amount, invoice.BalanceAmount);
            var balanceBefore = invoice.BalanceAmount;
            var statusBefore = invoice.Status;
            ApplyAmountToInvoice(invoice, apply);
            allocations.Add(new PaymentAllocation(invoice.InvoiceId, invoice.InvoiceNo, apply, balanceBefore, invoice.BalanceAmount, statusBefore, invoice.Status));
            amount -= apply;
        }

        if (amount > 0)
            throw new InvalidOperationException($"Payment amount could not be fully allocated. Remaining unapplied amount AED {amount:n2}.");

        return allocations;
    }

    private static void ApplyAmountToInvoice(ParkingInvoice invoice, decimal amount)
    {
        if (amount <= 0) return;
        var apply = Math.Min(amount, invoice.BalanceAmount);
        invoice.PaidAmount += apply;
        invoice.BalanceAmount -= apply;
        invoice.Status = GetInvoiceStatus(invoice.BalanceAmount, invoice.PaidAmount);
    }

    private sealed record PaymentAllocation(int InvoiceId, string InvoiceNo, decimal Amount, decimal BalanceBefore, decimal BalanceAfter, string StatusBefore, string StatusAfter);

    private sealed record PaymentSummary(int InvoiceId, decimal Amount, int Count);

    private async Task EnsureParkingCapacityAvailableAsync(CancellationToken cancellationToken)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        if (!GetBoolSetting(settings, "BlockEntryWhenParkingFull", true))
            return;

        var totalCapacity = GetIntSetting(settings, "TotalParkingCapacity", 500);
        var inside = await db.ParkingSessions.CountAsync(x => x.Status == ParkingConstants.SessionStatus.Inside, cancellationToken);
        if (inside >= totalCapacity)
            throw new InvalidOperationException($"Parking capacity is full ({inside}/{totalCapacity}). Allow exit or increase parking capacity in settings.");
    }

    private async Task<string> GenerateNextBarcodeNoAsync(CancellationToken cancellationToken)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var prefix = NormalizeCounterPrefix(GetStringSetting(settings, "BarcodePrefix", "KP"), "KP");
        var key = "BARCODE_" + prefix + "_" + DateTime.Now.ToString("yyyyMMdd");
        var counter = await db.SystemCounters.FirstOrDefaultAsync(x => x.CounterName == key, cancellationToken);
        if (counter == null)
        {
            counter = new SystemCounter { CounterName = key, LastNumber = 0, UpdatedDate = DateTime.Now };
            await db.SystemCounters.AddAsync(counter, cancellationToken);
        }
        counter.LastNumber += 1;
        counter.UpdatedDate = DateTime.Now;
        await db.SaveChangesAsync(cancellationToken);
        return prefix + DateTime.Now.ToString("yyMMdd") + counter.LastNumber.ToString("D6");
    }

    private async Task<string> GenerateNextCompanyCodeAsync(CancellationToken cancellationToken) => await GenerateFromCounterAsync("CMP", "CMP", cancellationToken);

    private async Task<string> GenerateNextInvoiceNoAsync(CancellationToken cancellationToken)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var prefix = NormalizeCounterPrefix(GetStringSetting(settings, "InvoicePrefix", "INV"), "INV");
        return await GenerateFromCounterAsync(prefix, prefix, cancellationToken);
    }

    private async Task<string> GenerateNextReceiptNoAsync(CancellationToken cancellationToken)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var prefix = NormalizeCounterPrefix(GetStringSetting(settings, "ReceiptPrefix", "RCT"), "RCT");
        return await GenerateFromCounterAsync(prefix, prefix, cancellationToken);
    }

    private async Task<string> GenerateFromCounterAsync(string prefix, string counterPrefix, CancellationToken cancellationToken)
    {
        var key = counterPrefix + "_" + DateTime.Now.ToString("yyyyMMdd");
        var counter = await db.SystemCounters.FirstOrDefaultAsync(x => x.CounterName == key, cancellationToken);
        if (counter == null)
        {
            counter = new SystemCounter { CounterName = key, LastNumber = 0, UpdatedDate = DateTime.Now };
            await db.SystemCounters.AddAsync(counter, cancellationToken);
        }
        counter.LastNumber += 1;
        counter.UpdatedDate = DateTime.Now;
        await db.SaveChangesAsync(cancellationToken);
        return prefix + "-" + DateTime.Now.ToString("yyMMdd") + "-" + counter.LastNumber.ToString("D5");
    }

    private async Task AddActivityAsync(
        string actionType,
        int? companyId,
        int? sessionId,
        string? barcodeNo,
        string? plateNo,
        string status,
        string? message,
        int operatorId,
        CancellationToken cancellationToken,
        IReadOnlyList<SystemActivityChange>? changes = null,
        string entityType = "GateActivity",
        string? entityId = null,
        string result = "Success")
    {
        await repository.AddActivityAsync(new GateActivityLog
        {
            ActionType = actionType,
            CompanyId = companyId,
            SessionId = sessionId,
            BarcodeNo = barcodeNo,
            PlateNo = plateNo,
            Status = status,
            Message = message,
            OperatorId = operatorId,
            ActionDate = DateTime.Now
        }, cancellationToken);

        var userDisplayName = await ResolveOperatorDisplayNameAsync(operatorId, cancellationToken);
        var log = new Domain.Entities.SystemActivityLog
        {
            ActivityDate = DateTime.Now,
            UserId = operatorId > 0 ? operatorId : null,
            Username = userDisplayName,
            ModuleKey = "gate_operation",
            ActionKey = actionType,
            Result = string.IsNullOrWhiteSpace(result) ? "Success" : result,
            EntityType = entityType,
            EntityId = entityId ?? sessionId?.ToString() ?? companyId?.ToString(),
            Title = status,
            Message = message
        };

        foreach (var change in changes ?? [])
        {
            if (string.Equals(change.OldValue, change.NewValue, StringComparison.Ordinal))
                continue;

            log.Details.Add(new Domain.Entities.SystemActivityLogDetail
            {
                FieldName = TrimAuditValue(change.FieldName, 150) ?? "-",
                OldValue = TrimAuditValue(change.OldValue, 2000),
                NewValue = TrimAuditValue(change.NewValue, 2000)
            });
        }

        await db.SystemActivityLogs.AddAsync(log, cancellationToken);
    }

    private async Task<string> ResolveOperatorDisplayNameAsync(int operatorId, CancellationToken cancellationToken)
    {
        if (operatorId <= 0)
            return "-";

        var user = await db.AppUsers
            .AsNoTracking()
            .Where(x => x.UserId == operatorId)
            .Select(x => new { x.FullName, x.Username })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
            return $"User #{operatorId}";

        if (!string.IsNullOrWhiteSpace(user.FullName))
            return user.FullName.Trim();

        if (!string.IsNullOrWhiteSpace(user.Username))
            return user.Username.Trim();

        return $"User #{operatorId}";
    }

    private static string? AuditValue(object? value)
    {
        return value switch
        {
            null => null,
            DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss"),
            DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            decimal amount => amount.ToString("0.##"),
            double number => number.ToString("0.##"),
            float number => number.ToString("0.##"),
            bool flag => flag ? "Yes" : "No",
            _ => value.ToString()
        };
    }

    private static string? TrimAuditValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var clean = value.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private async Task AddOutsideDisplayAsync(ParkingSession session, string displayStatus, string mainMessage, string? subMessage, decimal amountDue, int overstayDays, CancellationToken cancellationToken)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        if (!GetBoolSetting(settings, "OutsideDisplayEnabled", true))
            return;

        var resolvedMessages = ResolveOutsideDisplayMessages(settings, displayStatus, mainMessage, subMessage);
        await repository.AddOutsideDisplayEventAsync(new OutsideDisplayEvent
        {
            SessionId = session.SessionId,
            BarcodeNo = session.BarcodeNo,
            PlateNo = session.PlateNo,
            CompanyName = session.Company.CompanyName,
            DisplayStatus = displayStatus,
            MainMessage = resolvedMessages.MainMessage,
            SubMessage = resolvedMessages.SubMessage,
            AmountDue = amountDue,
            OverstayDays = overstayDays,
            CreatedDate = DateTime.Now
        }, cancellationToken);
    }

    private async Task AddInvalidDisplayAsync(string barcodeNo, string mainMessage, string subMessage, int operatorId, CancellationToken cancellationToken)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        if (GetBoolSetting(settings, "OutsideDisplayEnabled", true))
        {
            var resolvedMessages = ResolveOutsideDisplayMessages(settings, ParkingConstants.OutsideDisplayStatus.InvalidBarcode, mainMessage, subMessage);
            await repository.AddOutsideDisplayEventAsync(new OutsideDisplayEvent
            {
                BarcodeNo = barcodeNo,
                DisplayStatus = ParkingConstants.OutsideDisplayStatus.InvalidBarcode,
                MainMessage = resolvedMessages.MainMessage,
                SubMessage = resolvedMessages.SubMessage,
                CreatedDate = DateTime.Now
            }, cancellationToken);
        }

        await AddActivityAsync(ParkingConstants.GateActionType.ExitScanned, null, null, barcodeNo, null, ParkingConstants.ExitStatus.InvalidBarcode, subMessage, operatorId, cancellationToken, result: "Failure");
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static (string MainMessage, string? SubMessage) ResolveOutsideDisplayMessages(Dictionary<string, string> settings, string displayStatus, string mainMessage, string? subMessage)
    {
        if (displayStatus.Equals(ParkingConstants.OutsideDisplayStatus.EntryAllowed, StringComparison.OrdinalIgnoreCase))
            return (GetStringSetting(settings, "OutsideDisplayEntryAllowedMainMessage", mainMessage), GetStringSetting(settings, "OutsideDisplayEntryAllowedSubMessage", subMessage ?? string.Empty));

        if (displayStatus.Equals(ParkingConstants.OutsideDisplayStatus.ClearToExit, StringComparison.OrdinalIgnoreCase))
            return (GetStringSetting(settings, "OutsideDisplayClearToExitMainMessage", mainMessage), GetStringSetting(settings, "OutsideDisplayClearToExitSubMessage", subMessage ?? string.Empty));

        if (displayStatus.Equals(ParkingConstants.OutsideDisplayStatus.PaymentRequired, StringComparison.OrdinalIgnoreCase))
            return (GetStringSetting(settings, "OutsideDisplayPaymentRequiredMainMessage", mainMessage), GetStringSetting(settings, "OutsideDisplayPaymentRequiredSubMessage", subMessage ?? string.Empty));

        if (displayStatus.Equals(ParkingConstants.OutsideDisplayStatus.OverstayDetected, StringComparison.OrdinalIgnoreCase))
            return (GetStringSetting(settings, "OutsideDisplayOverstayMainMessage", mainMessage), GetStringSetting(settings, "OutsideDisplayOverstaySubMessage", subMessage ?? string.Empty));

        if (displayStatus.Equals(ParkingConstants.OutsideDisplayStatus.InvalidBarcode, StringComparison.OrdinalIgnoreCase))
            return (GetStringSetting(settings, "OutsideDisplayInvalidMainMessage", mainMessage), GetStringSetting(settings, "OutsideDisplayInvalidSubMessage", subMessage ?? string.Empty));

        return (mainMessage, subMessage);
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays} days {span.Hours} hours";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours} hours {span.Minutes} minutes";
        return $"{span.Minutes} minutes";
    }



    private async Task<IReadOnlyList<InvoiceListItemDto>> BuildInvoiceListItemsAsync(InvoiceListQueryRequest request, CancellationToken cancellationToken)
    {
        var query = db.ParkingInvoices
            .AsNoTracking()
            .Include(x => x.Company)
            .AsQueryable();

        var search = (request.SearchText ?? string.Empty).Trim();
        if (search.Length > 0)
        {
            query = query.Where(x => x.InvoiceNo.Contains(search) ||
                                     x.Company.CompanyName.Contains(search) ||
                                     x.Company.CompanyCode.Contains(search) ||
                                     (x.PlanType != null && x.PlanType.Contains(search)) ||
                                     (x.Remarks != null && x.Remarks.Contains(search)));
        }

        if (request.CompanyId.HasValue && request.CompanyId.Value > 0)
            query = query.Where(x => x.CompanyId == request.CompanyId.Value);

        var invoiceType = NormalizeInvoiceTypeFilter(request.InvoiceType);
        if (!string.IsNullOrWhiteSpace(invoiceType))
            query = query.Where(x => x.InvoiceType == invoiceType);

        var status = NormalizeInvoiceStatusFilter(request.Status);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        if (request.InvoiceFrom.HasValue)
            query = query.Where(x => x.InvoiceDate.Date >= request.InvoiceFrom.Value.Date);
        if (request.InvoiceTo.HasValue)
            query = query.Where(x => x.InvoiceDate.Date <= request.InvoiceTo.Value.Date);
        if (request.DueFrom.HasValue)
            query = query.Where(x => x.DueDate.HasValue && x.DueDate.Value.Date >= request.DueFrom.Value.Date);
        if (request.DueTo.HasValue)
            query = query.Where(x => x.DueDate.HasValue && x.DueDate.Value.Date <= request.DueTo.Value.Date);

        var list = await query.ToListAsync(cancellationToken);
        var today = DateTime.Today;
        var tab = (request.Tab ?? "All").Trim();
        if (tab.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.BalanceAmount > 0 && !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)).ToList();
        else if (tab.Equals("Paid", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.Status.Equals(ParkingConstants.PaymentStatus.Paid, StringComparison.OrdinalIgnoreCase)).ToList();
        else if (tab.Equals("Partial", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.Status.Equals(ParkingConstants.PaymentStatus.Partial, StringComparison.OrdinalIgnoreCase)).ToList();
        else if (tab.Equals("Unpaid", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.Status.Equals(ParkingConstants.PaymentStatus.Unpaid, StringComparison.OrdinalIgnoreCase)).ToList();
        else if (tab.Equals("Overdue", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.BalanceAmount > 0 && x.DueDate.HasValue && x.DueDate.Value.Date < today && !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)).ToList();
        else if (tab.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)).ToList();
        else if (tab.Equals("Manual", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.InvoiceType.Equals("Manual", StringComparison.OrdinalIgnoreCase)).ToList();
        else if (tab.Equals("Subscription", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.InvoiceType.Equals("Subscription", StringComparison.OrdinalIgnoreCase)).ToList();
        else if (tab.Equals("ExtraSlot", StringComparison.OrdinalIgnoreCase) || tab.Equals("Extra Slot", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.InvoiceType.Equals("ExtraSlot", StringComparison.OrdinalIgnoreCase)).ToList();
        else if (tab.Equals("Overstay", StringComparison.OrdinalIgnoreCase))
            list = list.Where(x => x.InvoiceType.Equals("Overstay", StringComparison.OrdinalIgnoreCase)).ToList();

        return list.Select(ToInvoiceListItem).ToList();
    }

    private static IEnumerable<InvoiceListItemDto> SortInvoices(IEnumerable<InvoiceListItemDto> rows, string? sortBy, string? sortDirection)
    {
        var desc = (sortDirection ?? "Desc").Equals("Desc", StringComparison.OrdinalIgnoreCase);
        var key = (sortBy ?? "InvoiceDate").Trim();
        return key switch
        {
            "InvoiceNo" => desc ? rows.OrderByDescending(x => x.InvoiceNo) : rows.OrderBy(x => x.InvoiceNo),
            "CompanyName" => desc ? rows.OrderByDescending(x => x.CompanyName) : rows.OrderBy(x => x.CompanyName),
            "InvoiceType" => desc ? rows.OrderByDescending(x => x.InvoiceType) : rows.OrderBy(x => x.InvoiceType),
            "DueDate" => desc ? rows.OrderByDescending(x => x.DueDate) : rows.OrderBy(x => x.DueDate),
            "TotalAmount" => desc ? rows.OrderByDescending(x => x.TotalAmount) : rows.OrderBy(x => x.TotalAmount),
            "PaidAmount" => desc ? rows.OrderByDescending(x => x.PaidAmount) : rows.OrderBy(x => x.PaidAmount),
            "BalanceAmount" => desc ? rows.OrderByDescending(x => x.BalanceAmount) : rows.OrderBy(x => x.BalanceAmount),
            "Status" => desc ? rows.OrderByDescending(x => x.Status) : rows.OrderBy(x => x.Status),
            "CreatedDate" => desc ? rows.OrderByDescending(x => x.CreatedDate) : rows.OrderBy(x => x.CreatedDate),
            _ => desc ? rows.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.InvoiceId) : rows.OrderBy(x => x.InvoiceDate).ThenBy(x => x.InvoiceId)
        };
    }

    private static InvoiceListItemDto ToInvoiceListItem(ParkingInvoice x) => new(
        x.InvoiceId,
        x.InvoiceNo,
        x.CompanyId,
        x.Company?.CompanyCode ?? string.Empty,
        x.Company?.CompanyName ?? string.Empty,
        x.InvoiceType,
        x.InvoiceDate,
        x.DueDate,
        x.PlanType,
        x.Slots,
        x.SubTotal,
        x.DiscountAmount,
        x.VatAmount,
        x.TotalAmount,
        x.PaidAmount,
        x.BalanceAmount,
        x.Status,
        x.SubscriptionId,
        x.SessionId,
        x.CreatedDate,
        x.ModifiedDate,
        x.Remarks,
        x.CancellationReason);

    private static string NormalizeInvoiceType(string? invoiceType)
    {
        var value = (invoiceType ?? string.Empty).Trim().Replace(" ", string.Empty);
        if (value.Equals("Subscription", StringComparison.OrdinalIgnoreCase)) return "Subscription";
        if (value.Equals("ExtraSlot", StringComparison.OrdinalIgnoreCase)) return "ExtraSlot";
        if (value.Equals("Overstay", StringComparison.OrdinalIgnoreCase)) return "Overstay";
        if (value.Equals("Adjustment", StringComparison.OrdinalIgnoreCase)) return "Adjustment";
        return "Manual";
    }

    private static string? NormalizeInvoiceTypeFilter(string? invoiceType)
    {
        var value = (invoiceType ?? string.Empty).Trim();
        if (value.Length == 0 || value.Equals("All", StringComparison.OrdinalIgnoreCase)) return null;
        return NormalizeInvoiceType(value);
    }

    private static string? NormalizeInvoiceStatusFilter(string? status)
    {
        var value = (status ?? string.Empty).Trim();
        if (value.Length == 0 || value.Equals("All", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Equals(ParkingConstants.PaymentStatus.Paid, StringComparison.OrdinalIgnoreCase)) return ParkingConstants.PaymentStatus.Paid;
        if (value.Equals(ParkingConstants.PaymentStatus.Partial, StringComparison.OrdinalIgnoreCase)) return ParkingConstants.PaymentStatus.Partial;
        if (value.Equals(ParkingConstants.PaymentStatus.Unpaid, StringComparison.OrdinalIgnoreCase)) return ParkingConstants.PaymentStatus.Unpaid;
        if (value.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) return "Cancelled";
        return null;
    }




    private async Task<IReadOnlyList<LiveParkingListItemDto>> BuildLiveParkingListItemsAsync(LiveParkingListQueryRequest request, CancellationToken cancellationToken)
    {
        await ReassignInsideSessionsToActiveSubscriptionsAsync(request.CompanyId, 0, cancellationToken);

        var query = db.ParkingSessions
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Subscription)
            .AsQueryable();

        var tab = (request.Tab ?? "Inside").Trim();
        if (tab.Equals("Inside", StringComparison.OrdinalIgnoreCase) || tab.Length == 0)
            query = query.Where(x => x.Status == ParkingConstants.SessionStatus.Inside);
        else if (tab.Equals("Generated", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == ParkingConstants.SessionStatus.BarcodeGenerated);
        else if (tab.Equals("Exited", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == ParkingConstants.SessionStatus.Exited);
        else if (tab.Equals("Invalid", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.BarcodeStatus == ParkingConstants.BarcodeStatus.Invalid || x.Status == ParkingConstants.SessionStatus.Cancelled || x.Status == ParkingConstants.SessionStatus.Rejected);
        else if (tab.Equals("Overstay", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == ParkingConstants.SessionStatus.Inside && x.Subscription != null && x.Subscription.EndDate.Date < DateTime.Today);

        ApplySessionBaseFilters(ref query, request.SearchText, request.CompanyId, request.Status, null, null);

        if (request.EntryFrom.HasValue)
            query = query.Where(x => x.EntryTime.HasValue && x.EntryTime.Value.Date >= request.EntryFrom.Value.Date);
        if (request.EntryTo.HasValue)
            query = query.Where(x => x.EntryTime.HasValue && x.EntryTime.Value.Date <= request.EntryTo.Value.Date);

        var sessions = await query.Take(5000).ToListAsync(cancellationToken);
        var pendingMap = await GetPendingAmountMapAsync(sessions.Select(x => x.CompanyId), cancellationToken);
        var rows = sessions.Select(x => ToLiveParkingListItem(x, pendingMap.GetValueOrDefault(x.CompanyId))).ToList();

        if (request.OverstayOnly == true)
            rows = rows.Where(x => x.OverstayDays > 0).ToList();

        var paymentStatus = (request.PaymentStatus ?? string.Empty).Trim();
        if (paymentStatus.Length > 0 && !paymentStatus.Equals("All", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(x => x.PaymentStatus.Equals(paymentStatus, StringComparison.OrdinalIgnoreCase)).ToList();

        if (tab.Equals("PaymentDue", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(x => !x.PaymentStatus.Equals(ParkingConstants.PaymentStatus.Paid, StringComparison.OrdinalIgnoreCase)).ToList();

        return rows;
    }

    private async Task<IReadOnlyList<VehicleBarcodeListItemDto>> BuildVehicleBarcodeListItemsAsync(VehicleBarcodeListQueryRequest request, CancellationToken cancellationToken)
    {
        await ReassignInsideSessionsToActiveSubscriptionsAsync(request.CompanyId, 0, cancellationToken);

        var query = db.ParkingSessions
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Subscription)
            .AsQueryable();

        var tab = (request.Tab ?? "All").Trim();
        if (tab.Equals("Generated", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == ParkingConstants.SessionStatus.BarcodeGenerated);
        else if (tab.Equals("Inside", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == ParkingConstants.SessionStatus.Inside);
        else if (tab.Equals("Exited", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == ParkingConstants.SessionStatus.Exited);
        else if (tab.Equals("Invalid", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.BarcodeStatus == ParkingConstants.BarcodeStatus.Invalid || x.Status == ParkingConstants.SessionStatus.Cancelled || x.Status == ParkingConstants.SessionStatus.Rejected);
        else if (tab.Equals("Overstay", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.OverstayDays > 0 || (x.Status == ParkingConstants.SessionStatus.Inside && x.Subscription != null && x.Subscription.EndDate.Date < DateTime.Today));

        ApplySessionBaseFilters(ref query, request.SearchText, request.CompanyId, request.Status, request.BarcodeStatus, request.VehicleType);

        if (request.EntryFrom.HasValue)
            query = query.Where(x => x.EntryTime.HasValue && x.EntryTime.Value.Date >= request.EntryFrom.Value.Date);
        if (request.EntryTo.HasValue)
            query = query.Where(x => x.EntryTime.HasValue && x.EntryTime.Value.Date <= request.EntryTo.Value.Date);
        if (request.ExitFrom.HasValue)
            query = query.Where(x => x.ExitTime.HasValue && x.ExitTime.Value.Date >= request.ExitFrom.Value.Date);
        if (request.ExitTo.HasValue)
            query = query.Where(x => x.ExitTime.HasValue && x.ExitTime.Value.Date <= request.ExitTo.Value.Date);
        if (request.CreatedFrom.HasValue)
            query = query.Where(x => x.CreatedDate.Date >= request.CreatedFrom.Value.Date);
        if (request.CreatedTo.HasValue)
            query = query.Where(x => x.CreatedDate.Date <= request.CreatedTo.Value.Date);

        var sessions = await query.Take(5000).ToListAsync(cancellationToken);
        var pendingMap = await GetPendingAmountMapAsync(sessions.Select(x => x.CompanyId), cancellationToken);
        return sessions.Select(x => ToVehicleBarcodeListItem(x, pendingMap.GetValueOrDefault(x.CompanyId))).ToList();
    }

    private static void ApplySessionBaseFilters(ref IQueryable<ParkingSession> query, string? searchText, int? companyId, string? status, string? barcodeStatus, string? vehicleType)
    {
        var search = (searchText ?? string.Empty).Trim();
        if (search.Length > 0)
        {
            query = query.Where(x => x.BarcodeNo.Contains(search) ||
                                     (x.PlateNo != null && x.PlateNo.Contains(search)) ||
                                     x.Company.CompanyName.Contains(search) ||
                                     x.Company.CompanyCode.Contains(search) ||
                                     (x.DriverName != null && x.DriverName.Contains(search)) ||
                                     (x.DriverMobile != null && x.DriverMobile.Contains(search)));
        }

        if (companyId.HasValue && companyId.Value > 0)
            query = query.Where(x => x.CompanyId == companyId.Value);

        var statusValue = (status ?? string.Empty).Trim();
        if (statusValue.Length > 0 && !statusValue.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == statusValue);

        var barcodeStatusValue = (barcodeStatus ?? string.Empty).Trim();
        if (barcodeStatusValue.Length > 0 && !barcodeStatusValue.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.BarcodeStatus == barcodeStatusValue);

        var vehicleTypeValue = (vehicleType ?? string.Empty).Trim();
        if (vehicleTypeValue.Length > 0 && !vehicleTypeValue.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.VehicleType == vehicleTypeValue);
    }

    private async Task<IReadOnlyList<PaymentListItemDto>> BuildPaymentListItemsAsync(PaymentListQueryRequest request, CancellationToken cancellationToken)
    {
        var query = db.ParkingPayments
            .AsNoTracking()
            .Include(x => x.Company)
            .AsQueryable();

        if (request.CompanyId.HasValue && request.CompanyId.Value > 0)
            query = query.Where(x => x.CompanyId == request.CompanyId.Value);
        if (request.PaymentId.HasValue && request.PaymentId.Value > 0)
            query = query.Where(x => x.PaymentId == request.PaymentId.Value);
        if (request.InvoiceId.HasValue && request.InvoiceId.Value > 0)
            query = query.Where(x => x.InvoiceId == request.InvoiceId.Value);
        if (request.SessionId.HasValue && request.SessionId.Value > 0)
            query = query.Where(x => x.SessionId == request.SessionId.Value);

        var mode = (request.PaymentMode ?? string.Empty).Trim();
        if (mode.Length > 0 && !mode.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.PaymentMode == mode);

        var type = (request.PaymentType ?? string.Empty).Trim();
        if (type.Length > 0 && !type.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.PaymentType == type);

        if (request.DateFrom.HasValue)
            query = query.Where(x => x.PaymentDate.Date >= request.DateFrom.Value.Date);
        if (request.DateTo.HasValue)
            query = query.Where(x => x.PaymentDate.Date <= request.DateTo.Value.Date);

        var search = (request.SearchText ?? string.Empty).Trim();
        if (search.Length > 0)
        {
            query = query.Where(x => x.ReceiptNo.Contains(search) ||
                                     x.Company.CompanyName.Contains(search) ||
                                     x.Company.CompanyCode.Contains(search) ||
                                     (x.ReferenceNo != null && x.ReferenceNo.Contains(search)) ||
                                     (x.Remarks != null && x.Remarks.Contains(search)));
        }

        var tab = (request.Tab ?? "All").Trim();
        if (tab.Equals("Today", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.PaymentDate.Date == DateTime.Today);
        else if (tab.Equals("Invoice", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.InvoiceId != null && x.SessionId == null);
        else if (tab.Equals("Gate", StringComparison.OrdinalIgnoreCase) || tab.Equals("GateCollection", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.SessionId != null || x.PaymentType == "GateCollection");

        var payments = await query.Take(5000).ToListAsync(cancellationToken);
        var invoiceIds = payments.Where(x => x.InvoiceId.HasValue).Select(x => x.InvoiceId!.Value).Distinct().ToList();
        var sessionIds = payments.Where(x => x.SessionId.HasValue).Select(x => x.SessionId!.Value).Distinct().ToList();

        var invoices = invoiceIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.ParkingInvoices.AsNoTracking().Where(x => invoiceIds.Contains(x.InvoiceId)).ToDictionaryAsync(x => x.InvoiceId, x => x.InvoiceNo, cancellationToken);
        var sessions = sessionIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.ParkingSessions.AsNoTracking().Where(x => sessionIds.Contains(x.SessionId)).ToDictionaryAsync(x => x.SessionId, x => x.BarcodeNo, cancellationToken);

        return payments.Select(x => new PaymentListItemDto(
            x.PaymentId,
            x.ReceiptNo,
            x.CompanyId,
            x.Company.CompanyCode,
            x.Company.CompanyName,
            x.InvoiceId,
            x.InvoiceId.HasValue && invoices.TryGetValue(x.InvoiceId.Value, out var invoiceNo) ? invoiceNo : null,
            x.SessionId,
            x.SessionId.HasValue && sessions.TryGetValue(x.SessionId.Value, out var barcodeNo) ? barcodeNo : null,
            x.PaymentType,
            x.Amount,
            x.PaymentMode,
            x.ReferenceNo,
            x.PaymentDate,
            x.Remarks,
            x.ReceivedBy)).ToList();
    }

    private async Task<decimal> GetSubscriptionPendingAmountAsync(int companyId, CancellationToken cancellationToken)
    {
        var map = await GetPendingAmountMapAsync(new[] { companyId }, cancellationToken);
        return map.GetValueOrDefault(companyId);
    }

    private async Task<Dictionary<int, decimal>> GetPendingAmountMapAsync(IEnumerable<int> companyIds, CancellationToken cancellationToken)
    {
        var ids = companyIds.Distinct().Where(x => x > 0).ToList();
        if (ids.Count == 0)
            return new Dictionary<int, decimal>();

        var subscriptions = await db.ParkingSubscriptions
            .AsNoTracking()
            .Where(x => ids.Contains(x.CompanyId) && x.Status != ParkingConstants.SubscriptionStatus.Cancelled)
            .Select(x => new
            {
                x.SubscriptionId,
                x.CompanyId,
                x.BalanceAmount
            })
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
            return new Dictionary<int, decimal>();

        var subscriptionIds = subscriptions.Select(x => x.SubscriptionId).ToList();
        var invoices = await db.ParkingInvoices
            .AsNoTracking()
            .Where(x => x.SubscriptionId.HasValue && subscriptionIds.Contains(x.SubscriptionId.Value) && x.Status != "Cancelled")
            .Select(x => new
            {
                SubscriptionId = x.SubscriptionId.GetValueOrDefault(),
                x.InvoiceDate,
                x.InvoiceId,
                x.BalanceAmount
            })
            .ToListAsync(cancellationToken);

        var latestInvoiceBySubscription = invoices
            .GroupBy(x => x.SubscriptionId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.InvoiceDate).ThenByDescending(y => y.InvoiceId).First());

        var result = new Dictionary<int, decimal>();
        foreach (var subscription in subscriptions)
        {
            latestInvoiceBySubscription.TryGetValue(subscription.SubscriptionId, out var invoice);
            var pending = invoice?.BalanceAmount ?? subscription.BalanceAmount;
            if (pending <= 0)
                continue;

            result[subscription.CompanyId] = result.GetValueOrDefault(subscription.CompanyId) + pending;
        }

        return result;
    }

    private static readonly HashSet<string> PaymentModeKpiKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "cash",
        "card",
        "banktransfer",
        "cheque"
    };

    private static string NormalizePaymentModeKey(string? paymentMode)
    {
        return (paymentMode ?? string.Empty)
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    private static int CountLiveParkingCompaniesWithPending(IEnumerable<LiveParkingListItemDto> rows)
    {
        return rows
            .Where(x => !x.PaymentStatus.Equals(ParkingConstants.PaymentStatus.Paid, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.CompanyId)
            .Distinct()
            .Count();
    }

    private static LiveParkingListItemDto ToLiveParkingListItem(ParkingSession s, decimal pendingAmount)
    {
        var now = DateTime.Now;
        var entry = s.EntryTime ?? s.CreatedDate;
        var validUntil = s.Subscription?.EndDate;
        var overstayDays = s.Status == ParkingConstants.SessionStatus.Inside && validUntil.HasValue && validUntil.Value.Date < now.Date
            ? (now.Date - validUntil.Value.Date).Days
            : Math.Max(s.OverstayDays, 0);
        var overstayAmount = s.OverstayAmount;
        var paymentStatus = pendingAmount <= 0 ? ParkingConstants.PaymentStatus.Paid : ParkingConstants.PaymentStatus.Unpaid;

        return new LiveParkingListItemDto(
            s.SessionId,
            s.BarcodeNo,
            s.CompanyId,
            s.Company.CompanyCode,
            s.Company.CompanyName,
            s.PlateNo,
            s.VehicleType,
            s.DriverName,
            s.DriverMobile,
            s.EntryTime,
            FormatDuration(now - entry),
            validUntil,
            overstayDays,
            overstayAmount,
            paymentStatus,
            s.Status,
            s.BarcodeStatus,
            s.EntryOperatorId,
            s.Remarks);
    }

    private static VehicleBarcodeListItemDto ToVehicleBarcodeListItem(ParkingSession s, decimal pendingAmount)
    {
        var now = DateTime.Now;
        var durationEnd = s.ExitTime ?? now;
        var durationStart = s.EntryTime ?? s.CreatedDate;
        var validUntil = s.Subscription?.EndDate;
        var overstayDays = s.Status == ParkingConstants.SessionStatus.Inside && validUntil.HasValue && validUntil.Value.Date < now.Date
            ? (now.Date - validUntil.Value.Date).Days
            : Math.Max(s.OverstayDays, 0);
        var paymentStatus = pendingAmount <= 0 ? ParkingConstants.PaymentStatus.Paid : ParkingConstants.PaymentStatus.Unpaid;

        return new VehicleBarcodeListItemDto(
            s.SessionId,
            s.BarcodeNo,
            s.CompanyId,
            s.Company.CompanyCode,
            s.Company.CompanyName,
            s.PlateNo,
            s.VehicleType,
            s.DriverName,
            s.DriverMobile,
            s.CreatedDate,
            s.EntryTime,
            s.ExitTime,
            FormatDuration(durationEnd - durationStart),
            validUntil,
            s.Status,
            s.BarcodeStatus,
            overstayDays,
            s.OverstayAmount,
            paymentStatus,
            s.ForceExit,
            s.ForceExitReason,
            s.CreatedBy,
            s.EntryOperatorId,
            s.ExitOperatorId,
            s.Remarks);
    }

    private static IEnumerable<LiveParkingListItemDto> SortLiveParking(IEnumerable<LiveParkingListItemDto> rows, string? sortBy, string? sortDirection)
    {
        var desc = (sortDirection ?? "Desc").Equals("Desc", StringComparison.OrdinalIgnoreCase);
        var key = (sortBy ?? "EntryTime").Trim();
        return key switch
        {
            "BarcodeNo" => desc ? rows.OrderByDescending(x => x.BarcodeNo) : rows.OrderBy(x => x.BarcodeNo),
            "CompanyName" => desc ? rows.OrderByDescending(x => x.CompanyName) : rows.OrderBy(x => x.CompanyName),
            "PlateNo" => desc ? rows.OrderByDescending(x => x.PlateNo) : rows.OrderBy(x => x.PlateNo),
            "OverstayDays" => desc ? rows.OrderByDescending(x => x.OverstayDays) : rows.OrderBy(x => x.OverstayDays),
            _ => desc ? rows.OrderByDescending(x => x.EntryTime).ThenByDescending(x => x.SessionId) : rows.OrderBy(x => x.EntryTime).ThenBy(x => x.SessionId)
        };
    }

    private static IEnumerable<PaymentListItemDto> SortPayments(IEnumerable<PaymentListItemDto> rows, string? sortBy, string? sortDirection)
    {
        var desc = (sortDirection ?? "Desc").Equals("Desc", StringComparison.OrdinalIgnoreCase);
        var key = (sortBy ?? "PaymentDate").Trim();
        return key switch
        {
            "ReceiptNo" => desc ? rows.OrderByDescending(x => x.ReceiptNo) : rows.OrderBy(x => x.ReceiptNo),
            "CompanyName" => desc ? rows.OrderByDescending(x => x.CompanyName) : rows.OrderBy(x => x.CompanyName),
            "InvoiceNo" => desc ? rows.OrderByDescending(x => x.InvoiceNo) : rows.OrderBy(x => x.InvoiceNo),
            "Amount" => desc ? rows.OrderByDescending(x => x.Amount) : rows.OrderBy(x => x.Amount),
            "PaymentMode" => desc ? rows.OrderByDescending(x => x.PaymentMode) : rows.OrderBy(x => x.PaymentMode),
            _ => desc ? rows.OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.PaymentId) : rows.OrderBy(x => x.PaymentDate).ThenBy(x => x.PaymentId)
        };
    }

    private static IEnumerable<VehicleBarcodeListItemDto> SortVehicleBarcodes(IEnumerable<VehicleBarcodeListItemDto> rows, string? sortBy, string? sortDirection)
    {
        var desc = (sortDirection ?? "Desc").Equals("Desc", StringComparison.OrdinalIgnoreCase);
        var key = (sortBy ?? "CreatedDate").Trim();
        return key switch
        {
            "BarcodeNo" => desc ? rows.OrderByDescending(x => x.BarcodeNo) : rows.OrderBy(x => x.BarcodeNo),
            "CompanyName" => desc ? rows.OrderByDescending(x => x.CompanyName) : rows.OrderBy(x => x.CompanyName),
            "PlateNo" => desc ? rows.OrderByDescending(x => x.PlateNo) : rows.OrderBy(x => x.PlateNo),
            "EntryTime" => desc ? rows.OrderByDescending(x => x.EntryTime) : rows.OrderBy(x => x.EntryTime),
            "ExitTime" => desc ? rows.OrderByDescending(x => x.ExitTime) : rows.OrderBy(x => x.ExitTime),
            "Status" => desc ? rows.OrderByDescending(x => x.Status) : rows.OrderBy(x => x.Status),
            _ => desc ? rows.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.SessionId) : rows.OrderBy(x => x.CreatedDate).ThenBy(x => x.SessionId)
        };
    }

    private static string AppendRemarks(string? current, string addition) =>
        string.IsNullOrWhiteSpace(current) ? addition : current + Environment.NewLine + addition;

    private static IReadOnlyList<ExtraSlotRateDto> BuildExtraSlotRatePlans(Dictionary<string, string> settings) =>
    [
        new ExtraSlotRateDto("Daily", GetExtraSlotRate(settings, "Daily"), 1, $"Daily - AED {GetExtraSlotRate(settings, "Daily"):n2} per slot"),
        new ExtraSlotRateDto("Weekly", GetExtraSlotRate(settings, "Weekly"), 7, $"Weekly - AED {GetExtraSlotRate(settings, "Weekly"):n2} per slot"),
        new ExtraSlotRateDto("Monthly", GetExtraSlotRate(settings, "Monthly"), 30, $"Monthly - AED {GetExtraSlotRate(settings, "Monthly"):n2} per slot")
    ];

    private static string NormalizeExtraSlotPlanType(string? planType)
    {
        var value = (planType ?? string.Empty).Trim();
        if (value.Equals("Weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
        if (value.Equals("Monthly", StringComparison.OrdinalIgnoreCase)) return "Monthly";
        return "Daily";
    }

    private static decimal GetExtraSlotRate(Dictionary<string, string> settings, string planType) =>
        NormalizeExtraSlotPlanType(planType) switch
        {
            "Weekly" => GetDecimalSetting(settings, "WeeklyRatePerSlot", 150m),
            "Monthly" => GetDecimalSetting(settings, "MonthlyRatePerSlot", 500m),
            _ => GetDecimalSetting(settings, "DailyRatePerSlot", 50m)
        };

    private static DateTime CalculateExtraSlotEndDate(string planType, DateTime startDate) =>
        NormalizeExtraSlotPlanType(planType) switch
        {
            "Weekly" => startDate.AddDays(7),
            "Monthly" => startDate.AddMonths(1),
            _ => startDate.AddDays(1)
        };

    private static string GetInvoiceStatus(decimal balance, decimal paid) =>
        balance <= 0 ? ParkingConstants.PaymentStatus.Paid : paid > 0 ? ParkingConstants.PaymentStatus.Partial : ParkingConstants.PaymentStatus.Unpaid;

    private static decimal ValidatePositivePaymentAmount(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Payment amount must be greater than zero.");
        return amount;
    }

    private static decimal ValidateInitialPaidAmount(decimal paidAmount, decimal total)
    {
        if (paidAmount < 0)
            throw new InvalidOperationException("Paid amount cannot be negative.");
        if (paidAmount > total)
            throw new InvalidOperationException("Paid amount cannot be greater than invoice total.");
        return paidAmount;
    }

    private static string NormalizePaymentMode(string? paymentMode)
    {
        var cleaned = (paymentMode ?? string.Empty).Trim();
        return cleaned.Length == 0 ? "Cash" : cleaned;
    }

    private static bool HasMoneyDifference(decimal value) => Math.Abs(value) >= 0.01m;

    private static string BuildPaymentAllocationSummary(IReadOnlyList<PaymentAllocation> allocations) =>
        string.Join("; ", allocations.Select(x => $"{x.InvoiceNo}: AED {x.Amount:n2}, balance AED {x.BalanceBefore:n2} -> {x.BalanceAfter:n2}, status {x.StatusBefore} -> {x.StatusAfter}"));


    private static void ValidateCompanyContact(string? mobile, string? email)
    {
        var cleanMobile = (mobile ?? string.Empty).Trim();
        if (cleanMobile.Length > 0)
        {
            var allowed = cleanMobile.All(c => char.IsDigit(c) || c == '+' || c == '-' || c == ' ' || c == '(' || c == ')');
            var digits = new string(cleanMobile.Where(char.IsDigit).ToArray());
            if (!allowed || digits.Length < 7 || digits.Length > 15)
                throw new InvalidOperationException("Mobile number format is invalid. Use 7 to 15 digits, optionally with +, space, hyphen or brackets.");
        }

        var cleanEmail = (email ?? string.Empty).Trim();
        if (cleanEmail.Length > 0)
        {
            try
            {
                var address = new MailAddress(cleanEmail);
                if (!address.Address.Equals(cleanEmail, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException();
            }
            catch
            {
                throw new InvalidOperationException("Email format is invalid.");
            }
        }
    }

    private static string? TrimOrNull(string? value)
    {
        var cleaned = (value ?? string.Empty).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string NormalizeCompanyStatus(string? status)
    {
        var value = (status ?? string.Empty).Trim();
        if (value.Equals(ParkingConstants.CompanyStatus.Blocked, StringComparison.OrdinalIgnoreCase)) return ParkingConstants.CompanyStatus.Blocked;
        if (value.Equals(ParkingConstants.CompanyStatus.Inactive, StringComparison.OrdinalIgnoreCase)) return ParkingConstants.CompanyStatus.Inactive;
        return ParkingConstants.CompanyStatus.Active;
    }

    private static string GetStringSetting(Dictionary<string, string> settings, string key, string defaultValue) =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : defaultValue;

    private static string NormalizeCounterPrefix(string? value, string fallback)
    {
        var clean = new string((value ?? string.Empty).Trim().ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (clean.Length == 0) clean = fallback;
        return clean.Length <= 12 ? clean : clean[..12];
    }

    private static int GetIntSetting(Dictionary<string, string> settings, string key, int defaultValue) =>
        settings.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : defaultValue;

    private static decimal GetDecimalSetting(Dictionary<string, string> settings, string key, decimal defaultValue) =>
        settings.TryGetValue(key, out var value) && decimal.TryParse(value, out var result) ? result : defaultValue;

    private static bool GetBoolSetting(Dictionary<string, string> settings, string key, bool defaultValue) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : defaultValue;
}
