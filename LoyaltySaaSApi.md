# dEngage.Loyalty — REST API contract

**Regenerated 2026-10-02** from the code: the route registrations in `src/dEngage.Loyalty.Api/*/*Module.cs` and the records in `*Dtos.cs`. Request/response JSON shapes for rule conditions, limits, settings and streak config come from the engine models they reuse (`src/dEngage.Loyalty.RuleEngine/Models/*`, `Campaigns/Streak/StreakConfig.cs`). Business rules enforced by validators and app services are summarised only where the code states them; for the full set read the resource's `*Validators.cs` / `*AppService.cs`.

When an endpoint or DTO changes, update this file in the same change (`.claude/rules/00-project-guardrails.md` §5).

## Conventions

| Topic | Contract |
|---|---|
| Base path | `/api/v1`. Tenant routes are `/api/v1/tenants/{tenantId}/…`, where `{tenantId}` is the tenant **slug** (e.g. `fintech`). Platform routes are `/api/v1/platform/…`. |
| Authentication | `Authorization: Bearer <jwt>` (portal admins, from `POST /auth/login`) or `X-Api-Key: <key>` (server-to-server ingestion). |
| Tenant scope | Every tenant route checks the caller may act on `{tenantId}`: a `platform_admin` may act on any tenant, everyone else only on their own (403 `forbidden` otherwise). The tenant is never read from a body or query string. |
| JSON | REST bodies are **camelCase**. Fields with a `null` value are **omitted** from responses (`JsonIgnoreCondition.WhenWritingNull`). |
| Money and points | Every `decimal` is a **JSON string** on the wire (e.g. `"100.5"`), in requests and responses. Requests also accept a JSON number. Never parse into a float. |
| Event data | Event `data` sent through the events routes is serialized **snake_case** (`contact_key`, `points_amount`, …) for the Consumer. |
| Time | UTC, ISO-8601. |
| Page lists | Query `page` (default 1) and `pageSize` (default 25, max 100). Response `{ data, page, pageSize, total }`. |
| Cursor lists | Query `cursor` and `limit` (1–100, default 25). Response `{ data, nextCursor, total? }` — `nextCursor` absent on the last page; `total` = rows matching the filters across all pages (the customer ledger, events, rule-fires and messages return it; counted before the cursor, so it is the same on every page). |
| Ids | Route ids are GUIDs unless stated (400 if malformed). |
| Status codes | 200 OK, 201 Created (POST that creates), 202 Accepted (event ingestion), 204 No Content (deletes, tier reorder). |
| Errors | RFC 7807-shaped body: `{ type, title, status, code, traceId, errors? }`. `code` is snake_case: `invalid_request` (400, `errors` maps field → messages), `unauthorized` (401), `forbidden` (403), `not_found` (404), domain conflict codes (409), `internal_error` (500). Domain codes raised below the API (`insufficient_*`, `reward_name_taken`, …) are mapped by `DomainErrorTranslator`. |

## Auth and health

| Method | Route | Auth | Body → Response |
|---|---|---|---|
| POST | `/api/v1/auth/login` | none | `LoginRequest { email, password }` → `LoginResponse { token, tokenType: "Bearer", expiresInSeconds }` |
| GET | `/api/v1/ping` | none | `{ pong, at }` |
| GET | `/api/v1/ping/db` | none | `{ tenantCount }` |

## Platform — `/api/v1/platform/tenants` (platform_admin only)

