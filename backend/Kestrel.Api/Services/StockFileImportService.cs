using System.Globalization;
using Kestrel.Api.Domain;
using Kestrel.Api.Dtos;
using Kestrel.Api.Infrastructure;
using Kestrel.Api.Mappers;
using Kestrel.Api.Repositories;

namespace Kestrel.Api.Services;

public interface IStockFileImportService
{
    Task<ImportBatchDetailDto> ImportAsync(Stream content, string fileName);
    Task<List<ImportBatchDto>> ListAsync();
    Task<ImportBatchDetailDto> GetAsync(int id);
}

public class StockFileImportService : IStockFileImportService
{
    private readonly IImportRepository _imports;
    private readonly IProductRepository _products;
    private readonly IStoreRepository _stores;
    private readonly IStockRepository _stock;
    private readonly ICategoryRepository _categories;

    public StockFileImportService(IImportRepository imports, IProductRepository products, IStoreRepository stores,
        IStockRepository stock, ICategoryRepository categories)
    {
        _imports = imports;
        _products = products;
        _stores = stores;
        _stock = stock;
        _categories = categories;
    }

    public async Task<List<ImportBatchDto>> ListAsync() => (await _imports.ListAsync()).Select(ImportMapper.ToDto).ToList();

    public async Task<ImportBatchDetailDto> GetAsync(int id)
    {
        var b = await _imports.GetAsync(id) ?? throw new NotFoundException($"Import {id} was not found.");
        return ImportMapper.ToDetail(b);
    }

