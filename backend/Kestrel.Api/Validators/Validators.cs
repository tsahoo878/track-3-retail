using FluentValidation;
using Kestrel.Api.Dtos;

namespace Kestrel.Api.Validators;

public class SaveCategoryValidator : AbstractValidator<SaveCategoryDto>
{
    public SaveCategoryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Department).NotEmpty().MaximumLength(40);
    }
}

public class SaveProductValidator : AbstractValidator<SaveProductDto>
{
    public SaveProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Brand).NotEmpty().MaximumLength(60);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThan(0);
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0);
    }
}

public class SaveStoreValidator : AbstractValidator<SaveStoreDto>
{
    public SaveStoreValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Region).NotEmpty().MaximumLength(30);
        RuleFor(x => x.City).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Timezone).NotEmpty().MaximumLength(40);
    }
}

public class StockAdjustValidator : AbstractValidator<StockAdjustDto>
{
    public StockAdjustValidator()
    {
        RuleFor(x => x.StoreId).GreaterThan(0);
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Delta).NotEqual(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
    }
}

public class SetStockValidator : AbstractValidator<SetStockDto>
{
    public SetStockValidator()
    {
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
    }
}

public class SaveCustomerValidator : AbstractValidator<SaveCustomerDto>
{
    private static readonly string[] Tiers = { "NONE", "SILVER", "GOLD" };

    public SaveCustomerValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.LoyaltyTier).Must(t => Tiers.Contains(t)).WithMessage("LoyaltyTier must be NONE, SILVER or GOLD.");
        RuleFor(x => x.Region).NotEmpty().MaximumLength(30);
    }
}

public class CreateOrderValidator : AbstractValidator<CreateOrderDto>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.StoreId).GreaterThan(0);
        RuleFor(x => x.Channel).Must(c => c == "STORE" || c == "WEB").WithMessage("Channel must be STORE or WEB.");
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(y => y.ProductId).GreaterThan(0);
            l.RuleFor(y => y.Quantity).InclusiveBetween(1, 20);
        });
    }
}

public class ChangeStatusValidator : AbstractValidator<ChangeStatusDto>
{
    public ChangeStatusValidator() => RuleFor(x => x.Status).NotEmpty().MaximumLength(12);
}

public class AddNoteValidator : AbstractValidator<AddNoteDto>
{
    public AddNoteValidator()
    {
        RuleFor(x => x.AuthorUserId).GreaterThan(0);
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
    }
}

public class CreatePaymentValidator : AbstractValidator<CreatePaymentDto>
{
    private static readonly string[] Methods = { "CARD", "CASH", "GIFTCARD", "PAYPAL" };

    public CreatePaymentValidator()
    {
        RuleFor(x => x.Method).Must(m => Methods.Contains(m)).WithMessage("Method must be CARD, CASH, GIFTCARD or PAYPAL.");
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.CardLast4).Matches("^[0-9]{4}$").When(x => x.CardLast4 != null);
        RuleFor(x => x.ProviderRef).MaximumLength(40);
    }
}
