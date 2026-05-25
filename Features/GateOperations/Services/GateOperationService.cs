using System.Data;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Domain.Constants;
using NetworldParkingLot.Api.Domain.Entities;
using NetworldParkingLot.Api.Features.GateOperations.Dtos;
using NetworldParkingLot.Api.Features.GateOperations.Repositories;

namespace NetworldParkingLot.Api.Features.GateOperations.Services;

public sealed class GateOperationService(NetworldParkingDbContext db, IGateRepository repository) : IGateOperationService
{
    public async Task<GateSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var totalCapacity = GetIntSetting(settings, "TotalParkingCapacity", 500);
        var now = DateTime.Now;
        var today = now.Date;

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

        var paid = Math.Min(Math.Max(request.PaidAmount, 0), total);
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
                PaymentMode = request.PaymentMode,
                ReferenceNo = request.ReferenceNo,
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

    public async Task<CompanyGateStatusDto> CheckCompanyAsync(CheckCompanyRequest request, CancellationToken cancellationToken = default)
    {
        var search = request.SearchText.Trim();
        var company = await db.ParkingCompanies
            .FirstOrDefaultAsync(x => x.CompanyCode == search ||
                                      x.CompanyName.Contains(search) ||
                                      (x.Mobile != null && x.Mobile.Contains(search)), cancellationToken);

        if (company == null)
            throw new InvalidOperationException("Company not found.");

        var status = await BuildCompanyStatusAsync(company, request.OperatorId, cancellationToken);
        await AddActivityAsync(ParkingConstants.GateActionType.EntryCheck, company.CompanyId, null, null, null, status.EntryStatus, status.Message, request.OperatorId, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return status;
    }

    public async Task<GenerateBarcodeResponseDto> GenerateBarcodeAsync(GenerateBarcodeRequest request, CancellationToken cancellationToken = default)
    {
        var company = await repository.GetCompanyAsync(request.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("Company not found.");

        var status = await BuildCompanyStatusAsync(company, request.OperatorId, cancellationToken);
        if (!status.CanGenerateBarcode && !(status.EntryStatus == "PaymentDue" && request.AllowPaymentDueWarning))
            throw new InvalidOperationException(status.Message);

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

        var status = await BuildCompanyStatusAsync(session.Company, request.OperatorId, cancellationToken);
        if (!status.CanGenerateBarcode && !(status.EntryStatus == "PaymentDue" && request.AllowPaymentDueWarning))
            throw new InvalidOperationException(status.Message);

        session.EntryTime = DateTime.Now;
        session.Status = ParkingConstants.SessionStatus.Inside;
        session.BarcodeStatus = ParkingConstants.BarcodeStatus.Active;
        session.EntryOperatorId = request.OperatorId;

        await AddActivityAsync(ParkingConstants.GateActionType.EntryAllowed, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, "Entry Allowed", "Vehicle entry allowed successfully.", request.OperatorId, cancellationToken);
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

        session.Status = ParkingConstants.SessionStatus.Rejected;
        session.BarcodeStatus = ParkingConstants.BarcodeStatus.Invalid;
        session.Remarks = AppendRemarks(session.Remarks, "Rejected: " + request.Reason);

        await AddActivityAsync(ParkingConstants.GateActionType.EntryRejected, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, "Entry Rejected", request.Reason ?? "Entry rejected.", request.OperatorId, cancellationToken);
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

        session.ExitTime = DateTime.Now;
        session.ExitOperatorId = request.OperatorId;
        session.Status = ParkingConstants.SessionStatus.Exited;
        session.BarcodeStatus = ParkingConstants.BarcodeStatus.Used;
        session.ForceExit = scan.TotalPayable > 0 && request.ForceAllow;
        session.ForceExitReason = request.ForceReason;
        session.OverstayDays = scan.OverstayDays;
        session.OverstayAmount = scan.OverstayAmount;

        var status = session.ForceExit ? "Exit Allowed With Warning" : "Clear To Exit";
        await AddActivityAsync(ParkingConstants.GateActionType.ExitAllowed, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, status, status, request.OperatorId, cancellationToken);
        await AddOutsideDisplayAsync(session, ParkingConstants.OutsideDisplayStatus.ClearToExit, "CLEAR TO EXIT", "Thank you. Please proceed.", 0, 0, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new ExitResultDto(session.SessionId, session.BarcodeNo, session.PlateNo, session.Company.CompanyName, session.ExitTime.Value, session.BarcodeStatus, session.Status, "Exit completed. Barcode is now used and cannot be reused.");
    }

    public async Task<PaymentResultDto> CollectPaymentAsync(CollectPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var company = await repository.GetCompanyAsync(request.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("Company not found.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        if (request.SessionId.HasValue)
            await EnsureOverstayInvoiceIfNeededAsync(request.SessionId.Value, request.OperatorId, cancellationToken);

        decimal currentPending;
        string? paidInvoiceNo = null;
        decimal? invoiceBalanceAfterPayment = null;

        if (request.InvoiceId.HasValue)
        {
            var invoice = await db.ParkingInvoices
                .FirstOrDefaultAsync(x => x.InvoiceId == request.InvoiceId.Value && x.CompanyId == company.CompanyId, cancellationToken)
                ?? throw new InvalidOperationException("Invoice not found.");

            currentPending = invoice.BalanceAmount;

            if (currentPending <= 0)
                throw new InvalidOperationException("No pending amount found for this invoice.");

            if (request.Amount > currentPending)
                throw new InvalidOperationException($"Payment amount cannot be more than current invoice balance AED {currentPending:n2}.");

            ApplyAmountToInvoice(invoice, request.Amount);
            paidInvoiceNo = invoice.InvoiceNo;
            invoiceBalanceAfterPayment = invoice.BalanceAmount;
        }
        else
        {
            currentPending = await repository.GetPendingAmountAsync(company.CompanyId, cancellationToken);

            if (currentPending <= 0)
                throw new InvalidOperationException("No pending amount found for this company.");

            if (request.Amount > currentPending)
                throw new InvalidOperationException($"Payment amount cannot be more than current pending amount AED {currentPending:n2}.");

            await ApplyPaymentToOldestInvoicesAsync(company.CompanyId, request.Amount, cancellationToken);
        }

        var receiptNo = await GenerateNextReceiptNoAsync(cancellationToken);

        var payment = new ParkingPayment
        {
            ReceiptNo = receiptNo,
            CompanyId = company.CompanyId,
            InvoiceId = request.InvoiceId,
            SessionId = request.SessionId,
            PaymentType = request.SessionId.HasValue ? "GateCollection" : "Invoice",
            Amount = request.Amount,
            PaymentMode = request.PaymentMode,
            ReferenceNo = request.ReferenceNo,
            ReceivedBy = request.OperatorId,
            PaymentDate = DateTime.Now,
            Remarks = request.Remarks
        };

        await db.ParkingPayments.AddAsync(payment, cancellationToken);
        await AddActivityAsync(ParkingConstants.GateActionType.PaymentCollected, company.CompanyId, request.SessionId, null, null, "Payment Collected", $"Received AED {request.Amount:n2}", request.OperatorId, cancellationToken);
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
                        cancellationToken);

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
            payment.PaymentId,
            payment.ReceiptNo,
            company.CompanyId,
            payment.Amount,
            payment.PaymentMode,
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

        var paid = Math.Min(Math.Max(request.PaidAmount, 0), total);
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
                PaymentMode = request.PaymentMode,
                ReferenceNo = request.ReferenceNo,
                ReceivedBy = request.OperatorId,
                PaymentDate = DateTime.Now,
                Remarks = "Payment collected with extra slot invoice."
            }, cancellationToken);
        }

        await AddActivityAsync(ParkingConstants.GateActionType.ExtraSlotInvoice, company.CompanyId, null, null, null, "Extra Slot Invoice", $"Added {request.AdditionalSlots} {planType} extra slots.", request.OperatorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ExtraSlotInvoiceResultDto(subscription.SubscriptionId, invoice.InvoiceId, invoice.InvoiceNo, company.CompanyId, request.AdditionalSlots, planType, total, balance, invoice.Status, "Extra slots added and invoice generated.");
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
        var result = new List<LiveParkingDto>();
        foreach (var s in sessions)
        {
            var pending = await repository.GetPendingAmountAsync(s.CompanyId, cancellationToken);
            result.Add(new LiveParkingDto(
                s.SessionId,
                s.BarcodeNo,
                s.PlateNo,
                s.Company.CompanyName,
                s.EntryTime ?? s.CreatedDate,
                FormatDuration(DateTime.Now - (s.EntryTime ?? s.CreatedDate)),
                s.Subscription?.EndDate,
                s.Status,
                pending));
        }
        return result;
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
                await AddActivityAsync(ParkingConstants.GateActionType.ExitScanned, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, ParkingConstants.ExitStatus.AlreadyExited, "Barcode already used. Vehicle has already exited.", operatorId, cancellationToken);
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
                await AddActivityAsync(ParkingConstants.GateActionType.ExitScanned, session.CompanyId, session.SessionId, session.BarcodeNo, session.PlateNo, ParkingConstants.ExitStatus.InvalidBarcode, "Barcode is not active for exit.", operatorId, cancellationToken);
                await repository.SaveChangesAsync(cancellationToken);
            }
            return invalid;
        }

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

    private async Task<decimal> ApplyPaymentToOldestInvoicesAsync(int companyId, decimal amount, CancellationToken cancellationToken)
    {
        var invoices = await db.ParkingInvoices
            .Where(x => x.CompanyId == companyId && x.BalanceAmount > 0)
            .OrderBy(x => x.InvoiceDate)
            .ToListAsync(cancellationToken);

        foreach (var invoice in invoices)
        {
            if (amount <= 0) break;
            var apply = Math.Min(amount, invoice.BalanceAmount);
            ApplyAmountToInvoice(invoice, apply);
            amount -= apply;
        }
        return amount;
    }

    private static void ApplyAmountToInvoice(ParkingInvoice invoice, decimal amount)
    {
        if (amount <= 0) return;
        var apply = Math.Min(amount, invoice.BalanceAmount);
        invoice.PaidAmount += apply;
        invoice.BalanceAmount -= apply;
        invoice.Status = GetInvoiceStatus(invoice.BalanceAmount, invoice.PaidAmount);
    }

    private async Task<string> GenerateNextBarcodeNoAsync(CancellationToken cancellationToken)
    {
        var key = "BARCODE_" + DateTime.Now.ToString("yyyyMMdd");
        var counter = await db.SystemCounters.FirstOrDefaultAsync(x => x.CounterName == key, cancellationToken);
        if (counter == null)
        {
            counter = new SystemCounter { CounterName = key, LastNumber = 0, UpdatedDate = DateTime.Now };
            await db.SystemCounters.AddAsync(counter, cancellationToken);
        }
        counter.LastNumber += 1;
        counter.UpdatedDate = DateTime.Now;
        await db.SaveChangesAsync(cancellationToken);
        return "KP" + DateTime.Now.ToString("yyMMdd") + counter.LastNumber.ToString("D6");
    }

    private async Task<string> GenerateNextCompanyCodeAsync(CancellationToken cancellationToken) => await GenerateFromCounterAsync("CMP", "CMP", cancellationToken);
    private async Task<string> GenerateNextInvoiceNoAsync(CancellationToken cancellationToken) => await GenerateFromCounterAsync("INV", "INV", cancellationToken);
    private async Task<string> GenerateNextReceiptNoAsync(CancellationToken cancellationToken) => await GenerateFromCounterAsync("RCPT", "RCT", cancellationToken);

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

    private async Task AddActivityAsync(string actionType, int? companyId, int? sessionId, string? barcodeNo, string? plateNo, string status, string? message, int operatorId, CancellationToken cancellationToken)
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
    }

