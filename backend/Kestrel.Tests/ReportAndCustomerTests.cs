using Kestrel.Api.Domain;
using Kestrel.Api.Repositories;
using Kestrel.Api.Services;
using Moq;

namespace Kestrel.Tests;

public class ReportServiceTests
{
    [Fact]
    public async Task Daily_sales_adds_up_the_orders_returned_for_the_day()
    {
        var reports = new Mock<IReportRepository>();
        reports.Setup(r => r.GetSalesOrdersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new List<SalesOrderRow>
        {
            new(1, 1, "STORE", new DateTime(2026, 9, 15, 11, 0, 0), 100m, 0m, 100m),
            new(2, 1, "WEB", new DateTime(2026, 9, 15, 15, 30, 0), 60m, 6m, 54m)
        });
        var stores = new Mock<IStoreRepository>();
        stores.Setup(s => s.ListAsync()).ReturnsAsync(new List<Store> { new() { Id = 1, Name = "Edinburgh" } });
        var sut = new ReportService(reports.Object, stores.Object);

        var report = await sut.GetDailySalesAsync(new DateOnly(2026, 9, 15));

        Assert.Equal(2, report.OrderCount);
        Assert.Equal(154m, report.NetSales);
        Assert.Equal(6m, report.Discounts);
    }
}

public class CustomerServiceTests
{
    [Fact]
    public void New_customers_this_month_are_counted()
    {
        var customers = new List<Customer>
        {
            new() { Id = 1, CreatedUtc = DateTime.UtcNow.AddDays(-2) },
            new() { Id = 2, CreatedUtc = DateTime.UtcNow.AddDays(-45) },
            new() { Id = 3, CreatedUtc = DateTime.UtcNow }
        };

        Assert.Equal(2, CustomerService.CountNewThisMonth(customers));
    }
}
