using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

public class Program : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Status { get; set; } = ProgramStatus.Active;
    public DateTime CreatedAt { get; set; }

    // 1.3.CL item 8: Draft → Published lifecycle. The engine only runs Published + Active.
    public string PublicationStatus { get; set; } = ProgramPublicationStatus.Draft;
    // Set by any change to the program or its nested config after it was published; cleared by
    // the next Publish.
    public bool HasUnpublishedChanges { get; set; }
    // The ProgramPublication ConfigVersion number of the latest publish (null until published).
    public int? PublishedVersion { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }

    // CR 2026-09-30 (A5): unique per tenant, no '_' (it separates slug from reward name), locked
    // once the program is first published. Prefixes the program's reward names
    // ({slug}_{suffix}) so a reward_name can only ever resolve to one program's reward.
    // Unrelated to the tenant slug in API routes. ProgramsAppService always sets it; this random
    // default only covers code that creates a program without one (fixtures, tools), mirroring
    // the column's DB default that keeps raw-SQL seed inserts working.
    public string Slug { get; set; } = "p-" + Guid.NewGuid().ToString("N")[..10];

    // Deprecated by 1.3.CL item 1 — superseded by AccountType.IsTierQualifying. Kept (and no
    // longer written) for one release so the column can be dropped in a follow-up CR.
    public Guid? QualifyingAccountTypeId { get; set; }

    // Deprecated by 1.3.CL item 1 — superseded by the POINTS account type config key
    // `warning_days`. Kept (and no longer read or written) for one release.
    public int? WarningDays { get; set; }

    public AccountType? QualifyingAccountType { get; set; }
    public ICollection<AccountType> AccountTypes { get; set; } = [];
    public ICollection<Rule> Rules { get; set; } = [];
}
