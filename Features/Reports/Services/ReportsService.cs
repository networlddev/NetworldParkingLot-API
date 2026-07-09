using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NetworldParkingLot.Api.Data;
using NetworldParkingLot.Api.Domain.Constants;
using NetworldParkingLot.Api.Features.Reports.Dtos;

namespace NetworldParkingLot.Api.Features.Reports.Services;

public sealed class ReportsService(NetworldParkingDbContext db) : IReportsService
{
    private static readonly List<ReportCatalogItemDto> Catalog =
    [
        new() { ReportKey = "company-list", ReportName = "Company List", Category = "Companies", Description = "Registered companies with contact and status.", SupportsDateRange = false, SupportsStatus = true, SupportsCompany = false },
        new() { ReportKey = "company-pending", ReportName = "Pending Amount by Company", Category = "Companies", Description = "Company-wise unpaid invoice balances.", SupportsDateRange = false, SupportsStatus = false, SupportsCompany = true },
        new() { ReportKey = "subscriptions-active", ReportName = "Active Subscriptions", Category = "Subscriptions", Description = "Current active subscriptions and slot allocation.", SupportsStatus = false, SupportsCompany = true },
        new() { ReportKey = "subscriptions-expired", ReportName = "Expired Subscriptions", Category = "Subscriptions", Description = "Expired/cancelled subscriptions for renewal review.", SupportsStatus = true, SupportsCompany = true },
        new() { ReportKey = "vehicles-inside", ReportName = "Vehicles Inside", Category = "Gate", Description = "Vehicles currently inside the parking area.", SupportsDateRange = false, SupportsStatus = false, SupportsCompany = true },
        new() { ReportKey = "vehicle-movement", ReportName = "Vehicle Movement", Category = "Gate", Description = "Entry/exit movement within a date range.", SupportsStatus = true, SupportsCompany = true },
        new() { ReportKey = "overstay", ReportName = "Overstay Vehicles", Category = "Gate", Description = "Inside/exited vehicles with overstay days or amount.", SupportsStatus = true, SupportsCompany = true },
        new() { ReportKey = "invoice-summary", ReportName = "Invoice Summary", Category = "Finance", Description = "Invoices with paid and pending values.", SupportsStatus = true, SupportsCompany = true },
        new() { ReportKey = "payments-collection", ReportName = "Payment Collection", Category = "Finance", Description = "Payment rows by date, mode and receipt.", SupportsStatus = false, SupportsCompany = true },
        new() { ReportKey = "payment-daily", ReportName = "Daily Collection Summary", Category = "Finance", Description = "Daily collection totals by payment mode.", SupportsStatus = false, SupportsCompany = false },
        new() { ReportKey = "invoice-payment-reconciliation", ReportName = "Invoice Payment Reconciliation", Category = "Finance", Description = "Invoices whose stored paid/pending values differ from linked payments.", SupportsStatus = false, SupportsCompany = true },
        new() { ReportKey = "user-list", ReportName = "User List", Category = "Access", Description = "System users and role assignments.", SupportsDateRange = false, SupportsStatus = true, SupportsCompany = false },
        new() { ReportKey = "user-permissions", ReportName = "User Permission Overrides", Category = "Access", Description = "Explicit user allow/deny permission overrides.", SupportsDateRange = false, SupportsStatus = false, SupportsCompany = false },
        new() { ReportKey = "settings-change", ReportName = "System Activity History", Category = "History", Description = "Full audited system activity history across modules.", SupportsDateRange = true, SupportsStatus = true, SupportsCompany = false }
    ];

    public IReadOnlyList<ReportCatalogItemDto> GetCatalog() => Catalog;

    public Task<ReportResultDto> RunAsync(string reportKey, ReportQueryDto query, CancellationToken cancellationToken = default)
    {
        var key = (reportKey ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "company-list" => CompanyListAsync(key, query, cancellationToken),
            "company-pending" => CompanyPendingAsync(key, query, cancellationToken),
            "subscriptions-active" => SubscriptionsAsync(key, query, true, cancellationToken),
            "subscriptions-expired" => SubscriptionsAsync(key, query, false, cancellationToken),
            "vehicles-inside" => VehicleMovementAsync(key, query, insideOnly: true, overstayOnly: false, cancellationToken),
            "vehicle-movement" => VehicleMovementAsync(key, query, insideOnly: false, overstayOnly: false, cancellationToken),
            "overstay" => VehicleMovementAsync(key, query, insideOnly: false, overstayOnly: true, cancellationToken),
            "invoice-summary" => InvoiceSummaryAsync(key, query, cancellationToken),
            "payments-collection" => PaymentsAsync(key, query, grouped: false, cancellationToken),
            "payment-daily" => PaymentsAsync(key, query, grouped: true, cancellationToken),
            "invoice-payment-reconciliation" => InvoicePaymentReconciliationAsync(key, query, cancellationToken),
            "user-list" => UserListAsync(key, query, cancellationToken),
            "user-permissions" => UserPermissionsAsync(key, query, cancellationToken),
            "settings-change" => SettingsChangeAsync(key, query, cancellationToken),
            _ => throw new InvalidOperationException("Report not found.")
        };
    }

