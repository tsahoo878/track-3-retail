using Kestrel.Api.Dtos;
using Kestrel.Api.Infrastructure;
using Kestrel.Api.Repositories;

namespace Kestrel.Api.Services;

public interface IReportService
{
    Task<DailySalesDto> GetDailySalesAsync(DateOnly? date);
    Task<List<DailySalesSummaryDto>> GetDailySalesRangeAsync(DateOnly from, DateOnly to);
    Task<List<TopProductDto>> GetTopProductsAsync(DateOnly from, DateOnly to, int take);
}

public class ReportService : IReportService
{
    private readonly IReportRepository _reports;
    private readonly IStoreRepository _stores;

    public ReportService(IReportRepository reports, IStoreRepository stores)
    {
        _reports = reports;
        _stores = stores;
    }

    public async Task<DailySalesDto> GetDailySalesAsync(DateOnly? date)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = day.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var rows = await _reports.GetSalesOrdersAsync(start, end);
        var stores = (await _stores.ListAsync()).ToDictionary(s => s.Id, s => s.Name);

        var byChannel = rows.GroupBy(r => r.Channel)
            .Select(g => new ChannelSalesDto(g.Key, g.Count(), g.Sum(x => x.TotalAmount)))
            .OrderBy(x => x.Channel).ToList();
        var byStore = rows.GroupBy(r => r.StoreId)
            .Select(g => new StoreSalesDto(g.Key, stores.GetValueOrDefault(g.Key, ""), g.Count(), g.Sum(x => x.TotalAmount)))
            .OrderByDescending(x => x.NetSales).ToList();

        return new DailySalesDto(day, rows.Count, rows.Sum(r => r.Subtotal), rows.Sum(r => r.DiscountAmount),
            rows.Sum(r => r.TotalAmount), byChannel, byStore);
    }

    public async Task<List<DailySalesSummaryDto>> GetDailySalesRangeAsync(DateOnly from, DateOnly to)
    {
        if (to < from) throw new BusinessRuleException("'to' must not be before 'from'.");
        if (to.DayNumber - from.DayNumber > 92) throw new BusinessRuleException("The range can be at most 93 days.");
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await _reports.GetSalesOrdersAsync(start, end);
        return rows.GroupBy(r => DateOnly.FromDateTime(r.OrderedUtc.Date))
            .Select(g => new DailySalesSummaryDto(g.Key, g.Count(), g.Sum(x => x.TotalAmount)))
            .OrderBy(x => x.Date).ToList();
    }

    public async Task<List<TopProductDto>> GetTopProductsAsync(DateOnly from, DateOnly to, int take)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await _reports.GetTopProductsAsync(start, end, Math.Clamp(take, 1, 50));
        return rows.Select(r => new TopProductDto(r.ProductId, r.Sku, r.Name, r.Units, r.Revenue)).ToList();
    }
}
