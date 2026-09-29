# Scope Change Impact Analysis: Program & Account Type changes (1.3.CL)

**Status:** Approved 2026-09-28 by Product Owner + Architect (confirmed by the developer) — **implemented and verified** (see §10). No git in this environment, so there is no branch; the file list in §10 is the review record.
**Date:** 2026-09-28
**Author:** Claude Code, at the request of danishaliqau@gmail.com
**Source:** Change log **1.3.CL — program account type changes** (items 1–9)
**Affects:**
- `docs/SCOPE_BASELINE.md`: AccountTypes, ConfigVersions, Programs, Rules and Tiers backend rows; the "Known limitation" note; the RuleEngine winner-selection and tier-evaluation rows; the `programs`, `programs/account-types` and `programs/rules` frontend rows. It also needs a new row for Program publishing.
- `docs/SOW.md` §2.2 (Programs, Account Types, Rules, Configuration history), §2.3 (points expiry warnings) and §3 (the `programs.status` limitation).
- `scripts/loyalty_schema_reference.md`, which must be regenerated.

This document is step 2 of the scope-change process defined in `CLAUDE.md`. It analyses how the nine change-log items affect the `docs/SCOPE_BASELINE.md` baseline and the as-built code. §1–§8 are the analysis as approved; §10 records what was implemented, where it deviates, and how it was verified.

## Approval record

