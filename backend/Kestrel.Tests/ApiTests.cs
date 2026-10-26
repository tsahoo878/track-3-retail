using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kestrel.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_endpoint_responds()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task There_are_twelve_stores()
    {
        var stores = await _client.GetFromJsonAsync<JsonElement>("/api/stores");
        Assert.Equal(12, stores.GetArrayLength());
    }

    [Fact]
    public async Task Product_list_is_paged()
    {
        var page = await _client.GetFromJsonAsync<JsonElement>("/api/products?page=1&pageSize=5");
        Assert.Equal(5, page.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Order_detail_includes_its_lines()
    {
        var order = await _client.GetFromJsonAsync<JsonElement>("/api/orders/1");
        Assert.True(order.GetProperty("lines").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Missing_category_returns_problem_details()
    {
        var response = await _client.GetAsync("/api/categories/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Invalid_product_is_rejected_with_400()
    {
        var response = await _client.PostAsJsonAsync("/api/products",
            new { sku = "", name = "", categoryId = 1, brand = "Kestrel", unitPrice = 0, costPrice = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Daily_sales_report_responds_for_a_date()
    {
        var response = await _client.GetAsync("/api/reports/daily-sales?date=2026-09-15");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_document_is_served()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
