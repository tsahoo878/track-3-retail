using Kestrel.Api.Services;

namespace Kestrel.Tests;

public class OrderPricingTests
{
    [Theory]
    [InlineData("NONE", 0)]
    [InlineData("SILVER", 5)]
    [InlineData("GOLD", 10)]
    public void Discount_percent_follows_loyalty_tier(string tier, int expected)
    {
        Assert.Equal(expected, OrderPricing.DiscountPercent(tier));
    }

    [Fact]
    public void Gold_customer_gets_ten_percent_off()
    {
        var total = OrderPricing.Total(105m, "GOLD");
        Assert.Equal(94.50m, total);
    }

    [Fact]
    public void Discount_is_rounded_to_pence()
    {
        Assert.Equal(1.45m, OrderPricing.Discount(28.99m, "SILVER"));
    }
}