    private async Task AddOutsideDisplayAsync(ParkingSession session, string displayStatus, string mainMessage, string? subMessage, decimal amountDue, int overstayDays, CancellationToken cancellationToken)
    {
        await repository.AddOutsideDisplayEventAsync(new OutsideDisplayEvent
        {
            SessionId = session.SessionId,
            BarcodeNo = session.BarcodeNo,
            PlateNo = session.PlateNo,
            CompanyName = session.Company.CompanyName,
            DisplayStatus = displayStatus,
            MainMessage = mainMessage,
            SubMessage = subMessage,
            AmountDue = amountDue,
            OverstayDays = overstayDays,
            CreatedDate = DateTime.Now
        }, cancellationToken);
    }

    private async Task AddInvalidDisplayAsync(string barcodeNo, string mainMessage, string subMessage, int operatorId, CancellationToken cancellationToken)
    {
        await repository.AddOutsideDisplayEventAsync(new OutsideDisplayEvent
        {
            BarcodeNo = barcodeNo,
            DisplayStatus = ParkingConstants.OutsideDisplayStatus.InvalidBarcode,
            MainMessage = mainMessage,
            SubMessage = subMessage,
            CreatedDate = DateTime.Now
        }, cancellationToken);
        await AddActivityAsync(ParkingConstants.GateActionType.ExitScanned, null, null, barcodeNo, null, ParkingConstants.ExitStatus.InvalidBarcode, subMessage, operatorId, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays} days {span.Hours} hours";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours} hours {span.Minutes} minutes";
        return $"{span.Minutes} minutes";
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

    private static int GetIntSetting(Dictionary<string, string> settings, string key, int defaultValue) =>
        settings.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : defaultValue;

    private static decimal GetDecimalSetting(Dictionary<string, string> settings, string key, decimal defaultValue) =>
        settings.TryGetValue(key, out var value) && decimal.TryParse(value, out var result) ? result : defaultValue;

    private static bool GetBoolSetting(Dictionary<string, string> settings, string key, bool defaultValue) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : defaultValue;
}