| Method | Route | Body / query → Response |
|---|---|---|
| GET | `` | query `search`, paging → page of `TenantResponse` |
| GET | `/{tenantId}` | → `TenantResponse` |
| POST | `` | `CreateTenantRequest { id, name }` → 201 `TenantResponse` (provisions the tenant's partitions) |
| PATCH | `/{tenantId}` | `UpdateTenantRequest { name?, status? }` (`active` \| `suspended`) → `TenantResponse` |
| POST | `/{tenantId}/api-keys` | → 201 `CreateApiKeyResponse { id, prefix, rawKey, createdAt }` — `rawKey` is shown only here |
| GET | `/{tenantId}/api-keys` | paging → page of `ApiKeyResponse { id, prefix, createdAt, lastUsedAt?, revokedAt? }` |
| DELETE | `/{tenantId}/api-keys/{keyId}` | → 204 (revokes) |
| POST | `/{tenantId}/admin-users` | `CreateAdminUserRequest { email, password, role }` (`platform_admin` \| `tenant_admin`) → 201 `AdminUserResponse` |
| GET | `/{tenantId}/admin-users` | paging → page of `AdminUserResponse { id, email, role, tenantId?, status, createdAt }` |

`TenantResponse { id, name, status, createdAt }`.

## Programs — `/api/v1/tenants/{tenantId}/programs`

| Method | Route | Body / query → Response |
|---|---|---|
| GET | `` | paging → page of `ProgramResponse` |
| GET | `/{programId}` | → `ProgramResponse` |
| POST | `` | `CreateProgramRequest` → 201 `ProgramResponse` |
| PATCH | `/{programId}` | `UpdateProgramRequest` → `ProgramResponse` |
| POST | `/{programId}/publish` | → `ProgramResponse` (Draft → Published; writes one `ProgramPublication` config version) |
| DELETE | `/{programId}` | → 204 |

- `CreateProgramRequest { name, description?, slug? }` — a new program starts Draft + inactive; `status`, `qualifyingAccountTypeId` and `warningDays` must be omitted (400 otherwise; kept on the record so old clients get a clear error). `slug` is derived from the name when omitted.
- `UpdateProgramRequest { name?, description?, status?, slug? }` — `status` is `active` \| `inactive`; `slug` can change only while the program is a Draft. `qualifyingAccountTypeId` / `warningDays` must be omitted.
- `ProgramResponse { id, name, description?, status, qualifyingAccountTypeId?, warningDays?, createdAt, accountTypeCount, ruleCount, publicationStatus, hasUnpublishedChanges, publishedVersion?, publishedAt?, publishedBy?, slug }` — `publicationStatus` is `draft` \| `published`; `qualifyingAccountTypeId` / `warningDays` are deprecated.

## Account types — `/api/v1/tenants/{tenantId}/programs/{programId}/account-types`

| Method | Route | Body → Response |
|---|---|---|
| GET | `` | paging → page of `AccountTypeResponse` |
| GET | `/{accountTypeId}` | → `AccountTypeResponse` |
| POST | `` | `CreateAccountTypeRequest { type, name, config, isTierQualifying? }` → 201 |
| PATCH | `/{accountTypeId}` | `UpdateAccountTypeRequest { name?, config?, isTierQualifying? }` → `AccountTypeResponse` |

- `type` is `POINTS` \| `CASH` (immutable). **CR 2026-10-05 (breaking):** `STAMP` is retired — creating one is a 400, `GET ` (list) no longer returns existing STAMP wallets, `GET /{accountTypeId}` still returns one, and `PATCH` on one is 409 `account_type_retired`. `config` is free-form JSON with snake_case keys validated per type by `AccountTypeConfigValidators` (e.g. POINTS `decimals`, `expiration_days`, `warning_days`, `redemption`, `transfer.daily_limit`; CASH `currency`, `decimals`, no expiry, no transfer).
- `AccountTypeResponse { id, type, name, config, createdAt, isTierQualifying }`.

## Tiers — `/api/v1/tenants/{tenantId}/programs/{programId}/tiers`

| Method | Route | Body → Response |
|---|---|---|
| GET | `` | paging → page of `TierResponse` |
| POST | `` | `CreateTierRequest { name, displayName, minPoints, qualifyingModel, qualifyingPeriodDays?, graceDays, sortOrder }` → 201 |
| PATCH | `/{tierId}` | `UpdateTierRequest { name?, displayName?, minPoints?, qualifyingModel?, qualifyingPeriodDays?, graceDays? }` → `TierResponse` |
| POST | `/reorder` | `ReorderTiersRequest { tierIds: [guid] }` → 204 |
| DELETE | `/{tierId}` | → 204 (refused while a reward targets the tier) |

- `qualifyingModel` is `periodic` \| `lifetime`.
- `TierResponse { id, name, displayName, minPoints, qualifyingModel, qualifyingPeriodDays?, graceDays, sortOrder, createdAt, hasAssignedAccounts }`.

## Rewards — `/api/v1/tenants/{tenantId}/programs/{programId}/rewards`

| Method | Route | Body → Response |
|---|---|---|
| GET | `` | paging → page of `RewardResponse` |
| POST | `` | `CreateRewardRequest` → 201 `RewardResponse` |
| PATCH | `/{rewardId}` | `UpdateRewardRequest` → `RewardResponse` |
| PATCH | `/{rewardId}/active` | `{ isActive }` → `RewardResponse` |
| PATCH | `/{rewardId}/approve` | → `RewardResponse` (cashback approval; the approver must differ from the creator) |
| DELETE | `/{rewardId}` | → 204 |

- `CreateRewardRequest { name, displayName, acquisition, rewardType, pointsPrice?, pointsAccountTypeId?, typeConfig?, isActive }`. Creatable: `acquisition` `points_purchase` \| `streak_completion`; `rewardType` `cashback` (either acquisition) \| `tier_upgrade` (streak only). `name` must be `{programSlug}_[a-z0-9_]+`.
- `typeConfig` (snake_case): cashback `{ amount, currency, cash_account_type_id }`; tier_upgrade `{ target_tier_id, duration_days? }`.
- `UpdateRewardRequest { name?, displayName?, pointsPrice?, pointsAccountTypeId?, typeConfig? }`.
- `RewardResponse { id, name, displayName, acquisition, rewardType, pointsPrice?, pointsAccountTypeId?, typeConfig, isActive, createdAt, status, createdBy?, approvedBy? }` — `status` is `active` \| `pending_approval`. Retired acquisition/type values can still appear on old rows. **CR 2026-10-05 (breaking):** `stampAccountTypeId` was removed from the requests, the response and the publish snapshot's `rewards`.

## Rules — `/api/v1/tenants/{tenantId}/programs/{programId}/rules`

| Method | Route | Body / query → Response |
|---|---|---|
| GET | `` | query `event`, `type`, `targetAccountTypeId`, `status`, `stackable`, paging → page of `RuleResponse` |
| GET | `/metadata` | → `RulesMetadataResponse` (the compatibility catalog for the rule builder) |
| GET | `/{ruleId}` | → `RuleResponse` |
| POST | `` | `CreateRuleRequest` → 201 `RuleResponse` (CASH-target rules and redeem rules that pay cash start `pending_approval`) |
| PATCH | `/{ruleId}` | `UpdateRuleRequest` → `RuleResponse` (inserts a new version) |
| PATCH | `/{ruleId}/status` | `SetRuleStatusRequest { status }` (`active` \| `disabled`) → `RuleResponse` (a redeem rule that pays cash and isn't approved goes to `pending_approval` instead of `active`; an incomplete redeem/transfer rule is 400) |
| PATCH | `/{ruleId}/approve` | → `RuleResponse` (CASH / cash-paying redeem rule approval; approver ≠ creator) |
| DELETE | `/{ruleId}` | → 204 |

- `CreateRuleRequest { name, trigger, targetAccountTypeId?, type, calculation?, conditions?, limits?, priority, stackable, exclusivityGroup?, stackMode?, configuration?, activeFrom?, activeTo? }` — `targetAccountTypeId` is null only for `ReversalRule`; `exclusivityGroup` must be null and `stackMode` null or `Additive` (retired by 1.3.CL).
- `UpdateRuleRequest` — the same fields, all optional, without `type`.
- `RuleResponse { id, name, type, trigger, targetAccountTypeId?, calculation?, conditions?, limits?, priority, stackable, exclusivityGroup?, stackMode, configuration?, version, activeFrom?, activeTo?, status, createdBy?, approvedBy?, createdAt, updatedAt }` — `status` is `active` \| `disabled` \| `deleted` \| `pending_approval`.
- `type`: `SpendRule`, `FixedBonusRule`, `RedemptionRule`, `TransferRule`, `ReversalRule`, `ManualAdjustmentRule`. **CR 2026-10-05 (breaking):** `StampRule` and `ExpiryRule` are retired, the `points.expired` trigger is refused (400 `trigger_retired`), and STAMP is no longer a valid target. Existing rules of those kinds were disabled; they can still be read and deleted, but editing, approving or setting them `active` is 409 `rule_type_retired`. Their type can still appear in `RuleResponse`.
- `calculation` (engine `RuleCalculation`): `rate`, `amount`, `ratio`, `minRedeem`, `fee`, `maxPerDay`, `cashAccountTypeId`, `mode`, `allowNegative`, `reason` — the subset per type (`ageDays`/`order` were removed with `ExpiryRule`).
- **CR 2026-10-05 — redeem / transfer rules** (`points.redeem` / `points.transfer` always run on a rule; the POINTS wallet's own `redemption` / `transfer` config only pre-fills new rules in the portal and is never read by events):
  - `RedemptionRule`: `rate` (cash per point, > 0, required), `cashAccountTypeId` (Redeem into: a CASH account type of the same program, required), `minRedeem` (≥ 0, optional). `ratio` is rejected (400) — it meant the inverse. A redeem rule always pays cash, so it starts `pending_approval`; changing `rate` or `cashAccountTypeId` (a new version) needs approval again.
  - `TransferRule`: `maxPerDay` (daily transfer limit in points, > 0, required). `ratio` / `fee` are not used.
  - Checked on create, on `PATCH` (when `calculation` is sent) and before `PATCH .../status` to `active` (400 otherwise). The rule's target is the POINTS wallet debited.
  - **Breaking:** clients that create these rules must send the new fields. Existing redeem/transfer rules were disabled by a migration and must be completed before they can be switched on.
- `conditions` (engine `ConditionTree`): `{ op, groups: [{ op, conditions: [{ field, operator, value: { type, data, currency?, inferred? } }] }] }`.
- `limits` (engine `RuleLimits`, snake_case): `per_customer_total`, `per_customer_per_day`, `max_per_event`, `min_event_amount`, `cooldown_hours`, `max_customers`, `rule_budget_total`, `rule_budget_per_period`, `per_customer_per_period`, `period`, `reset_window`, `on_breach` (default `Clamp`).
- `configuration` (engine `RuleSettings`): `rounding`, `posting` (default `Immediate`), `holdDays`, `expiryOverrideDays`, `reversible` (default true), `testMode`, `notifyOnAward`.
- `RulesMetadataResponse { events: [{ eventType, category, source, cardinality, period?, fields: [{ path, kind }], compatibleRuleTypes }], ruleTypes: [{ ruleType, category, requiredKinds, validTargetAccountKinds, note }] }`.

## Card buckets — `/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets`

A card bucket is stored as a `FixedBonusRule` (template `card_bucket`) authored through structured fields.

| Method | Route | Body / query → Response |
|---|---|---|
| GET | `` | query `status`, paging → page of `CardBucketResponse` |
| GET | `/{bucketId}` | → `CardBucketResponse` |
| POST | `` | `CreateCardBucketRequest` → 201 |
| PATCH | `/{bucketId}` | `UpdateCardBucketRequest` → `CardBucketResponse` |
| PATCH | `/{bucketId}/status` | `{ status }` (`active` \| `disabled`) → `CardBucketResponse` |
| DELETE | `/{bucketId}` | → 204 |

- `CreateCardBucketRequest { name, mccCodes?, amountMin?, amountMax?, countryMode?, countries?, requireCaptured, hourFrom?, hourTo?, daysOfWeek?, perCustomerPerDay?, perCustomerTotal?, targetAccountTypeId, rewardAmount, priority, activeFrom?, activeTo?, additionalConditions? }` — `countryMode` (`in` \| `not_in`) is required when `countries` is non-empty; `additionalConditions` are condition leaves (see Rules).
- `UpdateCardBucketRequest` — the same fields, all optional.
- `CardBucketResponse` — the create fields plus `id`, `status`, `createdAt`, `updatedAt`.

## Streak campaigns — `/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns`

| Method | Route | Body / query → Response |
|---|---|---|
| GET | `` | query `event`, `status`, paging → page of `StreakCampaignResponse` |
| GET | `/{campaignId}` | → `StreakCampaignResponse` |
| POST | `` | `CreateStreakCampaignRequest { name, trigger, targetAccountTypeId, conditions?, config, activeFrom?, activeTo? }` → 201 |
| PATCH | `/{campaignId}` | `UpdateStreakCampaignRequest` (same fields, all optional) → `StreakCampaignResponse` |
| PATCH | `/{campaignId}/status` | `{ status }` (`active` \| `disabled`) → `StreakCampaignResponse` |
| DELETE | `/{campaignId}` | → 204 |

- `trigger` `points.expired` is refused (400 `trigger_retired`, CR 2026-10-05); a campaign already on it was disabled and can't be edited or set `active` (409 `trigger_retired`).

- `conditions` is the flat list `[{ field, op, value }]` (`op`: `eq`, `ne`, `in`, `gte`, `lte`, `gt`, `lt`, `exists`, `occurred_within` with value `{ after_event, hours }`).
- `config` (engine `StreakConfig`, snake_case): `period` (`day` \| `week` \| `month`), `week_start` (`monday` \| `sunday`), `target_periods`, `aggregate { metric: sum \| count, threshold }`, `timezone` (IANA id), `on_complete` (`restart` \| `stop`), `reward { kind: fixed_bonus \| reward_definition, amount?, reward_definition_id? }`.
- `StreakCampaignResponse { id, name, trigger, targetAccountTypeId, conditions?, config, activeFrom?, activeTo?, status, createdAt, updatedAt }`.

## Customers — `/api/v1/tenants/{tenantId}/customers`

Read-only (the birthday route was removed by CR 2026-10-05 addendum A). A "customer" is a contact key with at least one account in the tenant.

| Method | Route | Body / query → Response |
|---|---|---|
| GET | `` | query `search` (contact key, case-insensitive contains), paging → page of `CustomerSummaryResponse { contactKey, accountCount, lastActivityAt }` |
| GET | `/{contactKey}` | → `CustomerProfileResponse` (404 if the contact key has no account) |
| GET | `/{contactKey}/ledger` | query `accountTypeId`, `programId`, `reasonGroup`, `from`, `to`, `eventId`, `ruleId`, `cursor`, `limit` → cursor page of `LedgerEntryResponse`, newest first |
| GET | `/{contactKey}/tier-history` | → `[TierHistoryEntryResponse]`, newest first |
| GET | `/{contactKey}/rule-fires` | query `ruleId`, `from`, `to`, `cursor`, `limit` → cursor page of `CustomerRuleFireResponse`, newest first |
| GET | `/{contactKey}/cap-usage` | → `[RuleCapUsageResponse]` |
| GET | `/{contactKey}/streaks` | → `[CustomerStreakResponse]` |
| GET | `/{contactKey}/rewards` | → `[CustomerRewardResponse]`, newest first |
| GET | `/{contactKey}/card-buckets` | → `[CustomerCardBucketResponse]` |
| GET | `/{contactKey}/messages` | query `eventType`, `status`, `from`, `to`, `cursor`, `limit` → cursor page of `SentMessageResponse`, newest first |
| GET | `/{contactKey}/events` | query `eventType`, `status`, `from`, `to`, `cursor`, `limit` → cursor page of `CustomerEventResponse`, newest first |
| GET | `/{contactKey}/events/{eventId}` | → `CustomerEventDetailResponse` (404 unless the event is linked to the customer) |

- `CustomerProfileResponse { contactKey, balances: [AccountBalanceResponse], tierProgress?, summary?, programs? }`
  - `summary` (CR 2026-10-02 P2): `{ firstSeenAt?, lastActivityAt, failedEventsLast7Days, activeStreakCount }` — `firstSeenAt` is the earliest posting; failed events count only events received after the CR; active streaks are progress rows still `active` with at least one period met.
  - `programs` (P2): `[{ programId, programName, wallets: [{ accountTypeId, name, type, balance, currency?, pendingAmount, expiringAmount?, expiresOn? }], tier?: { tierName?, tierDisplayName?, nextTierName?, nextTierDisplayName?, nextTierMinPoints?, qualifyingPoints, periodStart?, graceEndsAt?, lockedUntil? }, streaks: [{ campaignId, campaignName, streakCount, targetPeriods, completions, status }] }]`. `pendingAmount` = held postings not yet released; `expiringAmount`/`expiresOn` = the same FIFO figure as the points-expiring warning (POINTS wallets with `expiration_days` and `warning_days` only); `graceEndsAt` = `tier_expires_at`; `lockedUntil` = a tier-upgrade reward's lock; `tier` absent when the program has no tiers.
  - `AccountBalanceResponse { accountTypeId, accountTypeName, accountTypeType, balance, expirationDays?, programId, programName }`
  - `TierProgressResponse { currentTierName?, currentTierDisplayName?, nextTierName?, nextTierDisplayName?, nextTierMinPoints?, qualifyingPoints, periodStart?, expiresAt? }` — for the first account that has a tier.
- `LedgerEntryResponse { id, reason, delta, accountTypeId, metadata?, createdAt, sourceEventId, eventType?, ruleId?, ruleName?, ruleVersion?, campaignId?, campaignName?, accountTypeName, accountTypeType, programId, programName }`
  - `eventType` is absent when there is no inbound event (scheduled jobs such as points expiry).
  - `ruleVersion` is the version the posting was made under (from its rule-fire audit row).
  - A fixed-bonus streak posting is reported as `campaignId`/`campaignName` (not as a rule).
- Ledger filters (CR 2026-10-02): `programId` (GUID), `reasonGroup` — `earn` (earn, stamp_earn, cash_load), `burn` (points_redeemed, points_redeemed_cash, reward_purchase, cash_spend, stamp_reset), `transfer` (transfer_out, transfer_in), `expiry` (points_expired), `reward` (reward_cashback), `adjustment` (points_adjusted, refund, rule_reversal) — `from`/`to` (ISO-8601, UTC when no offset; inclusive, on `createdAt`), `eventId`, `ruleId` (GUID; P3). A malformed GUID or date, an unknown `reasonGroup`, or `from` after `to` → 400. `accountTypeId` keeps its lenient parsing (ignored when malformed).
- `TierHistoryEntryResponse { id, fromTierName?, toTierName, qualifyingPoints, createdAt, programId, programName, cause, sourceEventId }` — `cause` is `points` \| `reward` \| `downgrade` (P2, additive).
- `CustomerRuleFireResponse { id, ruleId, ruleName?, ruleVersion, sourceEventId, eventType?, resultingDelta, ledgerEntryId?, conditionsSnapshot?, calculationSnapshot, resolutionSnapshot?, createdAt }` — `ruleId` must be a GUID; `from`/`to` as for the ledger (400 on bad input).
- `RuleCapUsageResponse { ruleId, ruleName, programId, programName, status, isCardBucket, perCustomerTotal?, usedTotal, perCustomerPerDay?, usedToday, perCustomerPerPeriod?, period?, resetWindow?, periodStart?, usedThisPeriod? }` — every non-deleted rule with a per-customer cap in the customer's programs; usage = this customer's `earn` + `stamp_earn` + `refund` postings for the rule (as the engine counts), today = current UTC day, period = the rule's `period`/`reset_window` window.
- `CustomerStreakResponse { campaignId, campaignName, programId, programName, campaignStatus, period, targetPeriods, metric, threshold, rewardKind, streakCount, lastMetPeriod?, completions, status, latestPeriodStart?, latestAggSum?, latestAggCount?, latestPeriodMet?, history: [StreakCompletion] }` — values as stored by the engine; `latest*` is the most recent recorded period, not necessarily the current one.
- `CustomerRewardResponse { source, rewardName, rewardDefinitionId?, rewardType?, sourceEventId, cost?, costWalletName?, outcome, cashAmount?, cashWalletName?, tierName?, status?, createdAt }` — `source` `points_purchase` \| `stamp_completion` (from `reward_log`) or `streak_completion` (a streak granting a reward definition); `outcome` `cash_credited` \| `tier_upgraded` \| `none`.
- `CustomerCardBucketResponse { ruleId, name, programId, programName, status, rewardAmount?, perCustomerPerDay?, usedToday, perCustomerTotal?, usedTotal, postings, lastPostedAt? }` (P3) — every non-deleted card bucket in the customer's programs; usage counted as for `cap-usage`; `postings` counts earn postings.
- Messages (P3): `SentMessageResponse` as in the drawer, **without payload** (only `reason`, Addendum C); `status` filter `pending` \| `published` \| `failed` (unknown → 400); `from`/`to` on `createdAt`. Published messages are purged after 30 days, so older ones are not listed.
- `CustomerEventResponse { eventId, eventType, occurredAt?, receivedAt, processedAt?, status, error?, outcome: [{ accountTypeId, accountTypeName, accountTypeType, postings, netDelta }] }`
  - Lists events whose inbox row carries this contact key — events received **after** CR 2026-10-02 only (no backfill).
  - `status` is `pending` \| `processed` \| `failed` (unknown value → 400). `from`/`to` apply to `receivedAt`, with the same validation as the ledger.
- `CustomerEventDetailResponse { eventId, event?, postings, heldPostings, ruleFires, streaksApplied, streakCompletions, rewards, tierChanges, messages }`
  - `event` (absent when the id has no inbound event, i.e. a scheduled job's postings): `{ eventId, eventType, occurredAt?, receivedAt, processedAt?, status, error?, data? }`. `data` is the event's own snake_case data with sensitive fields replaced by `"***"`: any field whose name (ignoring case, `_` and `-`) contains `phone`, `mobile` or `msisdn`, or is `card_number`, `pan`, `iban`, `national_id`, `iqama` or `passport`, at any depth.
  - `postings`: `[{ id, contactKey, reason, delta, metadata?, createdAt, ruleId?, ruleName?, ruleVersion?, campaignId?, campaignName?, accountTypeId, accountTypeName, accountTypeType, programId, programName }]`. Postings to another contact key appear only under a real inbound event (a transfer's counterparty).
  - `heldPostings`: `[{ id, ruleId, ruleName?, accountTypeId, accountTypeName, reason, delta, holdUntil, postedAt? }]`
  - `ruleFires`: `[{ id, ruleId, ruleName?, ruleVersion, resultingDelta, ledgerEntryId?, conditionsSnapshot?, calculationSnapshot, resolutionSnapshot?, createdAt }]` (snapshots are JSON strings)
  - `streaksApplied`: `[{ campaignId, campaignName?, appliedAt }]`
  - `streakCompletions`: `[{ campaignId, campaignName?, completionNo, completedPeriod, periods, rewardKind, rewardRef?, createdAt }]`
  - `rewards`: `[{ id, rewardName, rewardDefinitionId?, status, completionCount, createdAt, deliveredAt? }]`
  - `tierChanges`: `[{ id, fromTierName?, toTierName, qualifyingPoints, createdAt }]`
  - `messages`: `[{ eventId, eventType, status, attempts, dedupKey?, createdAt, publishedAt?, reason? }]` — outbound messages caused by the event, **without payload**; `reason` (Addendum C, 2026-10-06) is the payload's failure code on `*_failed` messages (e.g. `no_rule`), omitted otherwise — no other payload field is exposed.
  - Linked means: the inbox row carries this contact key, or the customer has a posting for the event id. Otherwise 404 `not_found`.

## Events — `/api/v1/tenants/{tenantId}/events`

Ingestion routes need **API-key** auth and return **202** `EventAcceptedResponse { eventId, status }`: accepted for processing, not the business outcome. The optional `Idempotency-Key` header becomes the `eventId` (a random id otherwise); re-sending the same id is deduplicated by the Consumer. Ingestion is rate-limited per tenant. Bodies are camelCase; the published event `data` is snake_case.

| Method | Route | Auth | Body |
|---|---|---|---|
| POST | `/order-created` | API key | `OrderCreatedRequest { contactKey, amount, channel?, paymentMethod?, items?: [{ sku, category, qty, total }] }` |
| POST | `/order-refunded` | API key | `OrderRefundedRequest { contactKey?, originalEventId, refundRatio?, amount?, originalAmount? }` |
| POST | `/cash-added` | API key | `CashAddedRequest { contactKey, amount, accountTypeId }` — refused in a non-live program (see below) |
| POST | `/cash-spent` | API key | `CashSpentRequest { contactKey, amount, accountTypeId }` — refused in a non-live program (see below) |
| POST | `/points-redeem` | API key | `PointsRedeemRequest { contactKey, pointsAmount, sourceAccountTypeId }` |
| POST | `/points-transfer` | API key | `PointsTransferRequest { contactKey, targetContactKey, amount, accountTypeId }` — published as `{ contact_key, target_contact_key, points_amount, source_account_type_id }` |
| POST | `/reward-purchase` | API key | `RewardPurchaseRequest { contactKey, rewardName?, channel?, rewardId? }` |
| POST | `/generic` | API key | `GenericEventRequest { eventType, data }` — built-in or allow-listed generic types; internally scheduled types are refused |
| POST | `/simulate` | JWT or API key (tenant scope) | `GenericEventRequest` — meant for the portal's Event Simulator (admin JWT); same publish path as `/generic` |
| GET | `/types` | JWT or API key (tenant scope) | → `EventTypesResponse { builtIn, generic, publishable }` |
| GET | `/{eventId}` | JWT or API key (tenant scope) | → `EventStatusResponse { eventId, eventType, status, receivedAt, processedAt?, error? }` |

**Business outcomes (CR 2026-10-05).** Payloads are unchanged; the outcome arrives as an outbound event (snake_case), and the inbox is `processed`, not dead-lettered:
- `points.redeem` / `points.transfer` are processed by the **highest-priority active rule** for the wallet (`source_account_type_id`): conditions, active window and limits must pass; exactly one rule applies, and a lower-priority rule is never tried after the winner's own minimum / daily limit fails. Failures → `loyalty.points.redeem_failed` / `loyalty.points.transfer_failed` with `reason` `program_not_live`, `no_rule`, `rule_limit_reached`, `below_minimum`, `daily_limit_exceeded`, `insufficient_points` / `insufficient_balance`; the failure and success payloads carry `rule_id` (null when no rule applied). The redeem's cash is rounded down to the cash wallet's `decimals`.
- Every event below is refused while the program owning the wallet or reward is not live (status `active` and publication `published`), with nothing posted: `points.redeem`, `points.transfer`, `reward.purchase` (any reward type) → their `*_failed` event, reason `program_not_live`; `cash.added` / `cash.spent` → **new** `loyalty.cash.add_failed` / `loyalty.cash.spend_failed` `{ contact_key, amount, account_type_id, reason, source_event_id }`. `order.refunded` is not gated.

## Config versions — `/api/v1/tenants/{tenantId}/config-versions` (read-only)

| Method | Route | Query → Response |
|---|---|---|
| GET | `` | `entityType` (required), `entityId` (required GUID), paging → page of `ConfigVersionSummary { id, entityType, entityId, versionNumber, changeType, changeSummary?, changedBy, changedAt }` |
| GET | `/{versionId}` | → `ConfigVersionDetail` (the summary fields plus `snapshot`, a JSON string) |

A program publish writes `entityType` `ProgramPublication` with snapshot `{ program, accountTypes, tiers, rewards, rules, streakCampaigns }` (see `ProgramPublicationSnapshot` in `Programs/ProgramsDtos.cs`).

## Dashboard — `/api/v1/tenants/{tenantId}/dashboard`

| Method | Route | Query → Response |
|---|---|---|
| GET | `/summary` | `programId?`, `fromDate?`, `toDate?` → `DashboardSummaryResponse { programCount, tierCount, customerAccountCount, totalBalance, redemptionCount, streakActiveCount, streakCompletedInRange }` (`complaints` removed by CR 2026-10-05) |

## Revision history

| Date | Change | By |
|---|---|---|
| 2026-10-02 | Regenerated from `src/dEngage.Loyalty.Api/*/*Module.cs` and `*Dtos.cs` (the file was missing; prior history unrecoverable). Includes the CR 2026-10-02 (Customer 360) P1 customer endpoints. | Claude Code, at the request of Moiz |
| 2026-10-02 | CR 2026-10-02 (Customer 360) P2: profile `summary`/`programs`, tier-history program/cause, `rule-fires`, `cap-usage`, `streaks`, `rewards`. | Claude Code, at the request of Moiz |
| 2026-10-02 | CR 2026-10-02 (Customer 360) P3: `card-buckets`, `messages`, ledger `ruleId` filter. | Claude Code, at the request of Moiz |
| 2026-10-03 | Customer cursor lists (`ledger`, `events`, `rule-fires`, `messages`) return an additive `total`. | Claude Code, at the request of Moiz |
| 2026-10-05 | CR 2026-10-05 (breaking): Complaints endpoints and `DashboardSummaryResponse.complaints` removed; STAMP account types retired and hidden from the list; `stampAccountTypeId` removed from rewards; `StampRule`, `ExpiryRule` and the `points.expired` trigger retired (`events/types` no longer lists `points.expired`). See `docs/scope-changes/2026-10-05-remove-complaints-and-stamps.md`. | Claude Code, at the request of Moiz |
| 2026-10-06 | CR 2026-10-05 addendum A (breaking): `POST customers/{contactKey}/birthday` removed; `birthdaybonus` is no longer a built-in event type and is refused as a rule or streak trigger (`trigger_retired`). | Claude Code, at the request of Moiz |
