namespace BallBank.Domain.Treasury;

/// <summary>
/// How a member says they paid. BallBank records the claim and the treasurer's confirmation;
/// the money itself moves outside the system (see ADR-0002).
/// </summary>
public enum PaymentRail
{
    Cash,
    Venmo,
    Zelle,
    PayPal,
    CashApp,
    Check,
    Other,
}