Approved 2026-09-28. D1–D8 below were approved as proposed. The §5 open decisions were resolved with their recommended defaults, **except §5 f**: per-edit Program/Tier ConfigVersion rows are **kept** alongside the publish snapshots (so D2's "per-edit rows stop" does not apply). See §10.1 for the deviations this caused.

| # | Proposed decision |
|---|---|
| D1 | **Publish is a version stamp, not a staging area.**<br>• Edits apply live, as they do today.<br>• A new program starts as **Draft** and the engine ignores it.<br>• Once a program is published, any edit to it or to its nested entities marks it as having *unpublished changes*.<br>• **Publish** writes a version. |
| D2 | **Each publish writes one ConfigVersion snapshot covering the whole program**<br>• `EntityType = "Program"`, `ChangeType = "published"`.<br>• The snapshot contains the program plus its account types, tiers, rewards and rules. Each rule is included with its `id` and `currentVersion`.<br>• The per-edit Program and Tier ConfigVersion rows stop. |
| D3 | **Exclusivity group and Stack mode are retired.**<br>• They are hidden in the portal and the API rejects them. The DB columns stay.<br>• Non-stackable rules compete per target account type, which is the engine's existing fallback.<br>• All stackable rules become Additive. Existing Multiplier rules are migrated to Additive. |
| D4 | **The Decimal places setting becomes real.** Spend-rule earn rounds down to the target POINTS account type's `decimals` instead of always rounding to a whole number. |
| D5 | **The tier-qualifying account becomes a flag on a POINTS account type**, with at most one per program. It is backfilled from `programs.qualifying_account_type_id`. |
| D6 | **Points expiry warning days move to POINTS account type config** (`config.warning_days`).<br>• They are backfilled from `programs.warning_days`.<br>• The program column is kept (deprecated) for one release and dropped in a later CR. |
| D7 | **The CASH currency is chosen from a whitelist.**<br>• The list is SAR (default), AED, KWD, QAR, BHD, OMR, USD, EUR, GBP and TRY.<br>• It is a server-side constant, and the portal copies it. |
| D8 | **The Active/Inactive toggle works only after publish.**<br>• New programs start as Draft + inactive, with the toggle disabled.<br>• The engine runs only programs that are **published and active**.<br>• Toggling does not create a version. |

## 0. Change-log items mapped to the baseline

| CL item | Summary | Baseline row(s) touched | Classification |
|---|---|---|---|
| 1a | Points expiry warning (days before) moves from Program to POINTS Account Type | No row of its own. Only in SOW §2.3 ("advance 'expiring soon' warnings"). Also touches the AccountTypes and Programs rows | Behaviour move, **breaking API change** |
| 1b | Tier qualifying account moves from Program to Account Type | AccountTypes, Programs ("qualifying-account lock"), Tiers, RuleEngine tier evaluation | Behaviour move, **breaking API change** |
| 2 | Decimal places information icon, and decimals actually used in Spend-rule calculation | AccountTypes ("precision"), Rules, RuleEngine rule-type handlers | **Calculation change** |
| 3 | CASH account type: remove Expiration (days) | AccountTypes ("CASH (currency, precision, expiry)"), frontend `programs/account-types` | Validation and UI change |
| 4 | CASH account type: currency dropdown, default SAR | AccountTypes, frontend `programs/account-types` | Validation and UI change |
| 5 | Rules: remove Exclusivity group and Stack mode | Rules (CR-06), RuleEngine winner selection, frontend `programs/rules` ("exclusivity/stacking") | **Engine behaviour change** |
| 6 | Rules list: "Stackable" column | Frontend `programs/rules` | UI only |
| 7 | Program: Active/Inactive toggle | Programs, frontend `programs` | UI only |
| 8 | Program: Draft → Publish lifecycle | Programs, the "Known limitation" note, frontend `programs`, plus **a new row** | **New capability** |
| 9 | Version is written on publish and covers nested entities, using the existing `ConfigVersion` table | ConfigVersions ("Append-only audit trail for Program/Tier mutations"), SOW §2.2 "Configuration history" | **Changes what the audit trail records** |

## 1. Why this change is being proposed

- **Items 1a and 1b** move settings to the wallet (account type) they describe. Expiry warning days only mean something together with that wallet's `expiration_days`. The tier-qualifying setting picks out one wallet. Keeping either on the Program makes it possible to set values that don't fit: a warning that is longer than the wallet's expiry, or a qualifying account from another program or of the CASH type.
- **Item 2.** Admins set "Decimal places" today, but nothing in the backend reads it. Spend rules always round down to a whole number of points. The setting should either do something or say clearly what it does. The requester chose to make it do something.
- **Items 3 and 4.** CASH balances represent real credit, so they must never expire inside the loyalty system. Typing the currency as free text allows invalid codes.
- **Items 5 and 6.** The CR-06 stacking controls (named exclusivity groups and multiplier stacking) are more than operators need right now. The only question they need answered is "is this rule stackable?", and they need to see the answer in the rules list.
- **Items 7, 8 and 9.**
  - Operators need to build a program before it goes live (Draft).
  - They need an explicit release step (Publish).
  - They need one version record for each release, covering everything that makes up the program, not one audit row per individual field edit.

## 2. Current state (as-built)

### 2.1 Points expiry warning days
- The setting is `Program.WarningDays` (`src/dEngage.Loyalty.Schema/Entities/Program.cs:16-17`), stored in the `programs.warning_days` column (`ProgramConfiguration.cs:21`). It was added in migration `20260706070750_ProgramWarningDays`.
- The only production reader is `src/dEngage.Loyalty.Ledger/PointsExpiringDetectorJob.cs:13-27`. That query joins each POINTS account type's `config->>'expiration_days'` to its program's `warning_days`. At `:41` it skips account types unless `0 < warning_days < expiration_days`.
- The job runs nightly at 01:00 UTC from `Consumer/PointsExpiringDetectorWorker.cs`.
- Existing gaps:
  - A PATCH cannot set it back to null, because `ProgramsAppService.cs:90` treats null as "unchanged".
  - The portal input uses `min="0"` while the server requires `> 0` (`program-overview.page.ts:91-92`, `ProgramsValidators.cs:13,25`).

### 2.2 Tier qualifying account
- The setting is `Program.QualifyingAccountTypeId`: an optional FK to `account_types` (`Program.cs:14`, `ProgramConfiguration.cs:20-26`).
- It is locked while the program is active (`ProgramsAppService.cs:78-84`, `program_running`).
- These engine components read it:

  | Reader | Location |
  |---|---|
  | `TierContextLoader` | `RuleEngine/Processing/TierContextLoader.cs:19-35` |
  | `RuleEngine` | `RuleEngine/RuleEngine.cs:75-77, 86-88` |
  | `TierEvaluationService` | `TierEvaluationService.cs:18,38,47` |
  | `TierDowngradeJob` (raw SQL) | `TierDowngradeJob.cs:48,98` |

- **Existing gap:** nothing in the API or the DB checks that the chosen account type belongs to the same program or is of type POINTS. Only the portal dropdown filters the choices.

### 2.3 Account type configuration
- `AccountType` has `Type` (POINTS, CASH or STAMP, stored as a string) and a jsonb `Config` field. `decimals`, `expiration_days`, `currency` and `redemption` all live inside `Config`, not in their own columns (`Entities/AccountType.cs`, `AccountTypeConfiguration.cs:20`).
- Per-type validators are in `Api/AccountTypes/AccountTypeConfigValidators.cs`:
  - **POINTS** requires `decimals`.
  - **CASH** requires `currency` (any string) and `decimals`.
  - **`expiration_days` is never validated.**
- `Type` cannot be changed, and there is no delete. Edits to account types write **no** ConfigVersion rows.
- Portal: `web/src/app/features/programs/account-types/account-type-form.dialog.ts`.
  - The POINTS block is at `:46-88`.
  - The CASH block (`:90-105`) has a free-text currency field (placeholder SAR, max 3 characters) and an Expiration (days) field.
- **CASH `expiration_days` is stored but never used.** `PointsExpirationJob.cs:20-31` processes `type = 'POINTS'` only.

### 2.4 Decimal places in calculation
- **The backend never reads `decimals`.** The rounding that actually happens is:

  | Where | What it does |
  |---|---|
  | `RuleEngine/Calculation/SpendRuleHandler.cs:13` | `Math.Floor(evt.Amount * Factor)`, always a whole number |
  | `Processing/WinnerSelector.cs:191-197` `ApplyRounding` | Fixed 2 decimals. Applies **only** when a rule explicitly sets `Configuration.Rounding` |
  | `Consumer/Handlers/PointsRedeemHandler.cs:41` | Redemption rounds to a fixed 2 decimals |

- On the portal side, `web/src/app/shared/money/decimal.ts` (`multiplyFloor`) already accepts a `decimalPlaces` argument.

### 2.5 Exclusivity group and Stack mode (CR-06)
- **Where the fields are defined:**
  - Fields: `Rule.cs:29-37` and `RuleVersion.cs:24-26`.
  - Allowed stack-mode values (`Additive`, `Multiplier`): `Shared/Constants/RuleStackMode.cs`.
  - Migration `20260921111821_StackingResolutionCr06` added the columns and backfilled `exclusivity_group` with the account type's name for non-stackable rules.
- **Engine** (`WinnerSelector.cs:68-180`):
  - Non-stackable rules are grouped by `ExclusivityGroup ?? TargetAccountTypeId`. In each group, the matching rule with the highest priority wins.
  - Every matching stackable rule applies: multipliers scale the winners in the same wallet, and additive rules post their own amount.
- **API** (`RulesValidators.cs:91-98, 126-130`; `RulesAppService.cs:114-118, 201-207`):
  - `ExclusivityGroup` is required when a rule is not stackable.
  - A non-stackable rule is always stored with `StackMode = Additive`.
- **Portal** (`rule-form.page.ts:175-203`): a "Stackable" checkbox, plus a Stack mode select (shown when stackable) or an Exclusivity group input (shown when not stackable).
- **Tests**: `IntegrationTests/Engine/StackingResolutionWorkedExampleTests.cs` depends on both named groups and a Multiplier rule.
- **The API already returns `stackable`** on every list response and supports a `?stackable=` filter (`RulesAppService.cs:19,56`, `RulesModule.cs:29`). The rules list page doesn't show it yet (`rules-list.page.ts:103-110`).

### 2.6 Rule versioning
- `Api/Rules/RuleVersioningService.cs:26-53` (`ArchiveAndBumpAsync`) copies the rule's pre-edit state into `rule_versions` and bumps `Rule.CurrentVersion`.
- Ledger postings reference `ruleId + ruleVersion`.
- Changing a status, approving or deleting a rule does not create a version.

### 2.7 Program status
- `programs.status` can be `active`, `inactive` or `deleted` (`Shared/Constants/ProgramStatus.cs`). **There is no draft state.**
- The portal shows the status as a radio group (`program-overview.page.ts:59-69`). The create dialog always sends `active` (`create-program.dialog.ts:63`).
- **Correction to SOW §3 and the baseline's "Known limitation" note.** Status *is* enforced on the main evaluation path:
  - `Consumer/CampaignEvaluationService.cs:24-26` evaluates only active programs.
  - `Consumer/RuleSyncService.cs:138-141` and `RuleEngine/Processing/BirthdayBonusJob.cs:55` also filter on active.
- The per-event handlers (redeem, transfer, reward purchase) and the ledger jobs **do not** check status.

### 2.8 ConfigVersion
- **Storage.** The table is `config_versions`. It holds a full jsonb snapshot, and the version number is `MAX+1` per `(tenant, entityType, entityId)` (`Schema/Entities/ConfigVersion.cs`, `ConfigVersionConfiguration.cs`).
- **How rows are written.** `Api/ConfigVersions/ConfigVersionService.cs` `StageAsync` adds a row to the caller's unit of work, and the caller commits it.
- **Only Programs and Tiers write rows** (`ProgramsAppService.cs:64,92,106`; `TiersAppService.cs:77,115,161`). Tier reorder writes none.
  - The entity's own comment says AccountType, Rule, Reward and StreakCampaign are covered too. That is not true today.
- **Snapshot content.** The snapshot is the *tracked EF entity* passed to `JsonSerializer.Serialize`. Any navigation properties that happen to be loaded end up in the snapshot.
- **API and portal.**
  - The API is read-only (`ConfigVersionsModule.cs`). Rollback is explicitly a "fast-follow".
  - The portal history page lists only `entityType=Program`, as raw JSON (`features/programs/history/`).
- **What the engine reads.** The engine **never reads ConfigVersion**. It reads live tables:
  - rules and streak campaigns through the Redis caches (`RuleCacheService`, `CampaignConfigCacheService`)
  - the program, tiers, rewards and account types uncached, on every event

### 2.9 Portal i18n
- `web/public/i18n/en.json` and `tr.json` hold only nav, action and common keys.
- Every label on the program, account type and rule screens is hard-coded English.

## 3. Proposed direction

### 3.1 Item 1a: Warning days move to POINTS account type
- **Schema:** no column change. A new optional POINTS config key, `warning_days`.
- **Validation** (`AccountTypeConfigValidators.cs`, POINTS):
  - `warning_days` is optional and must be an integer.
  - It requires `expiration_days`, and must satisfy `0 < warning_days < expiration_days`. This is the same rule the job already enforces.
  - `expiration_days` gets the validation it lacks: an optional integer greater than 0.
- **Backfill migration:** copy `programs.warning_days` into `config.warning_days` for every POINTS account type in that program that has `expiration_days`, and only where `warning_days < expiration_days`.
- **Job:** `PointsExpiringDetectorJob` reads `at.config->>'warning_days'` and no longer takes `warning_days` from `programs`. It still joins `programs` only if that is needed for D8.
- **Programs API:**
  - `CreateProgramRequest` and `UpdateProgramRequest` drop `WarningDays`. A request that still sends it gets 400 `field_moved_to_account_type`, so it isn't silently ignored.
  - `ProgramResponse` keeps the field (deprecated, always null) for one release.
- **Portal:**
  - Remove the input at `program-overview.page.ts:91-92`.
  - Add "Expiry warning (days before)" to the POINTS block of `account-type-form.dialog.ts`, enabled only when Expiration (days) is set.
- **Side benefit:** because it now lives in config, the warning can be cleared again. This fixes the PATCH-null gap described in §2.1.

### 3.2 Item 1b: Tier qualifying flag on account type
- **Schema:**
  - New column `account_types.is_tier_qualifying boolean not null default false`.
  - A unique partial index on `(tenant_id, program_id) WHERE is_tier_qualifying` guarantees at most one per program.
- **Backfill:** set the flag where `id = programs.qualifying_account_type_id`.
- **Validation:** the flag is allowed only when `Type = POINTS`. This closes the gap in §2.2.
- **Lock:** move the `program_running` lock from `ProgramsAppService.cs:78-84` to `AccountTypesAppService`. The flag cannot be set or cleared on any account type while the program is active.
  - *Open question:* should the lock also apply to published programs that are inactive? (§5 j)
- **Engine:** these readers resolve the qualifying account type from the flag. `ITierEvaluationService` keeps its signature, since it still receives an id.

  | Reader | Change |
  |---|---|
  | `TierContextLoader` | Resolve from the flag |
  | `RuleEngine.cs:75-88` | Resolve from the flag |
  | `TierDowngradeJob` | Raw SQL join changes from `p.qualifying_account_type_id` to `at.is_tier_qualifying` |

- **Programs API:**
  - Create and Update requests drop `QualifyingAccountTypeId`. A request that still sends it gets 400 `field_moved_to_account_type`.
  - `ProgramResponse` keeps it as a read-only value derived from the flag, for one release.
  - The AccountType request and response DTOs gain `isTierQualifying`.
- **Deprecation:** `programs.qualifying_account_type_id` stays (FK kept) but nothing writes it. It is dropped together with `warning_days` in a later CR.
- **Portal:**
  - Remove the select at `program-overview.page.ts:75-88`.
  - Add a "Tier qualifying account" toggle to the POINTS account type form, disabled while the program is active.
  - The account types list shows a "Tier qualifying" badge.

### 3.3 Item 2: Decimal places information icon and real rounding
- **Portal:**
  - Add an info icon and tooltip next to "Decimal places" in the POINTS block: *"Points earned by Spending rules are rounded down to this many decimal places."* It needs an accessible label and must not rely on hover alone.
  - Update the Event Simulator preview, if it shows earn amounts, to use `multiplyFloor(..., decimals)` so the portal and engine agree.
- **Engine:**
  - `RuleCacheService.LoadFromDbAsync` loads the target account type's `decimals` into a new `CachedRule.TargetDecimals` field (default 0 when it's missing).
  - `SpendRuleHandler` floors to that precision: `Math.Floor(amount × factor × 10^d) / 10^d`, with `d` clamped to 0–4.
  - The ledger column is `numeric(20,4)`, so 4 is the highest precision it can store.
  - Everything stays `decimal`; no `double` is involved.
