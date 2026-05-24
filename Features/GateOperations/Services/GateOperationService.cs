using System.Data;
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
        var finalMessage = "Payment saved successfully.";

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


    public async Task<ExtraSlotInvoiceResultDto> CreateExtraSlotInvoiceAsync(CreateExtraSlotInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var company = await repository.GetCompanyAsync(request.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("Company not found.");

        if (request.EndDate.Date < request.StartDate.Date)
            throw new InvalidOperationException("End date cannot be before start date.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var subTotal = request.AdditionalSlots * request.RatePerSlot;
        var total = subTotal - request.DiscountAmount + request.VatAmount;
        var paid = Math.Min(request.PaidAmount, total);
        var balance = total - paid;
        var invoiceNo = await GenerateNextInvoiceNoAsync(cancellationToken);

        var subscription = new ParkingSubscription
        {
            CompanyId = company.CompanyId,
            PlanType = request.PlanType,
            SlotsPurchased = request.AdditionalSlots,
            RatePerSlot = request.RatePerSlot,
            StartDate = request.StartDate.Date,
            EndDate = request.EndDate.Date,
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
            DueDate = request.EndDate.Date,
            PlanType = request.PlanType,
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

        await AddActivityAsync(ParkingConstants.GateActionType.ExtraSlotInvoice, company.CompanyId, null, null, null, "Extra Slot Invoice", $"Added {request.AdditionalSlots} extra slots.", request.OperatorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ExtraSlotInvoiceResultDto(subscription.SubscriptionId, invoice.InvoiceId, invoice.InvoiceNo, company.CompanyId, request.AdditionalSlots, request.PlanType, total, balance, invoice.Status, "Extra slots added and invoice generated.");
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
        var pending = await repository.GetPendingAmountAsync(session.CompanyId, cancellationToken);
        var totalPayable = pending + overstayAmount;
        var paymentStatus = totalPayable <= 0 ? ParkingConstants.PaymentStatus.Paid : ParkingConstants.PaymentStatus.Unpaid;
        var finalStatus = totalPayable <= 0 ? ParkingConstants.ExitStatus.ClearToExit : ParkingConstants.ExitStatus.PaymentRequired;
        var message = totalPayable <= 0 ? "Clear to exit. No pending amount." : $"Payment required AED {totalPayable:n2}.";

        session.OverstayDays = overstayDays;
        session.OverstayAmount = overstayAmount;

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

        var exists = await db.ParkingInvoices.AnyAsync(x => x.SessionId == sessionId && x.InvoiceType == "Overstay", cancellationToken);
        if (exists) return;

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
            Remarks = $"Overstay charge for barcode {session.BarcodeNo}",
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

    private static string GetInvoiceStatus(decimal balance, decimal paid) =>
        balance <= 0 ? ParkingConstants.PaymentStatus.Paid : paid > 0 ? ParkingConstants.PaymentStatus.Partial : ParkingConstants.PaymentStatus.Unpaid;

    private static int GetIntSetting(Dictionary<string, string> settings, string key, int defaultValue) =>
        settings.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : defaultValue;

    private static decimal GetDecimalSetting(Dictionary<string, string> settings, string key, decimal defaultValue) =>
        settings.TryGetValue(key, out var value) && decimal.TryParse(value, out var result) ? result : defaultValue;

    private static bool GetBoolSetting(Dictionary<string, string> settings, string key, bool defaultValue) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : defaultValue;
}
