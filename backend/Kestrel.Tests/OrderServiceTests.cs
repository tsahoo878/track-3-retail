using FluentValidation;
using Kestrel.Api.Domain;
using Kestrel.Api.Dtos;
using Kestrel.Api.Infrastructure;
using Kestrel.Api.Repositories;
using Kestrel.Api.Services;
using Kestrel.Api.Validators;
using Moq;

namespace Kestrel.Tests;

public class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<IStoreRepository> _stores = new();
    private readonly Mock<IProductRepository> _products = new();
    private readonly Mock<IStockRepository> _stock = new();
    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        _sut = new OrderService(_orders.Object, _customers.Object, _stores.Object, _products.Object, _stock.Object,
            new CreateOrderValidator(), new ChangeStatusValidator(), new AddNoteValidator());
    }

    private static CreateOrderDto OneLine(int qty) =>
        new(1, 1, "STORE", new List<CreateOrderLineDto> { new(1, qty) });

    private void SetUpStoreAndProduct()
    {
        _stores.Setup(r => r.GetAsync(1)).ReturnsAsync(new Store { Id = 1, Name = "Edinburgh", IsActive = true });
        _products.Setup(r => r.GetAsync(1)).ReturnsAsync(new Product { Id = 1, Sku = "KO-TEST-1", Name = "Test jacket", UnitPrice = 50m, IsActive = true });
    }

    [Fact]
    public async Task Creating_an_order_takes_the_quantity_off_the_shelf()
    {
        SetUpStoreAndProduct();
        _customers.Setup(r => r.GetReadOnlyAsync(1)).ReturnsAsync(new Customer { Id = 1, FirstName = "A", LastName = "B", LoyaltyTier = "GOLD" });
        var stock = new StockLevel { Id = 1, StoreId = 1, ProductId = 1, Quantity = 5 };
        _stock.Setup(r => r.GetForStoreProductAsync(1, 1)).ReturnsAsync(stock);
        Order? saved = null;
        _orders.Setup(r => r.AddAsync(It.IsAny<Order>())).Callback<Order>(o => saved = o).Returns(Task.CompletedTask);
        _orders.Setup(r => r.GetWithDetailsAsync(It.IsAny<int>())).ReturnsAsync(() => saved);

        var result = await _sut.CreateAsync(OneLine(2));

        Assert.Equal(3, stock.Quantity);
        Assert.Equal(90m, result.TotalAmount);
    }

    [Fact]
    public async Task Cannot_oversell_when_stock_is_insufficient()
    {
        SetUpStoreAndProduct();
        _stock.Setup(r => r.GetForStoreProductAsync(1, 1)).ReturnsAsync(new StockLevel { Id = 1, StoreId = 1, ProductId = 1, Quantity = 0 });

        await Assert.ThrowsAnyAsync<Exception>(() => _sut.CreateAsync(OneLine(1)));
    }

    [Fact]
    public async Task A_fulfilled_order_cannot_be_cancelled()
    {
        _orders.Setup(r => r.GetForUpdateAsync(7)).ReturnsAsync(new Order { Id = 7, Status = OrderStatus.Fulfilled });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ChangeStatusAsync(7, new ChangeStatusDto("CANCELLED", null)));
        Assert.Contains("FULFILLED", ex.Message);
    }

    [Fact]
    public async Task Unknown_order_is_reported_as_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(999));
    }
}