- **Contract impact:**
  - Idempotency keys are unaffected.
  - Past postings are not recalculated.
  - The cache must be flushed when an account type's `decimals` changes. Today `AccountTypesAppService` doesn't touch the rule cache.
- **Open questions for §5:**
  - (d) If a rule also sets `Configuration.Rounding`, which setting wins?
  - (g) Should other rule types and redemption (`PointsRedeemHandler` rounds to 2 decimals) also use `decimals`?

### 3.4 Item 3: Remove Expiration (days) for CASH
- **Portal:** remove the input from the CASH block, and remove `expiration_days` from `CashConfig` in `account-type.model.ts`.
- **Validation:** the CASH validator rejects `expiration_days` with 400.
- **Data migration:** remove the key from existing CASH configs (`config = config - 'expiration_days' WHERE type = 'CASH'`).
- **Runtime effect: none.** No job reads it. `CustomersAppService.ReadExpirationDays` will return null for CASH wallets. That is correct, but the customer 360 view will visibly change.

### 3.5 Item 4: CASH currency dropdown
- **Shared list:** add `src/dEngage.Loyalty.Shared/Constants/SupportedCurrencies.cs` (D7 list, `Default = "SAR"`). The CASH validator checks against it.
- **Portal:** a select control defaulting to SAR, with the list copied in `account-type.model.ts`. Following the `RewardTypeRegistry` pattern, it is a static copy; there is no new endpoint.
- **Immutability:** once created, the currency can't be changed, just like `Type` (§5 e). Changing the currency of a wallet that already holds a balance would reinterpret real credit.
- **Existing data:** the migration **reports** CASH account types whose currency isn't on the list and **doesn't change them**. They can still be read, and they are only re-validated when edited.
- **Out of scope:** reward `typeConfig.currency` and the condition-tree `currency` field keep accepting free text.

### 3.6 Item 5: Retire Exclusivity group and Stack mode
- **Portal:** remove the Stack mode and Exclusivity group controls, keeping only "Stackable with other rules" (`rule-form.page.ts:175-203, 531-533, 583-585, 693-694, 740-742, 888-890`). Update the priority hint at `:128`.
- **API:**
  - The DTO fields stay, so the response shape doesn't change. Responses always return `exclusivityGroup: null` and `stackMode: "Additive"`.
  - The validators become **stricter**: a non-null `exclusivityGroup` or a `stackMode` other than `Additive`/null returns 400 `stacking_field_retired`.
  - Remove the "ExclusivityGroup required when not stackable" rule, since the engine fallback now covers that case.
- **Data migration** (applies to rules that have a group or a Multiplier mode):
  - For each affected rule, **first archive a `rule_versions` row and bump `current_version`**, just as `ArchiveAndBumpAsync` does. This keeps the "an edit always creates a new version" contract, so any later posting references a version that matches the rule's actual behaviour.
  - Then set `exclusivity_group = NULL` and change `stack_mode` from `Multiplier` to `Additive`.
  - The Redis rule cache must be flushed after deployment, or the Consumer must reload in full.
- **Engine:**
  - No code change is needed. `WinnerSelector.cs:69` already falls back to grouping by target account type.
  - The Multiplier branch becomes unused code; a later clean-up CR removes it.
- **Behaviour change:**
  - Rules that still carry the CR-06 backfilled group (the account type name) behave **exactly as before**.
  - Rules whose admin set a **custom** group name, or that use Multiplier, change behaviour:
    - separately named groups on the same wallet now compete with each other
    - multipliers now add their computed amount instead of scaling the winner
  - A pre-migration report must list these rules per tenant (see §6).
- **Tests:**
  - Replace `StackingResolutionWorkedExampleTests` with a new worked example that follows the post-1.3.CL rules. It is replaced with the Architect's sign-off, not deleted to make the build pass.
  - Add `WinnerSelectorTests` for the fallback grouping.

### 3.7 Item 6: "Stackable" column in the rules list
- `rules-list.page.ts`: add a `stackable` entry to `columns` (`:103-110`) and a matching `<td>` in the row template (`:50-75`).
- The cell shows a status pill reading **"Stackable"** or **"Exclusive"**, using both text and tone, never colour alone.
- Optionally add a Stackable filter that uses the existing `?stackable=` parameter. There is no backend change.

