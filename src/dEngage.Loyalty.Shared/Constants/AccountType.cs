namespace dEngage.Loyalty.Shared;

public enum AccountType
{
    POINTS,
    // CR 2026-10-05 (D1): retired — no new STAMP wallet can be created. Kept because existing
    // account_types rows still carry it as customer history.
    STAMP,
    CASH
}
