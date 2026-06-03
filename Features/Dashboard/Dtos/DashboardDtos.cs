namespace NetworldParkingLot.Api.Features.Dashboard.Dtos;

public sealed class DashboardSummaryDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public int TotalCapacity { get; set; }
    public int VehiclesInside { get; set; }
    public int AvailableSpaces { get; set; }
    public int TotalCompanies { get; set; }
    public int ActiveCompanies { get; set; }
    public int ActiveSubscriptions { get; set; }
    public int ExpiringSubscriptions { get; set; }
    public int PendingInvoices { get; set; }
    public decimal PendingAmount { get; set; }
    public decimal CollectedAmount { get; set; }
    public int NewCompanies { get; set; }
    public int OverstayVehicles { get; set; }
    public DateTime PreviousDateFrom { get; set; }
    public DateTime PreviousDateTo { get; set; }
    public List<DashboardComparisonMetricDto> ComparisonMetrics { get; set; } = [];
    public List<DashboardChartPointDto> CompanyRegistrationTrend { get; set; } = [];
    public List<DashboardChartPointDto> EntryExitTrend { get; set; } = [];
    public List<DashboardChartPointDto> RevenueTrend { get; set; } = [];
    public List<DashboardChartPointDto> InvoiceStatus { get; set; } = [];
    public List<DashboardChartPointDto> SubscriptionStatus { get; set; } = [];
    public List<DashboardChartPointDto> PaymentModes { get; set; } = [];
    public List<DashboardCompanyMetricDto> TopUsageCompanies { get; set; } = [];
    public List<DashboardCompanyMetricDto> TopPendingCompanies { get; set; } = [];
}

public sealed class DashboardChartPointDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal? SecondaryValue { get; set; }
}

public sealed class DashboardComparisonMetricDto
{
    public string MetricKey { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public decimal CurrentValue { get; set; }
    public decimal PreviousValue { get; set; }
    public decimal ChangePercent { get; set; }
    public string ValueType { get; set; } = "number";
}

public sealed class DashboardCompanyMetricDto
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal? SecondaryValue { get; set; }
    public string Status { get; set; } = string.Empty;
}