### 3.8 Item 7: Active/Inactive toggle
- Replace the radio group at `program-overview.page.ts:59-69` with a toggle. It sends `PATCH status`, and optimistic UI with rollback is acceptable here.
- The toggle is **disabled while the program is Draft**. Its tooltip reads "Publish the program first".
- Server: `ProgramsAppService.UpdateAsync` returns 409 `program_not_published` when a draft program is set to `active`.
- Toggling does **not** create a version (D8), and does **not** mark the program as having unpublished changes.
- The programs list page can show the same state as a status pill; that page doesn't need a toggle.

### 3.9 Item 8: Draft → Publish lifecycle
- **Schema** (new columns on `programs`):

  | Column | Type | Default / notes |
  |---|---|---|
  | `publication_status` | varchar(20), not null | `'draft'`; values `draft` / `published` |
  | `has_unpublished_changes` | bool, not null | `false` |
  | `published_version` | int | null |
  | `published_at` | timestamptz | null, UTC |
  | `published_by` | varchar(255) | null |

  New constant `Shared/Constants/ProgramPublicationStatus.cs`.
- **Backfill:** every existing non-deleted program becomes `published`, with `has_unpublished_changes = false` and no snapshot. Its first new version is written the next time it is published.
- **API:**
  - New route `POST /api/v1/tenants/{tenantId}/programs/{programId}/publish`. It is tenant-scoped and uses `RequireProgramAsync`, like the other program routes.
  - It returns the updated `ProgramResponse`. `ProgramResponse` adds `publicationStatus`, `hasUnpublishedChanges`, `publishedVersion`, `publishedAt` and `publishedBy`.
  - Publishing a program that is already published and has no unpublished changes returns 409 `nothing_to_publish`.
- **Create:** new programs are always `draft` + `inactive`. **Breaking:** `CreateProgramRequest.Status` is dropped, and a request that sends it gets 400. The portal create dialog stops sending it.
- **Unpublished-changes flag.** Every mutation of a nested entity sets `has_unpublished_changes = true` on the owning program, in the same `SaveChanges`:
  - Program PATCH, except status-only changes
  - AccountTypes create/update
  - Tiers create/update/delete/reorder
  - Rewards create/update/delete
  - Rules create/update/status/approve/delete
  - CardBuckets
  - StreakCampaigns (see §5 a)

  The change reuses the existing AppService pattern, with a small shared helper in `Api.Framework` or the Programs repository. It adds no new layer.
- **Engine:** the engine runs a program only when it is `published` **and** `active`. The added filter goes in:
  - `CampaignEvaluationService.cs:24-26`
  - `RuleSyncService.cs:138-141`
  - `BirthdayBonusJob.cs:55`
- **Draft programs:** they cannot be activated (see §3.8). *(Correction during implementation: this line originally also said drafts "cannot be deleted" — a draft is always inactive, so the existing "delete only while inactive" rule already allows deleting it, which is the sensible behaviour; see §10.1.)*
- **Portal:**
  - The program overview header shows a Draft/Published badge.
  - It shows an "Unpublished changes" indicator when there are any.
  - It has a **Publish** button with a confirmation dialog.

### 3.10 Item 9: One version per publish in `ConfigVersion`
- **Writing the version.** On publish, build an aggregate snapshot **from DTO projections, not tracked EF entities**, so navigation properties don't leak into it (§2.8). The snapshot contains:
  - `program`: its fields, excluding runtime counters
  - `accountTypes[]`: including config and `isTierQualifying`
  - `tiers[]`: in order
  - `rewards[]`
  - `rules[]`: each with `id`, `currentVersion`, `status` and the full rule DTO
  - `cardBuckets[]` and `streakCampaigns[]`, if §5 a is approved
- **Transaction.** `IConfigVersionService.StageAsync(db, tenantId, "Program", programId, snapshot, ConfigChangeType.Published, principal, summary, ct)` is called with a new `ConfigChangeType.Published = "published"`. It is committed in the **same** `SaveChanges` that sets `publication_status`, `published_version` (= the new VersionNumber), `published_at`, `published_by` and `has_unpublished_changes = false`.
- **Per-edit rows stop.** Remove the per-edit `StageAsync` calls in `ProgramsAppService` (create, update, delete) and `TiersAppService` (create, update, delete). Existing rows stay.
  - *Trade-off:* edits between publishes are no longer audited one by one (§5 f).
  - Per-edit rule history is still kept in `rule_versions`, which postings depend on.
- **Portal history page:** each row reads "Published vN · by · at". The version dialog shows the snapshot in sections (Program, Account types, Tiers, Rewards, Rules) instead of raw JSON. Older per-edit rows still display, labelled with their change type.

## 4. Blast radius

- **Schema** (`src/dEngage.Loyalty.Schema`): three new migrations, generated with `dotnet ef migrations add … -p src/dEngage.Loyalty.Schema`, never written by hand.
  1. `AccountTypeConfigCl13`:
     - `is_tier_qualifying` and its unique partial index
     - backfill of `warning_days` and the tier flag
     - stripping CASH `expiration_days`
     - report of currencies that aren't on the whitelist
  2. `RuleStackingRetirementCl13`: archive to `rule_versions` and bump the version for affected rules, then null the group and set Multiplier to Additive.
  3. `ProgramPublicationCl13`: the five `programs` columns and their backfill.
  - Entity and configuration changes: `AccountType`, `AccountTypeConfiguration`, `Program`, `ProgramConfiguration`.
  - Regenerate `scripts/loyalty_schema_reference.md`.
- **API** (`src/dEngage.Loyalty.Api`):

  | Area | Files |
  |---|---|
  | `Programs/` | Dtos, Validators, AppService, Module (new publish route) |
  | `AccountTypes/` | Dtos, `AccountTypeConfigValidators`, AppService (tier lock, unpublished-changes flag, rule cache flush on decimals change) |
  | `Rules/` | Validators, AppService (unpublished-changes flag) |
  | `Tiers/`, `Rewards/`, `CardBuckets/`, `StreakCampaigns/` | AppServices (unpublished-changes flag; Tiers also stops per-edit ConfigVersion rows) |
  | `ConfigVersions/` | Unchanged apart from the new change type |

  Plus `Shared/Constants`: `SupportedCurrencies`, `ProgramPublicationStatus` and `ConfigChangeType.Published`.
- **Engine, Consumer, Ledger:**

  | Component | Change |
  |---|---|
  | `RuleEngine` | `SpendRuleHandler`; `CachedRule` and `RuleCacheService` (decimals); `TierContextLoader`; `RuleEngine.cs`; `TierDowngradeJob` (tier flag); `BirthdayBonusJob` (publish filter) |
  | `Consumer` | `CampaignEvaluationService` and `RuleSyncService` (publish filter) |
  | `Ledger` | `PointsExpiringDetectorJob` (warning days from config) |
  | Event payloads | No change; `loyalty.points.expiring` keeps its snake_case shape |
- **Breaking API changes** (API clients must be told):
  1. Program Create/Update no longer accept `warningDays` or `qualifyingAccountTypeId` (400).
  2. Program Create no longer accepts `status`. New programs are always draft + inactive, **so programs created through the API stop running until they're published.**
  3. Rules no longer accept `exclusivityGroup` or a non-Additive `stackMode` (400).
  4. CASH account types reject `expiration_days`, currencies not on the whitelist, and currency changes.
  5. `PATCH status=active` on a draft program returns 409.

  Response shapes only gain fields; none are removed.