    public async Task<ImportBatchDetailDto> ImportAsync(Stream content, string fileName)
    {
        var batch = new ImportBatch { FileName = fileName, StartedUtc = DateTime.UtcNow, Status = "RUNNING" };
        await _imports.AddBatchAsync(batch);
        var reader = new StreamReader(content);
        var lineNo = 0;
        var applied = 0;
        var rejected = 0;
        var total = 0;
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            lineNo++;
            if (line.Trim().Length == 0) continue;
            if (line.StartsWith("#")) continue;
            if (lineNo == 1 && line.ToLower().StartsWith("type")) continue;
            total++;
            var cols = line.Split(',');
            for (var i = 0; i < cols.Length; i++) cols[i] = cols[i].Trim();
            var type = cols[0].ToUpper();
            var message = "";
            try
            {
                if (type == "DELIVERY")
                {
                    if (cols.Length < 4)
                    {
                        message = "DELIVERY needs sku, store and qty.";
                    }
                    else
                    {
                        var sku = cols[1];
                        var storeCode = cols[2];
                        var qtyText = cols[3];
                        if (sku == "" || storeCode == "" || qtyText == "")
                        {
                            message = "DELIVERY needs sku, store and qty.";
                        }
                        else if (!int.TryParse(qtyText, out var qty))
                        {
                            message = "Quantity '" + qtyText + "' is not a number.";
                        }
                        else if (qty <= 0)
                        {
                            message = "Delivery quantity must be positive.";
                        }
                        else
                        {
                            var product = await _products.GetBySkuAsync(sku);
                            if (product == null)
                            {
                                message = "Unknown SKU " + sku + ".";
                            }
                            else
                            {
                                var store = await _stores.GetByCodeAsync(storeCode);
                                if (store == null)
                                {
                                    message = "Unknown store " + storeCode + ".";
                                }
                                else
                                {
                                    var stock = await _stock.GetForStoreProductAsync(store.Id, product.Id);
                                    if (stock == null)
                                    {
                                        stock = new StockLevel
                                        {
                                            StoreId = store.Id,
                                            ProductId = product.Id,
                                            Quantity = qty,
                                            ReorderLevel = 2,
                                            UpdatedUtc = DateTime.UtcNow
                                        };
                                        await _stock.AddAsync(stock);
                                    }
                                    else
                                    {
                                        stock.Quantity = stock.Quantity + qty;
                                        stock.UpdatedUtc = DateTime.UtcNow;
                                    }
                                    if (cols.Length > 4 && cols[4] != "")
                                    {
                                        if (decimal.TryParse(cols[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var cost))
                                        {
                                            if (cost > 0)
                                            {
                                                product.CostPrice = cost;
                                            }
                                            else
                                            {
                                                message = "Unit cost must be positive.";
                                            }
                                        }
                                        else
                                        {
                                            message = "Unit cost '" + cols[4] + "' is not a number.";
                                        }
                                    }
                                    if (message == "")
                                    {
                                        await _stock.SaveAsync();
                                        applied++;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (type == "COUNT")
                {
                    if (cols.Length < 4)
                    {
                        message = "COUNT needs sku, store and qty.";
                    }
                    else
                    {
                        var sku = cols[1];
                        var storeCode = cols[2];
                        var qtyText = cols[3];
                        if (sku == "" || storeCode == "" || qtyText == "")
                        {
                            message = "COUNT needs sku, store and qty.";
                        }
                        else if (!int.TryParse(qtyText, out var qty))
                        {
                            message = "Quantity '" + qtyText + "' is not a number.";
                        }
                        else if (qty < 0)
                        {
                            message = "Counted quantity cannot be negative.";
                        }
                        else
                        {
                            var product = await _products.GetBySkuAsync(sku);
                            if (product == null)
                            {
                                message = "Unknown SKU " + sku + ".";
                            }
                            else
                            {
                                var store = await _stores.GetByCodeAsync(storeCode);
                                if (store == null)
                                {
                                    message = "Unknown store " + storeCode + ".";
                                }
                                else
                                {
                                    var stock = await _stock.GetForStoreProductAsync(store.Id, product.Id);
                                    if (stock == null)
                                    {
                                        stock = new StockLevel
                                        {
                                            StoreId = store.Id,
                                            ProductId = product.Id,
                                            Quantity = qty,
                                            ReorderLevel = 2,
                                            UpdatedUtc = DateTime.UtcNow
                                        };
                                        await _stock.AddAsync(stock);
                                    }
                                    else
                                    {
                                        stock.Quantity = qty;
                                        stock.UpdatedUtc = DateTime.UtcNow;
                                    }
                                    await _stock.SaveAsync();
                                    applied++;
                                }
                            }
                        }
                    }
                }
                else if (type == "DISCONTINUE")
                {
                    if (cols.Length < 2 || cols[1] == "")
                    {
                        message = "DISCONTINUE needs a sku.";
                    }
                    else
                    {
                        var product = await _products.GetBySkuAsync(cols[1]);
                        if (product == null)
                        {
                            message = "Unknown SKU " + cols[1] + ".";
                        }
                        else
                        {
                            product.IsActive = false;
                            if (cols.Length > 2 && cols[2] != "")
                            {
                                var store = await _stores.GetByCodeAsync(cols[2]);
                                if (store == null)
                                {
                                    message = "Unknown store " + cols[2] + ".";
                                    product.IsActive = true;
                                }
                                else
                                {
                                    var stock = await _stock.GetForStoreProductAsync(store.Id, product.Id);
                                    if (stock != null)
                                    {
                                        stock.Quantity = 0;
                                        stock.UpdatedUtc = DateTime.UtcNow;
                                    }
                                }
                            }
                            if (message == "")
                            {
                                await _products.SaveAsync();
                                applied++;
                            }
                        }
                    }
                }
                else if (type == "NEW")
                {
                    if (cols.Length < 8)
                    {
                        message = "NEW needs sku, store, qty, cost, price, name and category.";
                    }
                    else
                    {
                        var sku = cols[1];
                        var storeCode = cols[2];
                        var qtyText = cols[3];
                        var costText = cols[4];
                        var priceText = cols[5];
                        var name = cols[6];
                        var categoryCode = cols[7];
                        if (sku == "" || name == "" || categoryCode == "" || priceText == "")
                        {
                            message = "NEW needs sku, name, category and price.";
                        }
                        else if (!decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price <= 0)
                        {
                            message = "Retail price '" + priceText + "' is not valid.";
                        }
                        else
                        {
                            decimal cost = 0;
                            if (costText != "" && !decimal.TryParse(costText, NumberStyles.Any, CultureInfo.InvariantCulture, out cost))
                            {
                                message = "Unit cost '" + costText + "' is not a number.";
                            }
                            else if (await _products.GetBySkuAsync(sku) != null)
                            {
                                message = "SKU " + sku + " already exists.";
                            }
                            else
                            {
                                var category = await _categories.GetByCodeAsync(categoryCode);
                                if (category == null)
                                {
                                    message = "Unknown category " + categoryCode + ".";
                                }
                                else
                                {
                                    var product = new Product
                                    {
                                        Sku = sku,
                                        Name = name,
                                        CategoryId = category.Id,
                                        Brand = "Kestrel",
                                        UnitPrice = price,
                                        CostPrice = cost,
                                        IsActive = true,
                                        CreatedUtc = DateTime.UtcNow
                                    };
                                    await _products.AddAsync(product);
                                    await _products.SaveAsync();
                                    if (storeCode != "" && qtyText != "")
                                    {
                                        var store = await _stores.GetByCodeAsync(storeCode);
                                        if (store == null)
                                        {
                                            message = "Product created but store " + storeCode + " is unknown.";
                                        }
                                        else if (!int.TryParse(qtyText, out var qty) || qty < 0)
                                        {
                                            message = "Product created but quantity '" + qtyText + "' is not valid.";
                                        }
                                        else
                                        {
                                            await _stock.AddAsync(new StockLevel
                                            {
                                                StoreId = store.Id,
                                                ProductId = product.Id,
                                                Quantity = qty,
                                                ReorderLevel = 2,
                                                UpdatedUtc = DateTime.UtcNow
                                            });
                                            await _stock.SaveAsync();
                                        }
                                    }
                                    if (message == "") applied++;
                                }
                            }
                        }
                    }
                }
                else
                {
                    message = "Unknown line type '" + cols[0] + "'.";
                }
            }
            catch (Exception ex)
            {
                message = "Line failed: " + ex.GetBaseException().Message;
            }

            if (message != "")
            {
                rejected++;
                if (message.Length > 300) message = message.Substring(0, 300);
                await _imports.AddErrorAsync(new ImportError { BatchId = batch.Id, LineNumber = lineNo, Message = message });
            }
        }

        batch.TotalLines = total;
        batch.AppliedLines = applied;
        batch.RejectedLines = rejected;
        batch.FinishedUtc = DateTime.UtcNow;
        batch.Status = rejected == 0 ? "DONE" : "DONE_ERRORS";
        await _imports.SaveAsync();
        return await GetAsync(batch.Id);
    }
}
