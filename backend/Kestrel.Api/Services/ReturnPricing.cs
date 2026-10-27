using Kestrel.Api.Domain;

namespace Kestrel.Api.Services;

public class ReturnsOptions
{
    public const string SectionName = "Returns";

    public decimal GradeCRefundPercent { get; set; } = 85m;
    public int SerialWindowDays { get; set; } = 180;
    public int SerialMinRmas { get; set; } = 3;
    public int SerialMinOrders { get; set; } = 3;
    public decimal SerialMinRatePercent { get; set; } = 40m;
    public decimal ManagerApprovalThreshold { get; set; } = 150m;
}

// Pure pricing rules (BR-04, BR-05) so they can be unit tested without a database.
public static class ReturnPricing
{
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    // BR-04: amount paid for each line = line_total minus its prorated share of the order discount.
    // The last line (highest id) absorbs any rounding difference, so the lines add up to subtotal - discount.
    public static Dictionary<int, decimal> PaidPerLine(Order order)
    {
        var result = new Dictionary<int, decimal>();
        var lines = order.Lines.OrderBy(l => l.Id).ToList();
        if (lines.Count == 0) return result;

        var gross = lines.Sum(l => l.LineTotal);
        var discount = Math.Min(order.DiscountAmount, gross);
        var allocated = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            decimal share;
            if (i == lines.Count - 1) share = discount - allocated;
            else share = gross == 0 ? 0m : Round(discount * line.LineTotal / gross);
            allocated += share;
            result[line.Id] = line.LineTotal - share;
        }
        return result;
    }

    // The part of a line's paid amount that belongs to the first `units` units. Cumulative rounding means
    // returning every unit in any number of RMAs always adds up to exactly the paid amount.
    public static decimal PaidForFirstUnits(decimal paidForLine, int orderedQuantity, int units) =>
        orderedQuantity <= 0 ? 0m : units >= orderedQuantity ? paidForLine : Round(paidForLine * units / orderedQuantity);

    // Grades A, B and SCRAP refund 100% of the paid amount; C refunds GradeCRefundPercent.
    public static decimal GradePercent(string grade, decimal gradeCRefundPercent) =>
        grade == ReturnGrade.C ? gradeCRefundPercent : 100m;

    // BR-05: never refund more than what is still left of the amount paid.
    public static decimal CapToRemaining(decimal amount, decimal paid, decimal alreadyRefunded) =>
        Math.Max(0m, Math.Min(amount, paid - alreadyRefunded));

    public static decimal RefundForLine(decimal paidForLine, int orderedQuantity, int alreadyReturnedUnits,
        decimal alreadyRefunded, int quantity, string grade, decimal gradeCRefundPercent)
    {
        var paidShare = PaidForFirstUnits(paidForLine, orderedQuantity, alreadyReturnedUnits + quantity)
                        - PaidForFirstUnits(paidForLine, orderedQuantity, alreadyReturnedUnits);
        var amount = Round(paidShare * GradePercent(grade, gradeCRefundPercent) / 100m);
        return CapToRemaining(amount, paidForLine, alreadyRefunded);
    }
}