- **Portal** (`web/src/app/features/programs/`):

  | Area | Files |
  |---|---|
  | Program | `program.model.ts`, `program-overview.page.ts`, `create-program.dialog.ts`, `programs-list.page.ts`, `programs.service.ts` (publish) |
  | Account types | `account-types/account-type.model.ts`, `account-type-form.dialog.ts`, `account-types-list.page.ts` |
  | Rules | `rules/rule.model.ts`, `rule-form.page.ts`, `rules-list.page.ts` |
  | History | `history/` page and version dialog |
  | Shared | `shared/money/decimal.ts` (simulator parity), if the preview shows earn amounts |
  | i18n | New keys for **new** strings only, in `en.json` and `tr.json`. Existing hard-coded strings are left alone; no drive-by migration |
- **Tests:**
  - `Engine.Tests`: SpendRuleHandler decimals, WinnerSelector fallback grouping, tier resolution from the flag.
  - `IntegrationTests`:
    - the publish flow and snapshot contents
    - the unpublished-changes flag for each nested resource
    - draft programs being ignored by the engine
    - validator 400s
    - migration backfills
    - `PointsExpiringDetectorJob` reading config
    - replacement for `StackingResolutionWorkedExampleTests`
  - Update `RuleEngineTestHarness`, which sets `QualifyingAccountTypeId` and the stacking fields, and `TestCli/OutboundTest.cs`, which sets `warning_days` with raw SQL.
- **Docs:**
  - `docs/SCOPE_BASELINE.md`: the rows listed in §0, plus a new "Program publishing" row. Replace the "Known limitation" note with the published + active rule.
  - `docs/SOW.md` §2.2, §2.3 and §3.
  - `.claude/rules/01-workflow-and-debugging.md`, the pitfall row "Rule fires for an inactive program".
  - `CLAUDE.md` needs no change.
  - **`LoyaltySaaSApi.md` does not exist**, so the API contract changes can't be recorded there. That has to be settled first (see §7).

## 5. Open decisions requiring explicit PO/Architect sign-off

| # | Decision | Recommended default |
|---|---|---|
| a | Do StreakCampaigns and CardBuckets count toward the unpublished-changes flag and appear in the publish snapshot? The change log names tier, reward, account type and rules only. | **Yes to both.** Card buckets are rules under the hood, and streak campaigns are program configuration. |
| b | Can a program be published while a CASH rule is `pending_approval`? | **Allow.** The rule's status is recorded in the snapshot and the UI warns. Approving the rule later sets the unpublished-changes flag. |
| c | What does a program need before it can be published? | At least one account type. If tiers exist, exactly one tier-qualifying account type. |
| d | If a rule sets `Configuration.Rounding` explicitly, does its fixed 2-decimal rounding still apply, or does account `decimals` win? | Account `decimals` sets the **precision**, and the rule's `Rounding` sets the **direction** (default: down). |
| e | Can the CASH currency be changed after creation? | **No**, same as `Type`. |
| f | Is it acceptable that edits between publishes are no longer written to `ConfigVersion` one by one (the D2 trade-off)? The SOW §2.2 promise that "every program/tier mutation is versioned" would change. | Needs an explicit decision. The alternative is to keep per-edit rows **and** add the publish snapshot. |
| g | Should `decimals` also apply to other rule types and to redemption (`PointsRedeemHandler` rounds to 2 decimals)? | Out of scope; raise a follow-up CR. |
| h | Can a published program go back to Draft? | **No.** Use the Inactive toggle instead. |
| i | How long do the deprecated `programs.warning_days` and `programs.qualifying_account_type_id` columns (and the deprecated response fields) stay? | One release, then dropped in a follow-up CR. |
| j | Is the tier-qualifying flag locked only while the program is active, or once it has ever been published? | While **active**, as today. |
| k | What happens to existing Multiplier rules or custom-group rules if the pre-migration report finds any in production tenants? | Migrate as in D3, after the tenant is notified. Alternatively, block the migration for that tenant until an operator reviews it. |

## 6. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Earn amounts change for POINTS wallets with `decimals > 0` (item 2) | Customers receive fractional points they didn't get before, a visible behaviour change | Tell tenants. Not retroactive. Report per tenant which account types have `decimals > 0` before release. |
| Rule outcomes change where custom exclusivity groups or Multiplier rules exist (item 5) | Different winners, or bonuses added instead of multiplied | Pre-migration report; migration bumps rule versions so audits stay consistent (§5 k) |
| Programs created through the API no longer run until published (item 8) | Integrations that create and then immediately use a program break silently | Listed as a breaking change. Return clear 409s. Publish becomes a documented integration step. |
| A mutation path misses the unpublished-changes flag | The "Unpublished changes" indicator is wrong, and a publish is skipped or reports nothing to publish | One integration test per nested resource and mutation |
| The snapshot serializes tracked entities or grows too large | Private data or circular references end up in the audit record | Build from DTO projections only. Measure snapshot size with a large program in tests. |
| Coarser audit trail (item 9) | Who changed which tier between publishes is lost | §5 f. `rule_versions` still keeps per-edit rule history. |
| Rule cache left stale after the migration or a decimals change | Engine uses old stacking or rounding | Flush the cache after deployment. `AccountTypesAppService` refreshes the rule cache when `decimals` changes. |
| The portal and the engine round decimals differently | The simulator shows amounts that don't match what gets posted | Keep amounts as `DecimalString`/`decimal`. Test both sides with the same fixtures. |
| Backfill: `warning_days >= expiration_days` for some account type | Warning silently dropped | The migration skips those and reports them. The job already skips them today, so behaviour is unchanged. |

## 7. Suggested phasing and verification

The three phases can ship independently, in this order:

| Phase | Items | Depends on |
|---|---|---|
| **P1: Account types** | 1a, 1b, 2, 3, 4 | none |
| **P2: Rules** | 5, 6 | none |
| **P3: Program lifecycle** | 7, 8, 9 | P1, because the snapshot includes `isTierQualifying` and `warning_days` |

Each phase is checked with:
- `dotnet build dEngage.Loyalty.sln`
- `dotnet test src/dEngage.Loyalty.Engine.Tests`
- `dotnet test src/dEngage.Loyalty.IntegrationTests` (requires Docker)
- `npx ng lint`, `npm test` and `npx ng build --configuration development` (from `web/`)
- A manual run with `.\run.ps1` and the Event Simulator. Check that:
  - spend earn respects `decimals`
  - a draft program's rules don't fire
  - publishing writes one `ConfigVersion` row, and the history page shows it
  - the expiring-warning job reads the account type config

**Prerequisite:** `LoyaltySaaSApi.md` is missing, so the API contract changes above have nowhere to be recorded. Before P1, either regenerate it from `src/dEngage.Loyalty.Api/*/*Dtos.cs` and `*Module.cs`, or accept a Swagger export as the contract. This gap was also flagged in `2026-09-22-rules-engine-taxonomy.md` §7.

## 8. Explicitly out of scope

- True staging, where the engine keeps running the last published configuration while edits are pending
- Rolling back or restoring a program from a ConfigVersion snapshot
- Dropping the deprecated `programs` columns
- Removing the Multiplier code path from `WinnerSelector`
- Applying `decimals` to non-spend rule types or to redemption
- Currency whitelists for rewards or condition trees
- Moving existing hard-coded portal strings to i18n

## 10. Implementation record (2026-09-28)

Implemented in the three phases of §7, in order, each built and fully tested before the next
started. §5 was resolved with the recommended defaults, except §5 f (keep per-edit rows).

### 10.1 Deviations from §3, and why

