namespace dEngage.Loyalty.Shared;

// The "active" value shared by every entity's status vocabulary (Program, Tenant, AdminUser,
// Rule, StreakProgress all use the identical literal) — the one constant every other status
// class (ProgramStatus, TenantStatus, ...) defines its own Active as an alias of. AdminUser has
// no second status value today, so it references this directly rather than through its own class.
public static class EntityStatus
{
    public const string Active = "active";
}
