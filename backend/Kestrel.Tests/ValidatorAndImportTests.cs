using System.Text;
using Kestrel.Api.Domain;
using Kestrel.Api.Dtos;
using Kestrel.Api.Repositories;
using Kestrel.Api.Services;
using Kestrel.Api.Validators;
using Moq;

namespace Kestrel.Tests;

public class ValidatorTests
{
    [Fact]
    public void Product_needs_a_positive_price()
    {
        var result = new SaveProductValidator().Validate(new SaveProductDto("KO-X-1", "Jacket", 1, "Kestrel", 0m, 5m));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Valid_product_passes()
    {
        var result = new SaveProductValidator().Validate(new SaveProductDto("KO-X-1", "Jacket", 1, "Kestrel", 80m, 30m));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Order_must_have_at_least_one_line()
    {
        var result = new CreateOrderValidator().Validate(new CreateOrderDto(1, 1, "WEB", new List<CreateOrderLineDto>()));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Payment_method_must_be_known()
    {
        var result = new CreatePaymentValidator().Validate(new CreatePaymentDto("BITCOIN", 10m, null, null));
        Assert.False(result.IsValid);
    }
}

public class StockFileImportServiceTests
{
    [Fact]
    public async Task A_delivery_line_adds_to_the_store_stock()
    {
        var imports = new Mock<IImportRepository>();
        imports.Setup(r => r.AddBatchAsync(It.IsAny<ImportBatch>())).ReturnsAsync((ImportBatch b) => b);
        imports.Setup(r => r.GetAsync(It.IsAny<int>())).ReturnsAsync((int _) => new ImportBatch { Id = 0, FileName = "f.csv" });
        var products = new Mock<IProductRepository>();
        products.Setup(r => r.GetBySkuAsync("KO-FLC-0012")).ReturnsAsync(new Product { Id = 12, Sku = "KO-FLC-0012" });
        var stores = new Mock<IStoreRepository>();
        stores.Setup(r => r.GetByCodeAsync("EDI")).ReturnsAsync(new Store { Id = 1, Code = "EDI" });
        var stock = new Mock<IStockRepository>();
        var level = new StockLevel { Id = 3, StoreId = 1, ProductId = 12, Quantity = 4 };
        stock.Setup(r => r.GetForStoreProductAsync(1, 12)).ReturnsAsync(level);
        var sut = new StockFileImportService(imports.Object, products.Object, stores.Object, stock.Object, new Mock<ICategoryRepository>().Object);

        var csv = "type,sku,store_code,qty\nDELIVERY,KO-FLC-0012,EDI,6\n";
        await sut.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "f.csv");

        Assert.Equal(10, level.Quantity);
    }
}