| Where | Analysis said | Implemented | Why |
|---|---|---|---|
| §3.10 / D2 | Publish snapshot under `EntityType = "Program"`; per-edit Program/Tier rows stop | Snapshot under its own `EntityType = "ProgramPublication"`; per-edit rows **kept** | §5 f was answered "keep both". Sharing the `Program` sequence would interleave edit and publish numbers (v1 created, v2 edit, v3 = first publish). Its own sequence makes the version number the publish number (1, 2, 3 …). `Program.PublishedVersion` stores it. |
| §3.9 | "Draft programs … cannot be deleted" | Drafts can be deleted | A draft is always inactive, and the existing rule is "delete only while inactive". Blocking deletion of a draft would strand abandoned drafts. Treated as a wording error in the analysis. |
| §3.9 | Backfill existing programs as published | Done through the column default `'published'`, which also stays as the DB default | Backfills every existing row in one step, and keeps raw-SQL inserts (`scripts/*.sql` tenant seeds, TestCli) producing runnable programs. The API always sets the column explicitly and creates new programs as `draft`. |
| §3.9 | `CreateProgramRequest.Status` "ignored or rejected" | **Rejected** (400 `status_not_settable_on_create`) | A silently ignored field would hide the breaking change from API clients. `warningDays` and `qualifyingAccountTypeId` are rejected the same way (400 `field_moved_to_account_type`). |
| §3.3 / §5 d | Precision from `decimals`, direction from rule `Rounding` | Spend rules round **down** to `decimals`; an explicit rule `Rounding` is applied at the wallet's precision for Spend rules instead of the fixed 2 places. Other rule types keep 2 places (§5 g). | Spend already floored before `Rounding` applied, so in practice direction was always "down". Kept that behaviour and fixed only the precision. |
| §3.6 | Replace `StackingResolutionWorkedExampleTests` | **Kept**, and a new post-1.3.CL worked example was added (`StackingWithoutGroupsCl13Tests`) | The Multiplier code path stays in `WinnerSelector` until the clean-up CR (§8), so the existing test still documents real engine behaviour and still passes. Nothing was deleted or weakened. |
| §3.5 | The migration **reports** CASH currencies not on the whitelist | The report is a pre-deploy query (§10.3), not part of the migration | The idempotent deploy script wraps every migration in `DO $EF$ … $EF$`, where a nested reporting `DO` block is fragile. The migration changes data only. |
| §4 i18n | New keys for new strings | Done, en + tr (76 keys each, at parity) | Strings that already existed in touched files were **not** migrated to keys (§8 out of scope), although `frontend.md` asks for it when a string is touched. |

Additional fixes needed along the way:
- **Test fixture.** `CustomWebApplicationFactory` and `PostgresWebApplicationFactory` faked Redis with `Mock.Of<IConnectionMultiplexer>()`. Its `GetDatabase()` returned null, so any API route that refreshes the rule cache returned 500 under test. Rule create/edit already did this, but no API test covered it until now. The fakes now use `DefaultValue.Mock`.
- **`IConfigVersionService.StageAsync`** now returns the staged version number (`Task<int>`). Existing callers are unaffected.

### 10.2 Verification (all run, all passing)

| Check | Baseline before 1.3.CL | After |
|---|---|---|
| Backend build, every project | 12/12 clean | 12/12 clean, 0 warnings |
| `dotnet test src/dEngage.Loyalty.Engine.Tests` | 58 passed | **65 passed** |
| `dotnet test src/dEngage.Loyalty.IntegrationTests` (Docker, Testcontainers) | 36 passed | **84 passed**, including 10 Postgres E2E tests |
| `npm test` (portal) | 30 passed | **46 passed** |
| `npx ng build --configuration development` | OK | OK |
| `npx ng lint` | 3 errors (pre-existing: `confirm.service.ts` ×2, `dashboard.page.ts`) | the same 3, **no new errors** |
| Regenerated `scripts/loyalty_schema.sql` applied twice to an empty Postgres 16 | — | both runs OK (idempotent), all 3 `…Cl13` migrations recorded |
| **Live end-to-end run**: real Api + Consumer + RabbitMQ + Redis + Postgres (throwaway containers, all 3 migrations applied) | — | **31/31 checks passed**, no errors in either log |

The live run drove the whole flow over HTTP:
1. Create a program: it starts as a draft, and activating it returns 409.
2. Add account types. Checked: warning days, the tier flag and the one-per-program rule, CASH without expiry, currency whitelist and currency lock.
3. Program fields that moved now return 400.
4. Rules: the stacking fields are rejected, and exclusive rules work with no group.
5. Publish (v1), then activate.
6. A real `order.created` event flowed through RabbitMQ and the Consumer and posted **12.34** points: 123.45 × 0.1 floored to 2 places, plus a stackable bonus of 5, giving 17.34.
7. A draft program earned nothing.
8. A rule edit set unpublished changes. Re-publishing gave v2, and the v2 snapshot pins the edited rule at version 2.

Not verified: the portal was not clicked through in a browser. Portal behaviour is covered by the component specs and the production build only.

### 10.3 Pre-deploy queries (run per environment before migrating)

```sql
-- Item 5: rules whose behaviour changes (custom group names, or Multiplier).
-- Rules still carrying the CR-06 backfilled group (= the account type name) resolve exactly as before.
SELECT r.tenant_id, r.program_id, r.id, r.name, r.exclusivity_group, r.stack_mode
FROM rules r LEFT JOIN account_types at ON at.id = r.target_account_type_id
WHERE r.status <> 'deleted'
  AND (r.stack_mode <> 'Additive' OR (r.exclusivity_group IS NOT NULL AND r.exclusivity_group <> at.name));

-- Item 4: CASH wallets whose currency is not on the whitelist (kept as-is, re-validated only on edit).
SELECT tenant_id, program_id, id, name, config->>'currency' AS currency
FROM account_types
WHERE type = 'CASH'
  AND COALESCE(config->>'currency', '') NOT IN ('SAR','AED','KWD','QAR','BHD','OMR','USD','EUR','GBP','TRY');

-- Item 2: POINTS wallets whose Spend earn changes from whole numbers to fractional points.
SELECT tenant_id, program_id, id, name, config->>'decimals' AS decimals
FROM account_types
WHERE type = 'POINTS' AND COALESCE((config->>'decimals')::numeric, 0) > 0;

-- Item 1: tier pointers the backfill cannot carry over (cross-program or non-POINTS target).
SELECT p.id AS program_id, p.qualifying_account_type_id, at.program_id AS at_program, at.type
FROM programs p JOIN account_types at ON at.id = p.qualifying_account_type_id
WHERE at.program_id <> p.id OR at.type <> 'POINTS';
```

### 10.4 Open items (not done; need a decision)

1. **`scripts/loyalty_schema_reference.md` is stale** for `programs` (5 new columns; 2 deprecated), `account_types` (`is_tier_qualifying` and a unique partial index; the `warning_days` config key; CASH has no expiry) and `rules` (retired stacking semantics). The rules say "regenerate, don't hand-edit", but there is no generator for this Markdown: it is prose written from `loyalty_schema.sql`, which *was* regenerated. It needs a decision: allow a hand update, or add a generator.
2. **`LoyaltySaaSApi.md` is still missing**, so the API contract changes (new `POST programs/{id}/publish`, new response fields, and the breaking request changes in §4) are recorded only here and in `SCOPE_BASELINE.md`. The offer to regenerate it from `*Dtos.cs` / `*Module.cs` still stands.
3. **`dotnet build dEngage.Loyalty.sln` fails before compiling anything.** The solution references `src/dEngage.Loyalty.LifeSim`, which does not exist. This predates 1.3.CL. Every project was built individually instead.
4. **Seed scripts.** `scripts/fintech_loyalty.sql` (and the other tenant seeds) still set `programs.warning_days` / `qualifying_account_type_id`. Being reference data, they were not modified. A database seeded from them *after* migrating gets no expiry warnings or tier wallet until the account types are updated. Seeding *before* migrating is backfilled correctly.
5. **Follow-up CRs** from §8: drop the deprecated `programs` columns and response fields after one release; remove the Multiplier code path; decide whether `decimals` applies to other rule types and to redemption.

