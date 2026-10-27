using Kestrel.Api.Domain;
using Kestrel.Api.Services;

namespace Kestrel.Tests;

public class ReturnPricingTests
{
    private static Order OrderOf(decimal discount, params (int Id, decimal Total)[] lines) => new()
    {
        DiscountAmount = discount,
        Lines = lines.Select(l => new OrderLine { Id = l.Id, Quantity = 1, UnitPrice = l.Total, LineTotal = l.Total }).ToList()
    };

    [Fact]
    public void Without_a_discount_each_line_is_paid_in_full()
    {
        var paid = ReturnPricing.PaidPerLine(OrderOf(0m, (1, 40m), (2, 60m)));
        Assert.Equal(40m, paid[1]);
        Assert.Equal(60m, paid[2]);
    }

    [Fact]
    public void An_order_with_no_lines_has_nothing_paid()
    {
        Assert.Empty(ReturnPricing.PaidPerLine(OrderOf(5m)));
    }

    [Fact]
    public void The_discount_is_shared_between_lines_in_proportion_to_their_value()
    {
        // 10% off 100.00: the 25.00 line gets 2.50, the 75.00 line gets 7.50.
        var paid = ReturnPricing.PaidPerLine(OrderOf(10m, (1, 25m), (2, 75m)));
        Assert.Equal(22.50m, paid[1]);
        Assert.Equal(67.50m, paid[2]);
    }

    [Fact]
    public void The_last_line_absorbs_the_rounding_difference()
    {
        // 10.00 discount over three 10.00 lines: 3.33 + 3.33 + 3.34.
        var paid = ReturnPricing.PaidPerLine(OrderOf(10m, (1, 10m), (2, 10m), (3, 10m)));
        Assert.Equal(6.67m, paid[1]);
        Assert.Equal(6.67m, paid[2]);
        Assert.Equal(6.66m, paid[3]);
        Assert.Equal(20m, paid.Values.Sum());
    }

    [Fact]
    public void The_last_line_is_the_one_with_the_highest_id_whatever_the_list_order()
    {
        var order = OrderOf(10m, (3, 10m), (1, 10m), (2, 10m));
        var paid = ReturnPricing.PaidPerLine(order);
        Assert.Equal(6.66m, paid[3]);
        Assert.Equal(6.67m, paid[1]);
    }

    [Fact]
    public void Paid_amounts_always_add_up_to_subtotal_minus_discount()
    {
        var order = OrderOf(0.05m, (1, 10m), (2, 10m), (3, 10m));
        var paid = ReturnPricing.PaidPerLine(order);
        Assert.Equal(30m - 0.05m, paid.Values.Sum());

        var odd = OrderOf(13.37m, (1, 19.99m), (2, 7.77m), (3, 123.45m), (4, 0.99m));
        Assert.Equal(odd.Lines.Sum(l => l.LineTotal) - 13.37m, ReturnPricing.PaidPerLine(odd).Values.Sum());
    }

    [Fact]
    public void A_discount_larger_than_the_order_never_makes_a_line_negative()
    {
        var paid = ReturnPricing.PaidPerLine(OrderOf(500m, (1, 10m), (2, 20m)));
        Assert.All(paid.Values, v => Assert.True(v >= 0m));
        Assert.Equal(0m, paid.Values.Sum());
    }

    [Theory]
    [InlineData("A", 100)]
    [InlineData("B", 100)]
    [InlineData("SCRAP", 100)]
    public void Grades_A_B_and_scrap_refund_the_full_paid_amount(string grade, int expectedPercent)
    {
        Assert.Equal(expectedPercent, ReturnPricing.GradePercent(grade, 85m));
        Assert.Equal(40m, ReturnPricing.RefundForLine(40m, 1, 0, 0m, 1, grade, 85m));
    }

    [Fact]
    public void Grade_C_refunds_the_configured_percentage()
    {
        Assert.Equal(85m, ReturnPricing.GradePercent("C", 85m));
        Assert.Equal(34m, ReturnPricing.RefundForLine(40m, 1, 0, 0m, 1, "C", 85m));
        Assert.Equal(30m, ReturnPricing.RefundForLine(40m, 1, 0, 0m, 1, "C", 75m));
    }

    [Fact]
    public void Grade_C_rounds_to_two_decimals()
    {
        // 85% of 19.99 = 16.9915
        Assert.Equal(16.99m, ReturnPricing.RefundForLine(19.99m, 1, 0, 0m, 1, "C", 85m));
    }

    [Theory]
    [InlineData(50, 100, 0, 50)]    // asked for less than is left
    [InlineData(80, 100, 50, 50)]   // capped to what is left
    [InlineData(10, 100, 100, 0)]   // nothing left
    [InlineData(10, 100, 120, 0)]   // over-refunded already: never negative
    public void The_refund_is_capped_to_what_is_left_of_the_paid_amount(decimal amount, decimal paid, decimal already, decimal expected)
    {
        Assert.Equal(expected, ReturnPricing.CapToRemaining(amount, paid, already));
    }

    [Fact]
    public void RefundForLine_is_capped_by_what_was_already_refunded()
    {
        // 10.00 paid, 9.00 already refunded (e.g. rounding or earlier RMAs): at most 1.00 is left.
        Assert.Equal(1m, ReturnPricing.RefundForLine(10m, 2, 0, 9m, 1, "A", 85m));
    }

    [Theory]
    [InlineData("10.00", 3)]
    [InlineData("6.66", 3)]
    [InlineData("99.99", 7)]
    [InlineData("0.01", 3)]
    [InlineData("33.33", 6)]
    [InlineData("1.00", 3)]
    public void Returning_one_unit_at_a_time_adds_up_to_exactly_the_paid_amount(string paidText, int qty)
    {
        var paid = decimal.Parse(paidText, System.Globalization.CultureInfo.InvariantCulture);
        var refunded = 0m;
        for (var returned = 0; returned < qty; returned++)
            refunded += ReturnPricing.RefundForLine(paid, qty, returned, refunded, 1, "A", 85m);
        Assert.Equal(paid, refunded);
    }

    [Fact]
    public void Returning_in_uneven_chunks_adds_up_to_exactly_the_paid_amount()
    {
        const decimal paid = 77.77m;
        const int qty = 9;
        foreach (var chunks in new[] { new[] { 2, 3, 4 }, new[] { 4, 1, 4 }, new[] { 1, 7, 1 }, new[] { 9 } })
        {
            var returned = 0;
            var refunded = 0m;
            foreach (var c in chunks)
            {
                refunded += ReturnPricing.RefundForLine(paid, qty, returned, refunded, c, "B", 85m);
                returned += c;
            }
            Assert.Equal(paid, refunded);
        }
    }

    [Fact]
    public void A_discounted_order_refunds_exactly_what_was_paid_in_total()
    {
        var order = OrderOf(10m, (1, 10m), (2, 10m), (3, 10m));
        var paid = ReturnPricing.PaidPerLine(order);
        var total = order.Lines.Sum(l => ReturnPricing.RefundForLine(paid[l.Id], 1, 0, 0m, 1, "A", 85m));
        Assert.Equal(20m, total);
    }
}
