using FluentValidation;

namespace dEngage.Loyalty.Api.Events;

public sealed class OrderCreatedRequestValidator : AbstractValidator<OrderCreatedRequest>
{
    public OrderCreatedRequestValidator()
    {
        RuleFor(x => x.ContactKey).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class OrderRefundedRequestValidator : AbstractValidator<OrderRefundedRequest>
{
    public OrderRefundedRequestValidator()
    {
        RuleFor(x => x.OriginalEventId).NotEmpty();
        RuleFor(x => x).Must(x => x.RefundRatio is not null || (x.Amount is not null && x.OriginalAmount is not null))
            .WithMessage("Either refundRatio, or both amount and originalAmount, are required.");
    }
}

public sealed class CashAddedRequestValidator : AbstractValidator<CashAddedRequest>
{
    public CashAddedRequestValidator()
    {
        RuleFor(x => x.ContactKey).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.AccountTypeId).NotEmpty();
    }
}

public sealed class CashSpentRequestValidator : AbstractValidator<CashSpentRequest>
{
    public CashSpentRequestValidator()
    {
        RuleFor(x => x.ContactKey).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.AccountTypeId).NotEmpty();
    }
}

public sealed class PointsRedeemRequestValidator : AbstractValidator<PointsRedeemRequest>
{
    public PointsRedeemRequestValidator()
    {
        RuleFor(x => x.ContactKey).NotEmpty();
        RuleFor(x => x.PointsAmount).GreaterThan(0);
        RuleFor(x => x.SourceAccountTypeId).NotEmpty();
    }
}

public sealed class PointsTransferRequestValidator : AbstractValidator<PointsTransferRequest>
{
    public PointsTransferRequestValidator()
    {
        RuleFor(x => x.ContactKey).NotEmpty();
        RuleFor(x => x.TargetContactKey).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.AccountTypeId).NotEmpty();
    }
}

public sealed class RewardPurchaseRequestValidator : AbstractValidator<RewardPurchaseRequest>
{
    public RewardPurchaseRequestValidator()
    {
        RuleFor(x => x.ContactKey).NotEmpty();
        RuleFor(x => x.RewardName).NotEmpty().When(x => x.RewardId is null)
            .WithMessage("Either rewardName or rewardId is required.");
    }
}

public sealed class GenericEventRequestValidator : AbstractValidator<GenericEventRequest>
{
    public GenericEventRequestValidator()
    {
        RuleFor(x => x.EventType).NotEmpty();
    }
}