### 10.5 Files created / changed (review record)

No files were deleted. Paths under `src/` are relative to the project folder.

**Backend: Shared / Schema**

| File | Change | Reason |
|---|---|---|
| `Shared/Constants/SupportedCurrencies.cs` | Created | Item 4 currency whitelist |
| `Shared/Constants/ProgramPublicationStatus.cs` | Created | Item 8 draft/published values |
| `Shared/Constants/ConfigChangeType.cs` | Changed | Item 9 `Published` change type |
| `Schema/Entities/AccountType.cs` | Changed | Item 1 `IsTierQualifying` |
| `Schema/Configurations/AccountTypeConfiguration.cs` | Changed | Item 1 column + unique partial index |
| `Schema/Entities/Program.cs` | Changed | Item 8 publication fields; deprecation notes |
| `Schema/Configurations/ProgramConfiguration.cs` | Changed | Item 8 column mappings |
| `Schema/Migrations/20260928185345_AccountTypeConfigCl13.cs` (+ `.Designer.cs`) | Created | Items 1, 3: flag, index, backfills, CASH expiry strip |
| `Schema/Migrations/20260928190845_RuleStackingRetirementCl13.cs` (+ `.Designer.cs`) | Created | Item 5: archive a version, then clear group / Multiplier |
| `Schema/Migrations/20260928191319_ProgramPublicationCl13.cs` (+ `.Designer.cs`) | Created | Item 8 columns; existing programs → published |
| `Schema/Migrations/LoyaltyDbContextModelSnapshot.cs` | Changed (by `dotnet ef`, not by hand) | Model snapshot |
| `scripts/loyalty_schema.sql` | Regenerated (`dotnet ef migrations script --idempotent`) | Schema script in step |

**Backend: Api**

| File | Change | Reason |
|---|---|---|
| `Api/AccountTypes/AccountTypeConfigValidators.cs` | Changed | warning_days / expiration_days / decimals 0–4; CASH currency whitelist; no CASH expiry |
| `Api/AccountTypes/AccountTypesDtos.cs` | Changed | `isTierQualifying` on requests/response |
| `Api/AccountTypes/AccountTypesAppService.cs` | Changed | Tier-flag rules and lock; currency lock; rule-cache refresh on decimals change; unpublished-changes flag |
| `Api/Programs/ProgramsDtos.cs` | Changed | Publication fields; publish snapshot records; moved/removed request fields kept for 400s |
| `Api/Programs/ProgramsValidators.cs` | Changed | Reject moved fields and create-time status |
| `Api/Programs/ProgramsAppService.cs` | Changed | Draft on create; activation gate; publish + aggregate snapshot; derived qualifying id |
| `Api/Programs/ProgramsModule.cs` | Changed | `POST /{programId}/publish` |
| `Api/Programs/ProgramChangeTracker.cs` | Created | Unpublished-changes flag, staged in the caller's SaveChanges |
| `Api/Program.cs` | Changed | Register `IProgramChangeTracker` |
| `Api/ConfigVersions/ConfigVersionService.cs` | Changed | `StageAsync` returns the version number |
| `Api/Rules/RulesValidators.cs`, `RulesAppService.cs`, `RulesDtos.cs` | Changed | Item 5 retirement; unpublished-changes flag (service); DTO comment |
| `Api/Tiers/TiersAppService.cs`, `Rewards/RewardsAppService.cs`, `CardBuckets/CardBucketsAppService.cs`, `StreakCampaigns/StreakCampaignsAppService.cs` | Changed | Unpublished-changes flag on every mutation |

**Backend: RuleEngine / Consumer / Ledger / TestCli**

| File | Change | Reason |
|---|---|---|
| `RuleEngine/Calculation/IRuleTypeHandler.cs` | Changed | Default-implemented `Compute(…, targetDecimals)` overload |
| `RuleEngine/Calculation/SpendRuleHandler.cs` | Changed | Floor to the wallet's decimals (0–4) |
| `RuleEngine/Models/CachedRule.cs` | Changed | `TargetDecimals` |
| `RuleEngine/Cache/RuleCacheService.cs` | Changed | Load the target wallet's decimals |
| `RuleEngine/Processing/WinnerSelector.cs` | Changed | Pass decimals; wallet precision for Spend `Rounding` |
| `RuleEngine/Processing/ITierContextLoader.cs`, `TierContextLoader.cs`, `RuleEngine.cs` | Changed | Tier wallet resolved from the flag |
| `RuleEngine/TierDowngradeJob.cs` | Changed | Raw SQL joins on `is_tier_qualifying` |
| `RuleEngine/Processing/BirthdayBonusJob.cs` | Changed | Published + active gate |
| `Consumer/CampaignEvaluationService.cs`, `Consumer/RuleSyncService.cs` | Changed | Published + active gate |
| `Ledger/PointsExpiringDetectorJob.cs` | Changed | `warning_days` read from account type config |
| `TestCli/OutboundTest.cs` | Changed | Dev tool sets `warning_days` on the account type |

**Tests**

| File | Change | Reason |
|---|---|---|
| `Engine.Tests/RuleTypeHandlerRegistryTests.cs` | Changed | Decimals table tests |
| `IntegrationTests/Api/AccountTypeChangesCl13ApiTests.cs` | Created | Items 1–4 over HTTP, incl. tenancy |
| `IntegrationTests/Api/RuleStackingCl13ApiTests.cs` | Created | Items 5–6 over HTTP |
| `IntegrationTests/Api/ProgramPublishCl13ApiTests.cs` | Created | Items 7–9 over HTTP |
| `IntegrationTests/Engine/SpendDecimalsCl13Tests.cs` | Created | Item 2 through the engine, idempotency, cache load |
| `IntegrationTests/Engine/StackingWithoutGroupsCl13Tests.cs` | Created | Item 5 worked example |
| `IntegrationTests/Engine/ProgramPublicationGateCl13Tests.cs` | Created | Item 8 consumer gate |
| `IntegrationTests/E2E/AccountTypeChangesCl13E2ETests.cs` | Created | Postgres: jobs, tier lookup, Cl13 backfills incl. rule re-versioning |
| `IntegrationTests/E2E/ProgramLifecycleCl13E2ETests.cs` | Created | Postgres: full HTTP lifecycle; publication backfill |
| `IntegrationTests/Api/Phase4FeaturesSmokeTests.cs` | Changed | Same assertions; setup follows the new create → publish → activate lifecycle, and the qualifying-lock test uses the account-type route |
| `IntegrationTests/E2E/DashboardEndpointE2ETests.cs` | Changed | Create request no longer sends `status` |
| `IntegrationTests/Fixtures/RuleEngineTestHarness.cs` | Changed | Tier flag / config on `AddAccountType`; programs created as published |
| `IntegrationTests/Fixtures/CustomWebApplicationFactory.cs`, `PostgresWebApplicationFactory.cs` | Changed | Redis fake returns a mock database (see §10.1) |

**Portal (`web/src/app/`)**