    private async Task<ReportResultDto> CompanyListAsync(string key, ReportQueryDto request, CancellationToken cancellationToken)
    {
        var q = db.ParkingCompanies.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Status)) q = q.Where(x => x.Status == request.Status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            q = q.Where(x => x.CompanyCode.Contains(s) || x.CompanyName.Contains(s) || (x.Mobile != null && x.Mobile.Contains(s)) || (x.Trn != null && x.Trn.Contains(s)));
        }

        var result = NewResult(key, request, Col("companyCode", "Code"), Col("companyName", "Company"), Col("mobile", "Mobile"), Col("trn", "TRN"), Col("status", "Status"), Col("activeSlots", "Active Slots", "number"), Col("vehiclesInside", "Vehicles Inside", "number"), Col("pendingAmount", "Pending Amount", "money"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
        result.TotalRecords = await q.CountAsync(cancellationToken);
        var companies = await q.OrderBy(x => x.CompanyName).Skip(Skip(request)).Take(Size(request))
            .Select(x => new
            {
                x.CompanyCode,
                x.CompanyName,
                x.Mobile,
                x.Trn,
                x.Status,
                ActiveSlots = db.ParkingSubscriptions.Where(s => s.CompanyId == x.CompanyId && s.Status == ParkingConstants.SubscriptionStatus.Active && s.EndDate.Date >= DateTime.Today).Sum(s => (int?)s.SlotsPurchased) ?? 0,
                VehiclesInside = db.ParkingSessions.Count(s => s.CompanyId == x.CompanyId && s.Status == ParkingConstants.SessionStatus.Inside),
                PendingAmount = db.ParkingInvoices.Where(i => i.CompanyId == x.CompanyId && i.Status != "Cancelled").Sum(i => (decimal?)i.BalanceAmount) ?? 0,
                x.CreatedBy,
                x.ModifiedBy
            })
            .ToListAsync(cancellationToken);
        var users = await UserLookupAsync(companies.SelectMany(x => new[] { x.CreatedBy, x.ModifiedBy }), cancellationToken);
        result.Rows = companies.Select(x => Row(("companyCode", x.CompanyCode), ("companyName", x.CompanyName), ("mobile", x.Mobile), ("trn", x.Trn), ("status", x.Status), ("activeSlots", x.ActiveSlots.ToString(CultureInfo.InvariantCulture)), ("vehiclesInside", x.VehiclesInside.ToString(CultureInfo.InvariantCulture)), ("pendingAmount", Money(x.PendingAmount)), ("createdBy", UserName(users, x.CreatedBy)), ("modifiedBy", UserName(users, x.ModifiedBy)))).ToList();
        result.Rows.Add(Row(("companyCode", "Total"), ("companyName", string.Empty), ("mobile", string.Empty), ("trn", string.Empty), ("status", string.Empty), ("activeSlots", companies.Sum(x => x.ActiveSlots).ToString(CultureInfo.InvariantCulture)), ("vehiclesInside", companies.Sum(x => x.VehiclesInside).ToString(CultureInfo.InvariantCulture)), ("pendingAmount", Money(companies.Sum(x => x.PendingAmount))), ("createdBy", string.Empty), ("modifiedBy", string.Empty)));
        return result;
    }

    private async Task<ReportResultDto> CompanyPendingAsync(string key, ReportQueryDto request, CancellationToken cancellationToken)
    {
        var q = db.ParkingInvoices.AsNoTracking().Where(x => x.Status != "Cancelled" && x.BalanceAmount > 0);
        if (request.CompanyId.HasValue) q = q.Where(x => x.CompanyId == request.CompanyId.Value);

        var rows = await q.GroupBy(x => new { x.CompanyId, x.Company.CompanyCode, x.Company.CompanyName })
            .Select(x => new
            {
                x.Key.CompanyCode,
                x.Key.CompanyName,
                Invoices = x.Count(),
                Pending = x.Sum(i => i.BalanceAmount),
                Total = x.Sum(i => i.TotalAmount),
                LastCreatedBy = x.OrderByDescending(i => i.InvoiceDate).Select(i => i.CreatedBy).FirstOrDefault(),
                LastModifiedBy = x.OrderByDescending(i => i.ModifiedDate ?? i.CreatedDate).Select(i => i.ModifiedBy).FirstOrDefault()
            })
            .OrderByDescending(x => x.Pending)
            .Skip(Skip(request)).Take(Size(request))
            .ToListAsync(cancellationToken);
        var result = NewResult(key, request, Col("companyCode", "Code"), Col("companyName", "Company"), Col("invoices", "Invoices", "number"), Col("total", "Total", "money"), Col("pending", "Pending", "money"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
        result.TotalRecords = await q.Select(x => x.CompanyId).Distinct().CountAsync(cancellationToken);
        var users = await UserLookupAsync(rows.SelectMany(x => new[] { x.LastCreatedBy, x.LastModifiedBy }), cancellationToken);
        result.Rows = rows.Select(x => Row(("companyCode", x.CompanyCode), ("companyName", x.CompanyName), ("invoices", x.Invoices.ToString(CultureInfo.InvariantCulture)), ("total", Money(x.Total)), ("pending", Money(x.Pending)), ("createdBy", UserName(users, x.LastCreatedBy)), ("modifiedBy", UserName(users, x.LastModifiedBy)))).ToList();
        result.Rows.Add(Row(("companyCode", "Total"), ("companyName", string.Empty), ("invoices", rows.Sum(x => x.Invoices).ToString(CultureInfo.InvariantCulture)), ("total", Money(rows.Sum(x => x.Total))), ("pending", Money(rows.Sum(x => x.Pending))), ("createdBy", string.Empty), ("modifiedBy", string.Empty)));
        return result;
    }

    private async Task<ReportResultDto> SubscriptionsAsync(string key, ReportQueryDto request, bool activeOnly, CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var q = db.ParkingSubscriptions.AsNoTracking().AsQueryable();
        q = activeOnly ? q.Where(x => x.Status == ParkingConstants.SubscriptionStatus.Active && x.EndDate.Date >= today) : q.Where(x => x.Status != ParkingConstants.SubscriptionStatus.Active || x.EndDate.Date < today);
        ApplyCompany(ref q, request.CompanyId);
        ApplyStatus(ref q, request.Status);
        if (TryDateRange(request, out var from, out var toExclusive)) q = q.Where(x => x.StartDate < toExclusive && x.EndDate >= from);

        var result = NewResult(key, request, Col("company", "Company"), Col("planType", "Plan"), Col("slots", "Slots", "number"), Col("startDate", "Start"), Col("endDate", "End"), Col("status", "Status"), Col("total", "Total", "money"), Col("balance", "Pending Payment", "money"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
        result.TotalRecords = await q.CountAsync(cancellationToken);
        var subscriptions = await q.OrderByDescending(x => x.EndDate).Skip(Skip(request)).Take(Size(request))
            .Select(x => new
            {
                x.Company.CompanyName,
                x.PlanType,
                x.SlotsPurchased,
                x.StartDate,
                x.EndDate,
                x.Status,
                x.TotalAmount,
                PendingPayment = x.SourceInvoiceId == null
                    ? x.BalanceAmount
                    : db.ParkingInvoices.Where(i => i.InvoiceId == x.SourceInvoiceId.Value && i.Status != "Cancelled").Select(i => (decimal?)i.BalanceAmount).FirstOrDefault() ?? x.BalanceAmount,
                x.CreatedBy,
                x.ModifiedBy
            })
            .ToListAsync(cancellationToken);
        var users = await UserLookupAsync(subscriptions.SelectMany(x => new[] { x.CreatedBy, x.ModifiedBy }), cancellationToken);
        result.Rows = subscriptions.Select(x => Row(("company", x.CompanyName), ("planType", x.PlanType), ("slots", x.SlotsPurchased.ToString(CultureInfo.InvariantCulture)), ("startDate", Date(x.StartDate)), ("endDate", Date(x.EndDate)), ("status", x.Status), ("total", Money(x.TotalAmount)), ("balance", Money(x.PendingPayment)), ("createdBy", UserName(users, x.CreatedBy)), ("modifiedBy", UserName(users, x.ModifiedBy)))).ToList();
        result.Rows.Add(Row(("company", "Total"), ("planType", string.Empty), ("slots", subscriptions.Sum(x => x.SlotsPurchased).ToString(CultureInfo.InvariantCulture)), ("startDate", string.Empty), ("endDate", string.Empty), ("status", string.Empty), ("total", Money(subscriptions.Sum(x => x.TotalAmount))), ("balance", Money(subscriptions.Sum(x => x.PendingPayment))), ("createdBy", string.Empty), ("modifiedBy", string.Empty)));
        return result;
    }

    private async Task<ReportResultDto> VehicleMovementAsync(string key, ReportQueryDto request, bool insideOnly, bool overstayOnly, CancellationToken cancellationToken)
    {
        var q = db.ParkingSessions.AsNoTracking().AsQueryable();
        if (insideOnly) q = q.Where(x => x.Status == ParkingConstants.SessionStatus.Inside);
        if (overstayOnly) q = q.Where(x => x.OverstayDays > 0 || x.OverstayAmount > 0);
        ApplyCompany(ref q, request.CompanyId);
        ApplyStatus(ref q, request.Status);
        if (TryDateRange(request, out var from, out var toExclusive)) q = q.Where(x => (x.EntryTime != null && x.EntryTime >= from && x.EntryTime < toExclusive) || (x.ExitTime != null && x.ExitTime >= from && x.ExitTime < toExclusive));
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            q = q.Where(x => x.BarcodeNo.Contains(s) || (x.PlateNo != null && x.PlateNo.Contains(s)) || x.Company.CompanyName.Contains(s));
        }

        var columns = overstayOnly
            ? new[] { Col("barcode", "Barcode"), Col("plateNo", "Plate"), Col("company", "Company"), Col("entryTime", "Entry"), Col("exitTime", "Exit"), Col("status", "Status"), Col("overstayDays", "Overstay Days", "number"), Col("overstayAmount", "Overstay Amount", "money"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By") }
            : new[] { Col("barcode", "Barcode"), Col("plateNo", "Plate"), Col("company", "Company"), Col("entryTime", "Entry"), Col("exitTime", "Exit"), Col("validUntil", "Valid Until"), Col("duration", "Duration"), Col("status", "Status"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By") };
        var result = NewResult(key, request, columns);
        result.TotalRecords = await q.CountAsync(cancellationToken);
        var sessions = await q.OrderByDescending(x => x.CreatedDate).Skip(Skip(request)).Take(Size(request))
            .Select(x => new { x.BarcodeNo, x.PlateNo, x.Company.CompanyName, x.EntryTime, x.ExitTime, ValidUntil = x.Subscription == null ? (DateTime?)null : x.Subscription.EndDate, x.Status, x.OverstayDays, x.OverstayAmount, CreatedBy = (int?)x.CreatedBy, ModifiedBy = x.ExitOperatorId ?? x.EntryOperatorId })
            .ToListAsync(cancellationToken);
        var users = await UserLookupAsync(sessions.SelectMany(x => new[] { x.CreatedBy, x.ModifiedBy }), cancellationToken);
        result.Rows = sessions.Select(x => overstayOnly
            ? Row(("barcode", x.BarcodeNo), ("plateNo", x.PlateNo), ("company", x.CompanyName), ("entryTime", DateTimeText(x.EntryTime)), ("exitTime", DateTimeText(x.ExitTime)), ("status", x.Status), ("overstayDays", x.OverstayDays.ToString(CultureInfo.InvariantCulture)), ("overstayAmount", Money(x.OverstayAmount)), ("createdBy", UserName(users, x.CreatedBy)), ("modifiedBy", UserName(users, x.ModifiedBy)))
            : Row(("barcode", x.BarcodeNo), ("plateNo", x.PlateNo), ("company", x.CompanyName), ("entryTime", DateTimeText(x.EntryTime)), ("exitTime", DateTimeText(x.ExitTime)), ("validUntil", DateTimeText(x.ValidUntil)), ("duration", DurationText(x.EntryTime, x.ExitTime)), ("status", x.Status), ("createdBy", UserName(users, x.CreatedBy)), ("modifiedBy", UserName(users, x.ModifiedBy)))
        ).ToList();
        if (overstayOnly)
            result.Rows.Add(Row(("barcode", "Total"), ("plateNo", string.Empty), ("company", string.Empty), ("entryTime", string.Empty), ("exitTime", string.Empty), ("status", string.Empty), ("overstayDays", sessions.Sum(x => x.OverstayDays).ToString(CultureInfo.InvariantCulture)), ("overstayAmount", Money(sessions.Sum(x => x.OverstayAmount))), ("createdBy", string.Empty), ("modifiedBy", string.Empty)));
        return result;
    }

    private async Task<ReportResultDto> InvoiceSummaryAsync(string key, ReportQueryDto request, CancellationToken cancellationToken)
    {
        var q = db.ParkingInvoices.AsNoTracking().AsQueryable();
        ApplyCompany(ref q, request.CompanyId);
        ApplyStatus(ref q, request.Status);
        if (TryDateRange(request, out var from, out var toExclusive)) q = q.Where(x => x.InvoiceDate >= from && x.InvoiceDate < toExclusive);

        var result = NewResult(key, request, Col("invoiceNo", "Invoice"), Col("company", "Company"), Col("date", "Date"), Col("type", "Type"), Col("status", "Status"), Col("total", "Total", "money"), Col("paid", "Paid", "money"), Col("balance", "Balance", "money"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
        result.TotalRecords = await q.CountAsync(cancellationToken);
        var invoices = await q.OrderByDescending(x => x.InvoiceDate).Skip(Skip(request)).Take(Size(request))
            .Select(x => new { x.InvoiceNo, x.Company.CompanyName, x.InvoiceDate, x.InvoiceType, x.Status, x.TotalAmount, x.PaidAmount, x.BalanceAmount, x.CreatedBy, x.ModifiedBy })
            .ToListAsync(cancellationToken);
        var users = await UserLookupAsync(invoices.SelectMany(x => new[] { x.CreatedBy, x.ModifiedBy }), cancellationToken);
        result.Rows = invoices.Select(x => Row(("invoiceNo", x.InvoiceNo), ("company", x.CompanyName), ("date", Date(x.InvoiceDate)), ("type", x.InvoiceType), ("status", x.Status), ("total", Money(x.TotalAmount)), ("paid", Money(x.PaidAmount)), ("balance", Money(x.BalanceAmount)), ("createdBy", UserName(users, x.CreatedBy)), ("modifiedBy", UserName(users, x.ModifiedBy)))).ToList();
        result.Rows.Add(Row(("invoiceNo", "Total"), ("company", string.Empty), ("date", string.Empty), ("type", string.Empty), ("status", string.Empty), ("total", Money(invoices.Sum(x => x.TotalAmount))), ("paid", Money(invoices.Sum(x => x.PaidAmount))), ("balance", Money(invoices.Sum(x => x.BalanceAmount))), ("createdBy", string.Empty), ("modifiedBy", string.Empty)));
        return result;
    }

    private async Task<ReportResultDto> PaymentsAsync(string key, ReportQueryDto request, bool grouped, CancellationToken cancellationToken)
    {
        var q = db.ParkingPayments.AsNoTracking().AsQueryable();
        ApplyCompany(ref q, request.CompanyId);
        if (TryDateRange(request, out var from, out var toExclusive)) q = q.Where(x => x.PaymentDate >= from && x.PaymentDate < toExclusive);
        if (!string.IsNullOrWhiteSpace(request.PaymentMode))
        {
            var mode = request.PaymentMode.Trim();
            q = q.Where(x => x.PaymentMode == mode);
        }
        if (request.BankAccountId.HasValue) q = q.Where(x => x.BankAccountId == request.BankAccountId.Value);

        if (grouped)
        {
            var rows = await q.GroupBy(x => new { Date = x.PaymentDate.Date, x.PaymentMode, BankName = x.BankAccount == null ? "" : x.BankAccount.BankName })
                .Select(x => new { x.Key.Date, Mode = x.Key.PaymentMode, x.Key.BankName, Count = x.Count(), Amount = x.Sum(p => p.Amount), ReceivedBy = (int?)x.OrderByDescending(p => p.PaymentDate).Select(p => p.ReceivedBy).FirstOrDefault() })
                .OrderByDescending(x => x.Date)
                .Skip(Skip(request)).Take(Size(request))
                .ToListAsync(cancellationToken);
            var result = NewResult(key, request, Col("date", "Date"), Col("mode", "Mode"), Col("bank", "Bank"), Col("receipts", "Receipts", "number"), Col("amount", "Amount", "money"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
            result.TotalRecords = await q.Select(x => new { Date = x.PaymentDate.Date, x.PaymentMode, BankName = x.BankAccount == null ? "" : x.BankAccount.BankName }).Distinct().CountAsync(cancellationToken);
            var users = await UserLookupAsync(rows.Select(x => x.ReceivedBy), cancellationToken);
            result.Rows = rows.Select(x => Row(("date", Date(x.Date)), ("mode", x.Mode), ("bank", x.BankName), ("receipts", x.Count.ToString(CultureInfo.InvariantCulture)), ("amount", Money(x.Amount)), ("createdBy", UserName(users, x.ReceivedBy)), ("modifiedBy", string.Empty))).ToList();
            result.Rows.Add(Row(("date", "Total"), ("mode", string.Empty), ("bank", string.Empty), ("receipts", rows.Sum(x => x.Count).ToString(CultureInfo.InvariantCulture)), ("amount", Money(rows.Sum(x => x.Amount))), ("createdBy", string.Empty), ("modifiedBy", string.Empty)));
            return result;
        }

        var detail = NewResult(key, request, Col("receiptNo", "Receipt"), Col("company", "Company"), Col("date", "Date"), Col("type", "Type"), Col("mode", "Mode"), Col("bank", "Bank"), Col("amount", "Amount", "money"), Col("reference", "Reference"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
        detail.TotalRecords = await q.CountAsync(cancellationToken);
        var payments = await q.OrderByDescending(x => x.PaymentDate).Skip(Skip(request)).Take(Size(request))
            .Select(x => new { x.ReceiptNo, x.Company.CompanyName, x.PaymentDate, x.PaymentType, x.PaymentMode, BankName = x.BankAccount == null ? "" : x.BankAccount.BankName, x.Amount, x.ReferenceNo, ReceivedBy = (int?)x.ReceivedBy })
            .ToListAsync(cancellationToken);
        var detailUsers = await UserLookupAsync(payments.Select(x => x.ReceivedBy), cancellationToken);
        detail.Rows = payments.Select(x => Row(("receiptNo", x.ReceiptNo), ("company", x.CompanyName), ("date", DateTimeText(x.PaymentDate)), ("type", x.PaymentType), ("mode", x.PaymentMode), ("bank", x.BankName), ("amount", Money(x.Amount)), ("reference", x.ReferenceNo), ("createdBy", UserName(detailUsers, x.ReceivedBy)), ("modifiedBy", string.Empty))).ToList();
        detail.Rows.Add(Row(("receiptNo", "Total"), ("company", string.Empty), ("date", string.Empty), ("type", string.Empty), ("mode", string.Empty), ("bank", string.Empty), ("amount", Money(payments.Sum(x => x.Amount))), ("reference", string.Empty), ("createdBy", string.Empty), ("modifiedBy", string.Empty)));
        return detail;
    }

    private async Task<ReportResultDto> InvoicePaymentReconciliationAsync(string key, ReportQueryDto request, CancellationToken cancellationToken)
    {
        var payments = db.ParkingPayments.AsNoTracking().Where(x => x.InvoiceId != null)
            .GroupBy(x => x.InvoiceId!.Value)
            .Select(x => new { InvoiceId = x.Key, LinkedPaid = x.Sum(p => p.Amount), PaymentRows = x.Count() });

        var q = db.ParkingInvoices.AsNoTracking()
            .GroupJoin(payments, i => i.InvoiceId, p => p.InvoiceId, (i, p) => new { Invoice = i, Payment = p.FirstOrDefault() })
            .Where(x => x.Invoice.Status != "Cancelled" && ((x.Payment == null && x.Invoice.PaidAmount != 0) || (x.Payment != null && x.Invoice.PaidAmount != x.Payment.LinkedPaid) || x.Invoice.BalanceAmount != x.Invoice.TotalAmount - (x.Payment == null ? 0 : x.Payment.LinkedPaid)));

        if (request.CompanyId.HasValue) q = q.Where(x => x.Invoice.CompanyId == request.CompanyId.Value);

        var result = NewResult(key, request, Col("invoiceNo", "Invoice"), Col("company", "Company"), Col("storedPaid", "Stored Paid", "money"), Col("linkedPaid", "Linked Paid", "money"), Col("storedBalance", "Stored Balance", "money"), Col("expectedBalance", "Expected Balance", "money"), Col("rows", "Payment Rows", "number"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
        result.TotalRecords = await q.CountAsync(cancellationToken);
        var mismatches = await q.OrderByDescending(x => x.Invoice.InvoiceDate).Skip(Skip(request)).Take(Size(request))
            .Select(x => new { x.Invoice.InvoiceNo, x.Invoice.Company.CompanyName, x.Invoice.PaidAmount, LinkedPaid = x.Payment == null ? 0 : x.Payment.LinkedPaid, x.Invoice.BalanceAmount, x.Invoice.TotalAmount, PaymentRows = x.Payment == null ? 0 : x.Payment.PaymentRows, x.Invoice.CreatedBy, x.Invoice.ModifiedBy })
            .ToListAsync(cancellationToken);
        var users = await UserLookupAsync(mismatches.SelectMany(x => new[] { x.CreatedBy, x.ModifiedBy }), cancellationToken);
        result.Rows = mismatches.Select(x => Row(("invoiceNo", x.InvoiceNo), ("company", x.CompanyName), ("storedPaid", Money(x.PaidAmount)), ("linkedPaid", Money(x.LinkedPaid)), ("storedBalance", Money(x.BalanceAmount)), ("expectedBalance", Money(x.TotalAmount - x.LinkedPaid)), ("rows", x.PaymentRows.ToString(CultureInfo.InvariantCulture)), ("createdBy", UserName(users, x.CreatedBy)), ("modifiedBy", UserName(users, x.ModifiedBy)))).ToList();
        result.Rows.Add(Row(("invoiceNo", "Total"), ("company", string.Empty), ("storedPaid", Money(mismatches.Sum(x => x.PaidAmount))), ("linkedPaid", Money(mismatches.Sum(x => x.LinkedPaid))), ("storedBalance", Money(mismatches.Sum(x => x.BalanceAmount))), ("expectedBalance", Money(mismatches.Sum(x => x.TotalAmount - x.LinkedPaid))), ("rows", mismatches.Sum(x => x.PaymentRows).ToString(CultureInfo.InvariantCulture)), ("createdBy", string.Empty), ("modifiedBy", string.Empty)));
        return result;
    }

    private async Task<ReportResultDto> UserListAsync(string key, ReportQueryDto request, CancellationToken cancellationToken)
    {
        var q = db.AppUsers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Status)) q = q.Where(x => x.Status == request.Status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            q = q.Where(x => x.Username.Contains(s) || x.FullName.Contains(s) || (x.Email != null && x.Email.Contains(s)));
        }

        var roleRows = await db.AppUserRoles.AsNoTracking().Where(x => x.Role != null).Select(x => new { x.UserId, x.Role!.RoleName }).ToListAsync(cancellationToken);
        var result = NewResult(key, request, Col("username", "Username"), Col("fullName", "Full Name"), Col("email", "Email"), Col("status", "Status"), Col("roles", "Roles"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
        result.TotalRecords = await q.CountAsync(cancellationToken);
        var users = await q.OrderBy(x => x.Username).Skip(Skip(request)).Take(Size(request)).ToListAsync(cancellationToken);
        var userLookup = await UserLookupAsync(users.Select(x => x.ModifiedBy), cancellationToken);
        result.Rows = users.Select(x => Row(
            ("username", x.Username), ("fullName", x.FullName), ("email", x.Email), ("status", x.Status), ("roles", string.Join(", ", roleRows.Where(r => r.UserId == x.UserId).Select(r => r.RoleName).Distinct())), ("createdBy", string.Empty), ("modifiedBy", UserName(userLookup, x.ModifiedBy))
        )).ToList();
        return result;
    }

    private async Task<ReportResultDto> UserPermissionsAsync(string key, ReportQueryDto request, CancellationToken cancellationToken)
    {
        var q = db.AppUserPermissions.AsNoTracking().Where(x => x.User != null && x.Module != null && x.Action != null);
        var result = NewResult(key, request, Col("user", "User"), Col("module", "Module"), Col("action", "Action"), Col("override", "Override"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
        result.TotalRecords = await q.CountAsync(cancellationToken);
        var overrides = await q.OrderBy(x => x.User!.Username).ThenBy(x => x.Module!.SortOrder).Skip(Skip(request)).Take(Size(request))
            .Select(x => new { x.User!.Username, x.User.FullName, x.Module!.ModuleName, x.Action!.ActionName, x.IsAllowed })
            .ToListAsync(cancellationToken);
        result.Rows = overrides.Select(x => Row(("user", string.IsNullOrWhiteSpace(x.FullName) ? x.Username : x.FullName), ("module", x.ModuleName), ("action", x.ActionName), ("override", x.IsAllowed ? "Allow" : "Deny"), ("createdBy", string.Empty), ("modifiedBy", string.Empty))).ToList();
        return result;
    }

    private async Task<ReportResultDto> SettingsChangeAsync(string key, ReportQueryDto request, CancellationToken cancellationToken)
    {
        var q = db.SystemActivityLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Status)) q = q.Where(x => x.Result == request.Status);
        if (TryDateRange(request, out var from, out var toExclusive)) q = q.Where(x => x.ActivityDate >= from && x.ActivityDate < toExclusive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            q = q.Where(x => x.Username.Contains(s) || x.ModuleKey.Contains(s) || x.ActionKey.Contains(s) || x.Title.Contains(s) || (x.Message != null && x.Message.Contains(s)) || x.EntityType.Contains(s) || (x.EntityId != null && x.EntityId.Contains(s)));
        }

        var result = NewResult(key, request, Col("date", "Date"), Col("user", "User"), Col("module", "Module"), Col("action", "Action"), Col("entity", "Entity"), Col("result", "Result"), Col("title", "Title"), Col("message", "Message"), Col("createdBy", "Created By"), Col("modifiedBy", "Modified By"));
        result.TotalRecords = await q.CountAsync(cancellationToken);
        var activity = await q.OrderByDescending(x => x.ActivityDate).Skip(Skip(request)).Take(Size(request))
            .Select(x => new { x.ActivityDate, x.Username, x.ModuleKey, x.ActionKey, Entity = string.IsNullOrWhiteSpace(x.EntityId) ? x.EntityType : x.EntityType + " #" + x.EntityId, x.Result, x.Title, x.Message })
            .ToListAsync(cancellationToken);
        result.Rows = activity.Select(x => Row(("date", DateTimeText(x.ActivityDate)), ("user", x.Username), ("module", x.ModuleKey), ("action", x.ActionKey), ("entity", x.Entity), ("result", x.Result), ("title", x.Title), ("message", x.Message), ("createdBy", x.Username), ("modifiedBy", string.Empty))).ToList();
        return result;
    }

    private static ReportResultDto NewResult(string key, ReportQueryDto request, params ReportColumnDto[] columns)
    {
        var item = Catalog.First(x => x.ReportKey == key);
        return new ReportResultDto { ReportKey = key, ReportName = item.ReportName, PageNumber = Math.Max(request.PageNumber, 1), PageSize = Size(request), Columns = columns.ToList() };
    }

    private static ReportColumnDto Col(string key, string label, string type = "text") => new() { Key = key, Label = label, DataType = type };
    private static Dictionary<string, string?> Row(params (string Key, string? Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value);
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string DateTimeText(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    private static string DurationText(DateTime? from, DateTime? to)
    {
        if (!from.HasValue) return string.Empty;
        var duration = (to ?? DateTime.Now) - from.Value;
        if (duration < TimeSpan.Zero) return string.Empty;
        if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{Math.Max(0, duration.Minutes)}m";
    }

    private async Task<Dictionary<int, string>> UserLookupAsync(IEnumerable<int?> ids, CancellationToken cancellationToken)
    {
        var userIds = ids.Where(x => x.HasValue && x.Value > 0).Select(x => x!.Value).Distinct().ToList();
        if (userIds.Count == 0) return new Dictionary<int, string>();

        return await db.AppUsers.AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Select(x => new { x.UserId, Name = string.IsNullOrWhiteSpace(x.FullName) ? x.Username : x.FullName })
            .ToDictionaryAsync(x => x.UserId, x => x.Name, cancellationToken);
    }

    private static string UserName(IReadOnlyDictionary<int, string> users, int? id)
    {
        if (!id.HasValue || id.Value <= 0) return string.Empty;
        return users.TryGetValue(id.Value, out var name) ? name : $"User #{id.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static int Size(ReportQueryDto request) => request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500);
    private static int Skip(ReportQueryDto request) => (Math.Max(request.PageNumber, 1) - 1) * Size(request);

    private static void ApplyCompany<T>(ref IQueryable<T> q, int? companyId) where T : class
    {
        if (!companyId.HasValue) return;
        q = q.Where(x => EF.Property<int>(x, "CompanyId") == companyId.Value);
    }

    private static void ApplyStatus<T>(ref IQueryable<T> q, string? status) where T : class
    {
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => EF.Property<string>(x, "Status") == status);
    }

    private static bool TryDateRange(ReportQueryDto request, out DateTime from, out DateTime toExclusive)
    {
        from = DateTime.MinValue;
        toExclusive = DateTime.MinValue;
        if (!request.DateFrom.HasValue && !request.DateTo.HasValue) return false;
        from = (request.DateFrom ?? request.DateTo ?? DateTime.Today).Date;
        toExclusive = (request.DateTo ?? request.DateFrom ?? DateTime.Today).Date.AddDays(1);
        if (toExclusive < from)
        {
            (from, toExclusive) = (toExclusive.AddDays(-1), from.AddDays(1));
        }
        return true;
    }
}
