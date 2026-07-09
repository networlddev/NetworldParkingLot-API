namespace NetworldParkingLot.Api.Features.Reports.Dtos;

public sealed class ReportCatalogItemDto
{
    public string ReportKey { get; set; } = string.Empty;
    public string ReportName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SupportsDateRange { get; set; } = true;
    public bool SupportsStatus { get; set; } = true;
    public bool SupportsCompany { get; set; } = true;
}

public sealed class ReportQueryDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? CompanyId { get; set; }
    public string? Status { get; set; }
    public string? PaymentMode { get; set; }
    public int? BankAccountId { get; set; }
    public string? Search { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class ReportResultDto
{
    public string ReportKey { get; set; } = string.Empty;
    public string ReportName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public List<ReportColumnDto> Columns { get; set; } = [];
    public List<Dictionary<string, string?>> Rows { get; set; } = [];
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalRecords { get; set; }
}

public sealed class ReportColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DataType { get; set; } = "text";
}