| File | Change | Reason |
|---|---|---|
| `features/programs/account-types/account-type.model.ts` | Changed | Currency list, `warning_days`, `isTierQualifying`, no CASH expiry |
| `features/programs/account-types/account-type-form.dialog.ts` | Changed | Decimals info button; warning days with server-matching validation; tier toggle; currency select (locked on edit); CASH expiry removed |
| `features/programs/account-types/account-type-form.dialog.spec.ts` | Created | Dialog behaviour (10 tests) |
| `features/programs/account-types/account-types-list.page.ts` | Changed | "Tier qualifying" badge |
| `features/programs/program.model.ts`, `programs.service.ts` | Changed | Publication fields; `publish()`; create without status |
| `features/programs/create-program.dialog.ts` | Changed | Stops sending `status` |
| `features/programs/program-overview.page.ts` | Changed | Moved fields removed (read-only tier wallet + link); Active switch; Draft/Published badge; Publish action |
| `features/programs/program-overview.page.spec.ts` | Created | Publish/toggle behaviour (6 tests) |
| `features/programs/programs-list.page.ts` | Changed | Draft / unpublished-changes pill |
| `features/programs/history/program-history.page.ts`, `program-history.model.ts`, `view-version.dialog.ts` | Changed | Published-versions section; `published` change type; sectioned snapshot view |
| `features/programs/rules/rule-form.page.ts`, `rule.model.ts` | Changed | Exclusivity group / stack mode removed |
| `features/programs/rules/rules-list.page.ts` | Changed | Stackable column |
| `web/public/i18n/en.json`, `web/public/i18n/tr.json` | Changed | 47 new keys each |

**Docs and rules**

| File | Change | Reason |
|---|---|---|
| `docs/SCOPE_BASELINE.md` | Changed | Rows per §0, new "Program publishing" row, revision history |
| `docs/SOW.md` | Changed | §2.2, §2.3, §3 correction, revision 1.3 |
| `.claude/rules/01-workflow-and-debugging.md` | Changed | Pitfall rows for draft programs and remaining status gap; winner-selection wording |
| `.claude/rules/backend-api.md` | Changed | New required side effect (`IProgramChangeTracker`) and Publish |
| `.claude/rules/backend-rule-engine.md` | Changed | Winner-selection description |
| `docs/scope-changes/2026-09-28-program-account-type-changes.md` | Changed | This implementation record |


### 10.6 Addendum: rules list grouped by how rules apply (item 6)

Approved 2026-09-28 by the developer as a small addendum to item 6. It is a portal-only change: no API, engine or schema change.

- **Before:** a flat list of 20 rules per page, ordered by priority, with a Stackable column.
- **After:** the whole program's rules are grouped the way `WinnerSelector` resolves them:
  - Rules are grouped **by trigger event, then by target account**.
  - Inside each group there are two sub-tables:
    - **Exclusive**: highest priority first; only the top matching rule fires.
    - **Stackable**: every matching rule adds on top.
  - Transfer and Reversal rules skip winner selection, so they get their own "Not in winner selection" section.
  - Disabled and pending rules stay in their group, dimmed, with their status pill.
- **Not grouped by rule type:** type only says how an amount is calculated. A Spend rule and a FixedBonus rule on the same event and wallet compete with each other, so they belong in the same group.
- **All rules are loaded at once:** a group split across pages would misrepresent what competes. `RulesService.listAll` pages through the API's 100-row cap, and the pager was removed.

**Files**

| File | Change | Reason |
|---|---|---|
| `web/src/app/features/programs/rules/rule-grouping.ts` | Created | Pure grouping logic that mirrors `WinnerSelector` |
| `web/src/app/features/programs/rules/rule-grouping.spec.ts` | Created | 5 tests |
| `web/src/app/features/programs/rules/rules-list.page.ts` | Changed | Grouped layout |
| `web/src/app/features/programs/rules/rules.service.ts` | Changed | `listAll` |
| `web/public/i18n/en.json`, `tr.json` | Changed | 7 new keys each |
| `docs/SCOPE_BASELINE.md` | Changed | Frontend rules row |

**Verified:**
- `npm test`: 51 passed (46 before).
- `ng build`: OK.
- `ng lint`: only the same 3 errors that existed before.

**Not verified:** a click-through in the browser.

**Follow-up: the list now states the winner semantics explicitly (same day).**

I checked the list against `WinnerSelector.cs` and changed the labels to match it:

- **Exclusive rules have one winner per event *per target account*, not per event.** An event that matches exclusive rules on two different accounts pays both. The developer chose to keep this engine behaviour and only make the UI clearer. Limiting exclusive rules to one winner per event would be an engine scope change.
- **Exclusive rows are now labelled:**
  - "Wins when it matches" on the highest-priority active rule.
  - "Tied top priority — engine picks by rule id" when two active rules share the top priority.
  - "Fallback" on the rest.
  - Inactive rules get no label, because they don't compete.
- **Stackable rules all fire.** The engine posts them lowest priority first, but each is capped only by its own limits, so the order never changes the amounts. The hint text now says so.
- The ranking lives in `rankExclusiveRules` in `rule-grouping.ts`, with 3 more tests (54 portal tests in total).
- `en.json` and `tr.json` have 3 new keys each, and 3 existing hint texts were reworded.

### 10.7 Follow-up: rule Limits section regrouped and validated (not a scope change)

Requested 2026-09-28. The API shape, the engine and the stored data are unchanged. This is a validation fix plus a UI change.

**Bug found.** Limits had **no server-side validation**. The portal also dropped any value it couldn't parse (`toDecimalStringOrNull`).
- A typo such as `10,5` in a budget field saved the rule as **unlimited**.
- Negative and `0` caps were accepted.
- A per-period cap with no period silently used "Day" (`PeriodWindow.cs`).

**Server:** new `RuleLimitsValidator` in `RulesValidators.cs`, applied to rule create and update.

| Field | Rule |
|---|---|
| Point / amount caps | greater than 0, at most 4 decimal places |
| Cooldown hours | greater than 0, at most 2 decimal places |
| Max customers | at least 1 |
| `period` / `reset_window` / `on_breach` | closed lists of allowed values |
| Per-period caps | a period is required |
| Per-day and per-period caps | cannot exceed their total |

**Portal:** new `rule-limits.ts` holds the same rules, and exact decimal comparison uses no floating point.
- The Limits section is now grouped into **Per customer**, **Per event** and **Whole rule**. Each field has a unit and a one-line hint.
- **Period window** is shown only when a per-period cap is set.
- **When a cap is reached** (On breach) is shown only for the caps it applies to. Cooldown, minimum amount and max customers always skip.
- Inputs are text fields with a decimal or numeric keypad, so values stay decimal strings.
- Errors appear per field once the field has been edited or a save was attempted.

**Compatibility:** the API now returns 400 for limit values it previously accepted. None of the 6 rules with limits in the dev database would be rejected, checked with a read-only query.

**Verified:**

| Check | Result |
|---|---|
| Portal tests | 78 passed (24 new) |
| Integration tests | 105 passed (21 new) |
| Engine tests | 65 passed |
| `ng build` | OK |
| `ng lint` | only the same 3 errors that existed before |

Not verified: a click-through in the browser.

The running dev Api and Consumer lock their `bin` folders, so the backend was built and tested into a scratch output folder.

**Files**

| File | Change |
|---|---|
| `src/dEngage.Loyalty.Api/Rules/RulesValidators.cs` | Changed: `RuleLimitsValidator` |
| `src/dEngage.Loyalty.IntegrationTests/Api/RuleLimitsValidatorTests.cs` | Created |
| `web/src/app/features/programs/rules/rule-limits.ts` | Created |
| `web/src/app/features/programs/rules/rule-limits.spec.ts` | Created |
| `web/src/app/features/programs/rules/rule-form.page.ts` | Changed: grouped section, string controls, group validator, request building |
| `web/public/i18n/en.json`, `tr.json` | Changed: 55 new keys each |
| `docs/SCOPE_BASELINE.md` | Changed: Rules row |
