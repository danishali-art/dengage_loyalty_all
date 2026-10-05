# Loyalty DB Şema Referansı

Bu doküman [`loyalty_schema.sql`](loyalty_schema.sql) içindeki tüm tabloları ve kolonları açıklar.

- **Kaynak:** `src/dEngage.Loyalty.Schema` EF Core migration'ları (son migration: `20261005141326_RetireStampsAndExpiryRuleCr1005`). Script `dotnet ef migrations script --idempotent` ile üretilmiştir; şema değişirse aynı komutla yeniden üretilir, elle düzenlenmez.
- **Kurulum:** boş bir PostgreSQL veritabanına `psql -d loyalty_dev -f loyalty_schema.sql`. Ardından **her tenant için** [`provision_tenant.sql`](provision_tenant.sql) (partition oluşturur) ve tenant'ın seed script'i (örn. [`starbucks_loyalty.sql`](starbucks_loyalty.sql)) çalıştırılır.
- Tüm parasal/puan kolonları `numeric(20,4)`'tür; tüm zaman kolonları `timestamptz` (UTC) tutulur.
- Event/mesaj davranışlarının detayı için: `docsv2/01_inbound_events.md`, `docsv2/02_outbound_events.md`.

## Tablolar (özet)

| Tablo | Rol |
|-------|-----|
| [`programs`](#programs) | Tenant'ın loyalty programı (kök tanım) |
| [`account_types`](#account_types) | Cüzdan tanımları — POINTS / CASH (+ geçmişten kalan STAMP) + tip bazlı `config` |
| [`customer_accounts`](#customer_accounts) | Müşteri × cüzdan bakiyesi + güncel tier durumu |
| [`rules`](#rules) | Kazanım kuralları (8 tip — CR-02: Spend / Stamp / FixedBonus / Redemption / Transfer / Reversal / Expiry / ManualAdjustment) |
| [`rule_versions`](#rule_versions) | Kuralın önceki (superseded) versiyonları — CR-09 versiyonlama |
| [`rule_limit_counters`](#rule_limit_counters) | Kural bütçesi/kardinalite için kalıcı sayaç — CR-07/CR-09 (Redis önünde cache olarak durur) |
| [`held_postings`](#held_postings) | Gecikmeli (Delayed) postalama için bekleyen kayıtlar — CR-08, gece job'u ile ledger'a taşınır |
| [`customer_birthdays`](#customer_birthdays) | Müşteri doğum günü (MM-DD) — CR-10, doğum günü bonus job'unun girdisi |
| [`tier_definitions`](#tier_definitions) | Tier basamakları (eşik, pencere, grace) |
| [`tier_upgrade_log`](#tier_upgrade_log) | Tier değişim geçmişi (audit) |
| [`reward_definitions`](#reward_definitions) | Ödül kataloğu (damga doluşu / puanla satın alma) |
| [`reward_log`](#reward_log) | Kazanılan ödüllerin kaydı |
| [`ledger_entries`](#ledger_entries) | Append-only bakiye hareket defteri — **tenant'a göre partitioned** |
| [`event_inbox`](#event_inbox) | Gelen event kayıtları (idempotency + hata izi) — **tenant'a göre partitioned** |
| [`outbox_events`](#outbox_events) | Transactional outbox — dışarı gidecek bildirimler |
| [`rule_fire_audit`](#rule_fire_audit) | — |
| [`streak_log`](#streak_log) | — |
| [`streak_progress`](#streak_progress) | — |
| [`admin_users`](#admin_users) | — |
| [`config_versions`](#config_versions) | — |
| [`event_log`](#event_log) | — |
| [`streak_applied_event`](#streak_applied_event) | — |
| [`streak_campaigns`](#streak_campaigns) | — |
| [`streak_period_state`](#streak_period_state) | — |
| [`tenant_api_keys`](#tenant_api_keys) | — |
| [`tenants`](#tenants) | — |
| `__EFMigrationsHistory` | EF Core migration takibi (dokunulmaz) |

İlişki iskeleti:

```
programs ─┬─< account_types ─┬─< customer_accounts >── tier_definitions
          │                  ├─< rules (target_account_type_id)
          │                  └─< reward_definitions (points hesabı)
          ├─< tier_definitions
          └─< reward_definitions
ledger_entries >── customer_accounts, rules        (partitioned, FK'sız)
reward_log >── account_types, reward_definitions
tier_upgrade_log >── tier_definitions (from/to)
event_inbox, outbox_events                         (bağımsız; event akışı)
```

---

## programs

Tenant'ın loyalty programı. Cüzdanlar, kurallar, tier'lar ve ödüller bir programa bağlanır.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant tanımlayıcısı (küçük harf, örn. `starbucks`) |
| `name` | varchar(255) | ✗ |  | Program adı |
| `description` | text | ✓ |  | — |
| `status` | varchar(20) | ✗ |  | `active` / pasif değerler. **Bilinen sınırlama:** consumer şu an bu alanı filtrelemiyor — programı durdurmak için kuralları `disabled` yapın |
| `created_at` | timestamptz | ✗ |  | Oluşturulma zamanı |
| `publication_status` | varchar(20) | ✗ |  | — |
| `has_unpublished_changes` | boolean | ✗ |  | — |
| `published_version` | integer | ✓ |  | — |
| `published_at` | timestamptz | ✓ |  | — |
| `published_by` | varchar(255) | ✓ |  | — |
| `slug` | varchar(40) | ✗ |  | — |
| `qualifying_account_type_id` | uuid | ✓ |  | Tier hesabında hangi cüzdanın puanı sayılır (FK → `account_types`) |
| `warning_days` | integer | ✓ |  | Puan sönmeden kaç gün önce `loyalty.points.expiring` uyarısı üretilir; null = uyarı yok |

**PK:** `PK_programs (id)`.

**FK:** `qualifying_account_type_id` → `account_types.id` (NO ACTION); `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `IX_programs_qualifying_account_type_id (qualifying_account_type_id)`
- `idx_programs_tenant_id (tenant_id)`
- `ux_programs_tenant_slug (tenant_id, slug)` UNIQUE

---

## account_types

Cüzdan (hesap tipi) tanımları. Bir programda birden fazla cüzdan olur; müşteri bakiyeleri bu tanımlara açılır.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK — inbound event'lerdeki `account_type_id` / `source_account_type_id` budur |
| `tenant_id` | uuid | ✗ |  | Tenant |
| `program_id` | uuid | ✗ |  | FK → `programs` (CASCADE) |
| `type` | varchar(20) | ✗ |  | `POINTS` / `CASH`. `STAMP` CR 2026-10-05 ile kullanımdan kaldırıldı: yeni STAMP cüzdanı açılamaz, mevcut satırlar müşteri geçmişi olarak kalır ve cüzdan listesinde gösterilmez |
| `name` | varchar(255) | ✗ |  | Görünen ad (örn. "Stars", "Cashback Wallet") |
| `config` | jsonb | ✗ |  | Tip bazlı konfigürasyon (aşağıda); boş olabilir: `{}` |
| `is_tier_qualifying` | boolean | ✗ |  | — |
| `created_at` | timestamptz | ✗ |  | — |

**`config` şemaları (tipine göre, tüm anahtarlar opsiyonel):**

| type | Anahtarlar | Örnek |
|------|-----------|-------|
| `POINTS` | `decimal_places`, `expiration_days` (null/yok = süresiz), `redemption { target_account_type_id, rate, min_points }` | `{"expiration_days": 180, "redemption": {"target_account_type_id": "…", "rate": 0.10, "min_points": 200}}` |
| `STAMP` (kullanımdan kaldırıldı, sadece geçmiş satırlar) | `stamp_target` (kart kaç damgada dolar), `reward_type` (doluşta verilecek ödül/kupon tipi) | `{"stamp_target": 10, "reward_type": "free_drink"}` |
| `CASH` | `currency`, `decimal_places` | `{"currency": "USD", "decimal_places": 2}` |

`redemption` bloğu `points.redeem` akışının sözleşmesidir: `rate` (1 puanın para karşılığı), `min_points` (tek seferde bozdurulabilecek asgari puan), `target_account_type_id` (tutarın yazılacağı CASH cüzdan). Blok yoksa redeem `redemption_not_configured` ile reddedilir.

**PK:** `PK_account_types (id)`.

**FK:** `program_id` → `programs.id` (CASCADE); `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `IX_account_types_program_id (program_id)`
- `idx_account_types_tenant_program (tenant_id, program_id)`
- `ux_account_types_tier_qualifying (tenant_id, program_id)` UNIQUE WHERE `is_tier_qualifying`

---

## customer_accounts

Müşteri × cüzdan başına tek bakiye satırı. Bakiye, ledger yazımıyla **aynı transaction'da** güncellenir ve asla negatife düşmez. Tier alanları yalnızca programın qualifying cüzdanında anlamlıdır.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK — `ledger_entries.customer_account_id` buna işaret eder |
| `tenant_id` | uuid | ✗ |  | Tenant |
| `contact_key` | varchar(255) | ✗ |  | Müşterinin CRM kimliği (PII değil) |
| `account_type_id` | uuid | ✗ |  | FK → `account_types` (CASCADE) |
| `balance` | numeric(20,4) | ✗ |  | Güncel bakiye (puan / damga adedi / para) |
| `updated_at` | timestamptz | ✗ |  | Son bakiye/tier güncellemesi |
| `tier_id` | uuid | ✓ |  | Müşterinin güncel tier'ı (FK → `tier_definitions`); null = tier atanmamış |
| `tier_qualifying_pts` | numeric(20,4) | ✗ | `0` | Güncel dönemin qualifying puan sayacı (default 0) |
| `tier_period_start` | date | ✓ |  | Periyodik tier penceresinin başlangıcı (lifetime stratejide null) |
| `tier_expires_at` | date | ✓ |  | Pencere+grace sonu — downgrade job'ının kontrol tarihi |
| `tier_locked_until` | date | ✓ |  | — |

**PK:** `PK_customer_accounts (id)`.

**FK:** `account_type_id` → `account_types.id` (CASCADE); `tenant_id` → `tenants.id` (CASCADE); `tier_id` → `tier_definitions.id` (NO ACTION).

**Index:**

- `IX_customer_accounts_account_type_id (account_type_id)`
- `IX_customer_accounts_tier_id (tier_id)`
- `idx_customer_accounts_tenant_contact (tenant_id, contact_key)`
- `uq_customer_accounts_tenant_contact_accounttype (tenant_id, contact_key, account_type_id)` UNIQUE — müşteri başına cüzdan tekliği

---

## rules

Kazanım kuralları. Rule engine bunları bellekte cache'ler (30 sn delta / saatlik full sync,
yalnızca `active` satırlar — `pending_approval` eşleşmeye girmez).

> **CR-01..CR-11 (2026-09-22):** bu tablo `docs/scope-changes/2026-09-22-rules-engine-taxonomy.md`
> ile 3 tipten 8 tipe, düz AND koşul listesinden gruplu AND/OR ağacına, ve tek versiyondan
> versiyonlanan bir modele genişledi. Aşağıdaki açıklama güncel (post-CR) şemadır.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant (iç kimlik, slug değil — `20260903200505_TenantIdGuidForeignKeys` ile bu branch'ten önce `varchar`'dan dönüştürüldü) |
| `program_id` | uuid | ✗ |  | FK → `programs` (CASCADE) |
| `name` | varchar(255) | ✗ |  | Kural adı — outbound `applied_rules[].name` olarak dışarı gider |
| `type` | varchar(50) | ✗ |  | `SpendRule` / `FixedBonusRule` / `RedemptionRule` / `TransferRule` / `ReversalRule` / `ManualAdjustmentRule` (CR-02) — oluşturulduktan sonra değiştirilemez. `StampRule` ve `ExpiryRule` CR 2026-10-05 ile kaldırıldı; bu tipteki (ve `points.expired` tetikleyicili ya da STAMP hedefli) eski kurallar migration ile `disabled` yapıldı |
| `trigger` | varchar(50) | ✗ |  | Tetikleyici event tipi — built-in event kataloğundaki 14 tipten biri ya da tenant onaylı generic tip |
| `conditions` | jsonb | ✓ |  | Gruplu AND/OR koşul ağacı (CR-05); null = koşulsuz (aşağıda) |
| `calculation` | jsonb | ✗ |  | Tipe özgü kazanım/hesap parametreleri (aşağıda) |
| `configuration` | jsonb | ✓ |  | Kural bazlı davranış ayarları (CR-08, aşağıda); null = tüm ayarlar varsayılan |
| `current_version` | integer | ✗ |  | CR-09: her düzenlemede artar; postalamalar ve iadeler `rule_id + rule_version` ikilisine referans verir |
| `target_account_type_id` | uuid | ✓ |  | Kazanımın yazılacağı cüzdan (FK → `account_types`) — yalnızca `ReversalRule` için NULL: hedef, orijinal postalamadan miras alınır, burada yapılandırılmaz |
| `limits` | jsonb | ✓ |  | Limitler (CR-07, aşağıda); null = limitsiz |
| `priority` | integer | ✗ |  | Exclusivity group içindeki çakışmada büyük olan kazanır |
| `stackable` | boolean | ✗ |  | `false` = exclusive (bir `exclusivity_group` içinde tek kazanan), `true` = kazananın üstüne eklenir/çarpar (`stack_mode`) |
| `exclusivity_group` | varchar(100) | ✓ |  | `stackable=false` iken zorunlu — aynı grup içindeki kurallar birbiriyle yarışır; `stackable=true` iken her zaman NULL'a zorlanır |
| `stack_mode` | varchar(20) | ✗ |  | `Additive` (kendi delta'sını postalar, varsayılan) / `Multiplier` (cüzdanın kazanan taban tutarını çarpar) — yalnızca `stackable=true` iken anlamlı |
| `active_from` | timestamptz | ✓ |  | Kampanya penceresi; null = açık uçlu |
| `active_to` | timestamptz | ✓ |  | Kampanya penceresi; null = açık uçlu |
| `status` | varchar(20) | ✗ |  | `active` / `disabled` / `deleted` / `pending_approval` (CR-04: CASH hedefli yeni kurallar burada başlar, `PATCH .../approve` ile `active`'e geçer) |
| `template` | varchar(50) | ✓ |  | — |
| `created_by` | varchar(100) | ✓ |  | CR-04: `approved_by`, `created_by` ile aynı olamaz (self-approval engeli) |
| `approved_by` | varchar(100) | ✓ |  | CR-04: `approved_by`, `created_by` ile aynı olamaz (self-approval engeli) |
| `created_at` | timestamptz | ✗ |  | — |
| `updated_at` | timestamptz | ✗ |  | — |

**JSON şemaları:**

| Kolon | Şema | Örnekler |
|-------|------|----------|
| `calculation` | Tipe göre alt küme — `rate` (Spend), `amount` (FixedBonus, ManualAdjustment'ta opsiyonel fallback), `ratio`+`minRedeem` (Redemption), `ratio`+`fee`+`maxPerDay` (Transfer), `mode`("proportional"\|"full")+`allowNegative`("allow negative"\|"clamp to zero") (Reversal), `reason`("goodwill"\|"correction"\|"dispute"\|"migration") (ManualAdjustment) — kaldırılan StampRule/ExpiryRule kurallarının eski satırlarında boş obje ya da `ageDays`/`order` kalmış olabilir | `{"rate": 0.10}` |
| `conditions` | Gruplu AND/OR ağacı: `{"op":"AND\|OR","groups":[{"op":"AND\|OR","conditions":[{"field","operator","value":{"type","data","currency?","inferred?"}}]}]}`. `operator` ∈ `gte,lte,between,eq,neq,in,not_in,starts_with,exists,is_null`. Alan bulunamazsa: pozitif operatörler `false`, `not_in`/`neq`/`is_null` `true` döner. | `{"op":"AND","groups":[{"op":"AND","conditions":[{"field":"amount","operator":"gte","value":{"type":"money","data":100}}]}]}` |
| `limits` | `per_customer_total`, `per_customer_per_day`, `max_per_event`, `min_event_amount`, `cooldown_hours`, `max_customers`, `rule_budget_total`, `rule_budget_per_period`, `per_customer_per_period`, `period`("Day\|Week\|Month\|Year"), `reset_window`("Calendar\|Rolling"), `on_breach`("Clamp\|Skip", varsayılan Clamp) — bütçe alanları `rule_limit_counters`'a karşı postalama transaction'ı içinde rezerve edilir (CR-07/CR-09) | `{"rule_budget_per_period": 10000, "period": "Month", "on_breach": "Skip"}` |
| `configuration` | `rounding`("down\|nearest\|up", null=programdan miras), `posting`("Immediate\|Delayed" — "Pending" henüz desteklenmiyor), `holdDays` (Delayed iken zorunlu), `expiryOverrideDays`, `reversible`(varsayılan true), `testMode`(varsayılan false — postalamadan sadece audit), `notifyOnAward`(varsayılan false) | `{"posting": "Delayed", "holdDays": 3}` |

**Soft-delete trigger'ı (`trg_rules_soft_delete`):** `rules` üzerinde fiziksel `DELETE`, satırın `status`'u `deleted` değilse iptal edilir ve satır `status='deleted'` olarak güncellenir (kural geçmişi ledger'dan izlenebilir kalır). Zaten `deleted` olan satırın DELETE'i gerçekten siler (purge).

**PK:** `PK_rules (id)`.

**FK:** `program_id` → `programs.id` (CASCADE); `target_account_type_id` → `account_types.id` (NO ACTION); `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `IX_rules_program_id (program_id)`
- `IX_rules_target_account_type_id (target_account_type_id)`
- `idx_rules_tenant_program_status (tenant_id, program_id, status)`

---

## rule_versions

CR-09 kural versiyonlama: bir kural düzenlendiğinde mevcut satır yerine buraya bir önceki halinin
tam anlık görüntüsü (snapshot) yazılır — `rules` satırı her zaman güncel hali tutar. Bir postalama
ya da iade, ateşlendiği andaki kural halini burada arar (`rule_id + version_number`), böylece
düzenlemeler geçmiş postalamaların hesaplama mantığını değiştirmez.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant (iç kimlik, slug değil) |
| `rule_id` | uuid | ✗ |  | Hangi kuralın versiyonu (FK yok — `rules` fiziksel silinse de versiyon geçmişi kalır) |
| `version_number` | integer | ✗ |  | Bu snapshot'ın versiyon numarası |
| `name` | varchar(255) | ✗ |  | O andaki `rules` satırının birebir kopyası |
| `type` | varchar(50) | ✗ |  | O andaki `rules` satırının birebir kopyası |
| `trigger` | varchar(100) | ✗ |  | O andaki `rules` satırının birebir kopyası |
| `conditions` | jsonb | ✓ |  | O andaki `rules` satırının birebir kopyası |
| `calculation` | jsonb | ✗ |  | O andaki `rules` satırının birebir kopyası |
| `target_account_type_id` | uuid | ✓ |  | O andaki `rules` satırının birebir kopyası |
| `limits` | jsonb | ✓ |  | O andaki `rules` satırının birebir kopyası |
| `configuration` | jsonb | ✓ |  | O andaki `rules` satırının birebir kopyası |
| `priority` | integer | ✗ |  | O andaki `rules` satırının birebir kopyası |
| `stackable` | boolean | ✗ |  | O andaki `rules` satırının birebir kopyası |
| `exclusivity_group` | varchar(100) | ✓ |  | O andaki `rules` satırının birebir kopyası |
| `stack_mode` | varchar(20) | ✗ |  | O andaki `rules` satırının birebir kopyası |
| `active_from` | timestamptz | ✓ |  | O andaki `rules` satırının birebir kopyası |
| `active_to` | timestamptz | ✓ |  | O andaki `rules` satırının birebir kopyası |
| `effective_from` | timestamptz | ✗ |  | Bu versiyonun geçerli olduğu tarih aralığının başlangıcı |
| `created_at` | timestamptz | ✗ |  | Snapshot'ın yazıldığı an (= sonraki düzenlemenin anı) |

**PK:** `PK_rule_versions (id)`.

**Index:**

- `ux_rule_versions_tenant_rule_version (tenant_id, rule_id, version_number)` UNIQUE

---

## rule_limit_counters

CR-07/CR-09: kural bütçesi (`rule_budget_total` / `rule_budget_per_period`) ve kardinalite
sayaçlarının kalıcı kaynağı — Redis bunun önünde önbellek olarak durur, gerçek sayaç burada.
Postalama transaction'ı içinde okunup güncellenir (rezervasyon deseni), böylece eşzamanlı iki
postalama aynı bütçeyi iki kez harcayamaz.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant (iç kimlik, slug değil) |
| `rule_id` | uuid | ✗ |  | Hangi kural |
| `counter_type` | varchar(30) | ✗ |  | Sayaç türü (örn. bütçe toplamı, periyot bazlı bütçe) |
| `period_key` | varchar(20) | ✗ |  | Periyodik sayaçlar için dönem anahtarı (örn. `2026-09`); periyotsuz sayaçlarda sabit bir değer |
| `value` | numeric(20,4) | ✗ |  | Biriken tutar |
| `updated_at` | timestamptz | ✗ |  | — |

**Bilinen sınırlama:** kardinalite kilitleri (`OncePerCustomer`/`OncePerPeriod`) bu tablo üzerinde
check-then-write ile uygulanır, gerçek bir DB unique index ile değil — gerçek eşzamanlılık
altında dar bir yarış penceresi var. Bütçe dışı limitler (per-customer/per-event) hâlâ Redis
cache'i okuyor, postalama transaction'ı içindeki bir sayaç değil. Bkz.
`docs/scope-changes/2026-09-22-rules-engine-taxonomy.md`.

**PK:** `PK_rule_limit_counters (id)`.

**Index:**

- `ux_rule_limit_counters_key (tenant_id, rule_id, counter_type, period_key)` UNIQUE — tek bir sayacın eşzamanlı güncellemesi bu satır üzerinden serialize edilir.

---

## held_postings

CR-08 gecikmeli (Delayed) postalama: `configuration.posting="Delayed"` olan bir kural
ateşlendiğinde ledger'a hemen yazılmaz, burada `hold_until` tarihine kadar bekler; nightly bir
job (`DelayedPostingPromotionWorker`, mevcut `PointsExpirationWorker` deseniyle aynı
self-scheduling + idempotent SQL yaklaşımı) süresi dolanları `ledger_entries`'e taşır.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant (iç kimlik, slug değil) |
| `rule_id` | uuid | ✗ |  | Postalamayı üreten kural |
| `customer_account_id` | uuid | ✗ |  | Hedef bakiye |
| `contact_key` | varchar(255) | ✗ |  | Müşteri (denormalize) |
| `delta` | numeric(20,4) | ✗ |  | Postalanacak tutar |
| `reason` | varchar(50) | ✗ |  | Ledger reason kodu (promote edildiğinde `ledger_entries.reason`'a aynen geçer) |
| `source_event_id` | varchar(255) | ✗ |  | Bu postalamayı tetikleyen event |
| `idempotency_key` | varchar(255) | ✗ |  | Çift promote engeli |
| `metadata` | jsonb | ✓ |  | Postalamaya özgü ek bilgi |
| `hold_until` | timestamptz | ✗ |  | Bu tarihten önce promote edilmez |
| `created_at` | timestamptz | ✗ |  | — |
| `posted_at` | timestamptz | ✓ |  | Promote edildiği an; null = hâlâ bekliyor |
| `ledger_entry_id` | uuid | ✓ |  | Promote sonrası oluşan `ledger_entries` satırı |

**PK:** `PK_held_postings (id)`.

**Index:**

- `idx_held_postings_due (hold_until, posted_at)` — job'un "süresi dolmuş, henüz promote edilmemiş" sorgusu
- `idx_held_postings_tenant_contact_date (tenant_id, contact_key, created_at)` — CR 2026-10-02 (Customer 360): müşteri görünümünün müşteri bazlı sorguları
- `ux_held_postings_tenant_idempotency (tenant_id, idempotency_key)` UNIQUE

---

## customer_birthdays

CR-10: doğum günü bonus kuralının/job'unun girdisi. Customers modülünün salt-okunur kapsamına
bilinçli tek istisna: `POST customers/{ref}/birthday` (yalnızca MM-DD) bu tabloya yazar, başka
hiçbir müşteri alanı admin API'den yazılamaz.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant (iç kimlik, slug değil) |
| `contact_key` | varchar(255) | ✗ |  | Müşteri |
| `month_day` | varchar(5) | ✗ |  | `MM-DD` formatında (yıl tutulmaz) |
| `created_at` | timestamptz | ✗ |  | — |
| `updated_at` | timestamptz | ✗ |  | — |

**29 Şubat politikası:** artık olmayan bir yılda doğum günü bonusu 28 Şubat'ta ödenir
(`BirthdayBonusJob`, deterministik `eventId` ile yıl başına idempotent).

**PK:** `PK_customer_birthdays (id)`.

**Index:**

- `idx_customer_birthdays_month_day (tenant_id, month_day)` — job'un günlük taraması bu üzerinden çalışır
- `ux_customer_birthdays_tenant_contact (tenant_id, contact_key)` UNIQUE

---

## tier_definitions

Tier basamakları. Tier isimleri tenant sözlüğüdür.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant |
| `program_id` | uuid | ✗ |  | FK → `programs` (CASCADE) |
| `name` | varchar(100) | ✗ |  | Slug ad (örn. `gold`) — event'lerde `from_tier`/`to_tier` olarak gider |
| `display_name` | varchar(255) | ✗ |  | Görünen ad (örn. "Gold") |
| `min_points` | numeric(20,4) | ✗ |  | Bu tier için qualifying puan eşiği |
| `qualifying_days` | integer | ✓ |  | null = **lifetime** (ömür boyu toplam); dolu = **periyodik** pencere (son N gün) |
| `grace_days` | integer | ✗ | `0` | Pencere kapandıktan sonra koruma süresi (default 0) |
| `sort_order` | integer | ✗ |  | Basamak sırası (merdiven) |
| `status` | varchar(20) | ✗ | `'active'` | — |
| `created_at` | timestamptz | ✗ |  | — |

**PK:** `PK_tier_definitions (id)`.

**FK:** `program_id` → `programs.id` (CASCADE); `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `idx_tier_definitions_tenant_program (tenant_id, program_id)`
- `uq_tier_definitions_program_name (program_id, name)` UNIQUE WHERE `status != 'deleted'`
- `uq_tier_definitions_program_sort_order (program_id, sort_order)` UNIQUE WHERE `status != 'deleted'`

---

## tier_upgrade_log

Tier değişimlerinin audit kaydı (yükselme gerçek zamanlı, düşme gece batch).

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant |
| `contact_key` | varchar(255) | ✗ |  | Müşteri |
| `from_tier_id` | uuid | ✓ |  | Önceki tier (FK); null = ilk atama |
| `to_tier_id` | uuid | ✗ |  | Yeni tier (FK, CASCADE) |
| `qualifying_pts` | numeric(20,4) | ✗ |  | Değişim anındaki qualifying puan |
| `source_event_id` | varchar(255) | ✗ |  | Değişime sebep olan event (`order.created` eventId'si ya da gece job kimliği) |
| `created_at` | timestamptz | ✗ |  | — |

**PK:** `PK_tier_upgrade_log (id)`.

**FK:** `from_tier_id` → `tier_definitions.id` (NO ACTION); `tenant_id` → `tenants.id` (CASCADE); `to_tier_id` → `tier_definitions.id` (CASCADE).

**Index:**

- `IX_tier_upgrade_log_from_tier_id (from_tier_id)`
- `IX_tier_upgrade_log_to_tier_id (to_tier_id)`
- `idx_tier_upgrade_log_tenant_contact (tenant_id, contact_key)`
- `ux_tier_upgrade_log_source_event (tenant_id, contact_key, source_event_id)` UNIQUE — aynı event'ten çift tier yazımını engeller (idempotency)

---

## reward_definitions

Ödül kataloğu. İki bağımsız eksen taşır: **`acquisition`** (ödül NASIL kazanılır — damga doluşu /
puanla satın alma / streak tamamlama) ve **`reward_type`** (ödül NE'dir — bir kayıt registry'si
ile doğrulanır, bkz. `dEngage.Loyalty.Api.Rewards.RewardTypeRegistry`). Tip'e özgü alanlar
`type_config` (jsonb) içinde tutulur; yeni bir `reward_type` eklemek registry'ye bir giriş
eklemek demektir, şema migration'ı gerekmez. Detay: `docs/scope-changes/2026-09-17-reward-type-taxonomy.md`.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant |
| `program_id` | uuid | ✗ |  | FK → `programs` (CASCADE) |
| `name` | varchar(100) | ✗ |  | Slug ad — inbound `reward.purchase`'taki `reward_name` bununla eşleşir |
| `display_name` | varchar(255) | ✗ |  | Görünen ad |
| `acquisition` | varchar(30) | ✗ |  | Edinim tipi (NASIL kazanılır): `points_purchase` (puanla satın alma) / `streak_completion` (streak kampanyası tamamlanınca). `stamp_completion` (kart dolunca otomatik) CR 2026-09-30 ile kullanımdan kaldırıldı, sadece eski satırlarda kalır |
| `reward_type` | varchar(30) | ✗ |  | Ödül tipi (ödül NE'dir): `points_bonus` / `discount` / `cashback` / `free_product` / `gift_card` / `tier_upgrade` — registry'de doğrulanır, kapalı bir liste değildir |
| `points_price` | numeric(20,4) | ✓ |  | `points_purchase` için: puan fiyatı; diğer acquisition tiplerinde NULL olmalı |
| `points_account_type_id` | uuid | ✓ |  | `points_purchase` için: puanın düşüleceği POINTS cüzdanı (FK); diğer acquisition tiplerinde NULL olmalı |
| `type_config` | jsonb | ✗ | `'{}'` | `reward_type`'a özgü alanlar (örn. discount için `discount_kind`/`value`, free_product için `product_sku`/`quantity`) — registry tarafından doğrulanır, default `{}` |
| `is_active` | boolean | ✗ | `true` | Pasif ödül satın alınamaz (default TRUE) |
| `created_at` | timestamptz | ✗ |  | — |
| `status` | varchar(20) | ✗ | `'active'` | — |
| `created_by` | varchar(255) | ✓ |  | — |
| `approved_by` | varchar(255) | ✓ |  | — |

> **Not:** `external_coupon_type` kolonu kaldırıldı (2026-09-17, `RewardTypeTaxonomy` migration'ı) —
> outbound `loyalty.reward.earned` event'inde artık `coupon_type` yerine `reward_type` gider.
> Gerekçe ve etki analizi: `docs/scope-changes/2026-09-17-reward-type-taxonomy.md`.
>
> **Not:** `stamp_account_type_id` kolonu, FK'sı ve iki index'i (`IX_reward_definitions_stamp_account_type_id`,
> `ux_reward_definitions_active_stamp`) kaldırıldı (2026-10-05, `RetireStampsAndExpiryRuleCr1005` migration'ı).
> Etki analizi: `docs/scope-changes/2026-10-05-remove-complaints-and-stamps.md`.

**PK:** `PK_reward_definitions (id)`.

**FK:** `points_account_type_id` → `account_types.id` (NO ACTION); `program_id` → `programs.id` (CASCADE); `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `IX_reward_definitions_points_account_type_id (points_account_type_id)`
- `IX_reward_definitions_program_id (program_id)`
- `idx_reward_definitions_tenant_program (tenant_id, program_id)`
- `uq_reward_definitions_tenant_program_name (tenant_id, program_id, name)` UNIQUE
- `ux_reward_definitions_tenant_name_active (tenant_id, name)` UNIQUE WHERE `is_active`

---

## reward_log

Kazanılan ödüllerin kaydı (damga doluşu, puanla satın alma ya da streak tamamlama).

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | PK |
| `tenant_id` | uuid | ✗ |  | Tenant |
| `contact_key` | varchar(255) | ✗ |  | Müşteri |
| `account_type_id` | uuid | ✗ |  | İlgili cüzdan (damga doluşunda STAMP, satın almada POINTS; FK, CASCADE) |
| `reward_name` | varchar(100) | ✗ |  | Ödülün adı (STAMP config `reward_type` ya da tanımın `name`'i) — 2026-09-17 öncesi `reward_type` olarak adlandırılıyordu; bir kategori değil, ödülün adını tutar, bu yüzden yeniden adlandırıldı |
| `source_event_id` | varchar(255) | ✗ |  | Ödülü doğuran inbound event |
| `ledger_reset_entry_id` | uuid | ✗ |  | Bağlı ledger kaydı (damga doluşunda `stamp_reset`, satın almada `reward_purchase` satırı) |
| `completion_count` | integer | ✗ |  | Kaçıncı kart doluşu (satın almada 0) |
| `status` | varchar(20) | ✗ |  | Yaşam döngüsü; `pending` ile başlar (teslimat dış sistemde takip edilir) |
| `reward_definition_id` | uuid | ✓ |  | FK → `reward_definitions`; eski kayıtlar için null olabilir |
| `created_at` | timestamptz | ✗ |  | Kazanım anı |
| `delivered_at` | timestamptz | ✓ |  | Teslim edildi işareti (dış sistem onayıyla güncellenebilir) |

**PK:** `PK_reward_log (id)`.

**FK:** `account_type_id` → `account_types.id` (CASCADE); `reward_definition_id` → `reward_definitions.id` (NO ACTION); `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `IX_reward_log_account_type_id (account_type_id)`
- `IX_reward_log_reward_definition_id (reward_definition_id)`
- `idx_reward_log_tenant_contact_status (tenant_id, contact_key, status)`
- `ux_reward_log_source_event (tenant_id, account_type_id, source_event_id)` UNIQUE

---

## ledger_entries

**Append-only** bakiye hareket defteri — sistemin denetim kaynağı. Kayıtlar güncellenmez/silinmez; her hareket bir satırdır ve bakiye aynı transaction'da güncellenir.

> `PARTITION BY LIST (tenant_id)` — her tenant için partition'ı `provision_tenant.sql` oluşturur. Partition'lı olduğu için tabloda PK ve FK yoktur; teklik `uq_ledger_entries_idempotency_key` ile sağlanır.

> **Partitioned** (`LIST (tenant_id)`, ham SQL ile oluşturulur): aşağıdaki anahtar/index listesi EF modelinden üretilir; veritabanındaki kısıtlar farklı olabilir (bkz. açıklama ve Partitioning Notu).

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | Satır kimliği |
| `tenant_id` | varchar(100) | ✗ |  | Tenant (partition anahtarı) |
| `customer_account_id` | uuid | ✗ |  | Hareket eden bakiye (→ `customer_accounts.id`) |
| `contact_key` | varchar(255) | ✗ |  | Müşteri (denormalize — sorgu kolaylığı) |
| `delta` | numeric(20,4) | ✗ |  | Hareket miktarı; kazanımda +, düşümde − |
| `reason` | varchar(50) | ✗ |  | Hareket sebebi — 10 değerli sözlük (aşağıda) |
| `source_event_id` | varchar(255) | ✗ |  | Harekete sebep olan inbound event / job kimliği |
| `rule_id` | uuid | ✓ |  | Kazanımı üreten kural (yalnızca rule engine hareketlerinde) |
| `idempotency_key` | varchar(500) | ✗ |  | Genelde `{eventId}:{reason}` — çift işlem engeli |
| `metadata` | jsonb | ✓ |  | Harekete özgü ek bilgi (örn. redeem'de `{redeemed_points, cash_amount, rate}`) |
| `created_at` | timestamptz | ✗ |  | FIFO expire modelinin yaş referansı |

**`reason` sözlüğü:** `earn` (kural kazanımı) · `stamp_earn` (+1 damga) · `stamp_reset` (kart doluşunda sıfırlama) — bu ikisi CR 2026-10-05'ten beri yazılmaz, sadece geçmiş kayıtlarda bulunur · `cash_load` / `cash_spend` (nakit yükleme/harcama) · `points_redeemed` / `points_redeemed_cash` (puan→nakit dönüşümün iki bacağı) · `refund` (iade geri alımı) · `points_expired` (gece sönmesi) · `reward_purchase` (puanla ödül alımı).

**PK:** `PK_ledger_entries (tenant_id, id)`.

**FK:** `customer_account_id` → `customer_accounts.id` (CASCADE); `rule_id` → `rules.id` (NO ACTION).

**Index:**

- `IX_ledger_entries_customer_account_id (customer_account_id)`
- `IX_ledger_entries_rule_id (rule_id)`
- `idx_ledger_entries_tenant_account_date (tenant_id, customer_account_id, created_at)` — FIFO/ekstre sorguları
- `idx_ledger_entries_tenant_source_event (tenant_id, source_event_id)` — iade akışının "orijinal siparişin kazanımları" araması
- `uq_ledger_entries_idempotency_key (tenant_id, idempotency_key)` UNIQUE

---

## event_inbox

Gelen her event'in kaydı: idempotency kapısı ve hata izi.

> `PARTITION BY LIST (tenant_id)` — partition'ı `provision_tenant.sql` oluşturur.

> **Partitioned** (`LIST (tenant_id)`, ham SQL ile oluşturulur): aşağıdaki anahtar/index listesi EF modelinden üretilir; veritabanındaki kısıtlar farklı olabilir (bkz. açıklama ve Partitioning Notu).

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `event_id` | varchar(255) | ✗ |  | Zarftaki `eventId` — PK'nın parçası; aynı id ikinci kez gelirse işlenmeden ACK'lenir |
| `tenant_id` | varchar(100) | ✗ |  | Tenant (partition anahtarı, PK'nın parçası) |
| `event_type` | varchar(50) | ✗ |  | `order.created`, `points.redeem`, … |
| `payload` | jsonb | ✗ |  | Zarfın tamamı (yeniden işleme/inceleme için) |
| `received_at` | timestamptz | ✗ |  | Alınma zamanı |
| `processed_at` | timestamptz | ✓ |  | İşlenme zamanı |
| `status` | varchar(20) | ✗ |  | `pending` → `processed` / `failed` (failed olanlar DLQ'ya da düşer) |
| `error` | text | ✓ |  | Hata detayı (örn. `insufficient_points: 500 < 2000`) |
| `contact_key` | varchar(255) | ✓ |  | Event verisindeki `contact_key` — CR 2026-10-02 (Customer 360): Consumer inbox satırını yazarken doldurur; yalnızca CR sonrası gelen event'lerde dolu (geriye dönük doldurma yok), `contact_key` taşımayan event'lerde null |

**PK:** `PK_event_inbox (tenant_id, event_id)`.

**Index:**

- `idx_event_inbox_tenant_contact_received (tenant_id, contact_key, received_at)` — CR 2026-10-02 (Customer 360): müşterinin event listesi (Events sekmesi); her tenant partition'ına otomatik eklenir

---

## outbox_events

Transactional outbox: dışarı gidecek bildirimler, iş verisiyle **aynı transaction'da** buraya yazılır; publisher worker RabbitMQ `loyalty.outbound` exchange'ine basar.

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | bigint identity | ✗ |  | PK (sıralı) |
| `event_id` | uuid | ✗ |  | Outbound zarfın `eventId`'si — UNIQUE; tüketici dedup anahtarı |
| `tenant_id` | uuid | ✗ |  | Tenant — routing key'in ilk parçası |
| `event_type` | varchar(100) | ✗ |  | `loyalty.points.earned`, `loyalty.tier.changed`, … (7 tip) |
| `contact_key` | varchar(255) | ✗ |  | İlgili müşteri |
| `payload` | jsonb | ✗ |  | Yayınlanacak zarfın tamamı |
| `dedup_key` | varchar(255) | ✓ |  | İş olayı tekilleştirme anahtarı (örn. `points_earned:{eventId}`, `expiring:{account_id}:{expires_on}`); üreticiler `ON CONFLICT DO NOTHING` ile yazar |
| `status` | varchar(20) | ✗ |  | `pending` → `published` / `failed` (8. denemeden sonra) |
| `attempts` | integer | ✗ |  | Yayın deneme sayısı |
| `next_attempt_at` | timestamptz | ✗ |  | Bir sonraki deneme zamanı (backoff: 1m → 5m → 30m → 2h → 12h) |
| `published_at` | timestamptz | ✓ |  | Yayınlanma zamanı; `published` kayıtlar 30 gün sonra purge edilir |
| `created_at` | timestamptz | ✗ |  | Enqueue zamanı |

**PK:** `PK_outbox_events (id)`.

**FK:** `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `idx_outbox_pending_next_attempt (next_attempt_at)` WHERE `status = 'pending'` — publisher'ın çalışma kuyruğu
- `idx_outbox_tenant_contact_date (tenant_id, contact_key, created_at)` — CR 2026-10-02 (Customer 360): müşteri görünümünün müşteri bazlı sorguları
- `ux_outbox_dedup (tenant_id, dedup_key)` UNIQUE WHERE `dedup_key IS NOT NULL`
- `ux_outbox_event_id (event_id)` UNIQUE

---

## rule_fire_audit

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | — |
| `tenant_id` | uuid | ✗ |  | — |
| `rule_id` | uuid | ✗ |  | — |
| `rule_version` | integer | ✗ |  | — |
| `source_event_id` | varchar(255) | ✗ |  | — |
| `contact_key` | varchar(255) | ✗ |  | — |
| `conditions_snapshot` | jsonb | ✓ |  | — |
| `calculation_snapshot` | jsonb | ✗ |  | — |
| `resulting_delta` | numeric(20,4) | ✗ |  | — |
| `ledger_entry_id` | uuid | ✓ |  | — |
| `resolution_snapshot` | jsonb | ✓ |  | — |
| `created_at` | timestamptz | ✗ |  | — |

**PK:** `PK_rule_fire_audit (id)`.

**FK:** `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `idx_rule_fire_audit_tenant_contact_date (tenant_id, contact_key, created_at)` — CR 2026-10-02 (Customer 360): müşteri görünümünün müşteri bazlı sorguları
- `idx_rule_fire_audit_tenant_rule_date (tenant_id, rule_id, created_at)`
- `ux_rule_fire_audit_source_event_rule (tenant_id, source_event_id, rule_id)` UNIQUE

---

## streak_log

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | — |
| `tenant_id` | uuid | ✗ |  | — |
| `campaign_id` | uuid | ✗ |  | — |
| `contact_key` | varchar(255) | ✗ |  | — |
| `completion_no` | integer | ✗ |  | — |
| `completed_period` | date | ✗ |  | — |
| `periods` | integer | ✗ |  | — |
| `reward_kind` | varchar(50) | ✗ |  | — |
| `reward_ref` | varchar(255) | ✓ |  | — |
| `source_event_id` | varchar(255) | ✗ |  | — |
| `created_at` | timestamptz | ✗ |  | — |

**PK:** `PK_streak_log (id)`.

**FK:** `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `idx_streak_log_tenant_contact (tenant_id, contact_key)` — CR 2026-10-02 (Customer 360): müşteri görünümünün müşteri bazlı sorguları
- `ux_streak_log_completion (tenant_id, campaign_id, contact_key, completion_no)` UNIQUE

---

## streak_progress

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `tenant_id` | uuid | ✗ |  | — |
| `campaign_id` | uuid | ✗ |  | — |
| `contact_key` | varchar(255) | ✗ |  | — |
| `streak_count` | integer | ✗ |  | — |
| `last_met_period` | date | ✓ |  | — |
| `completions` | integer | ✗ |  | — |
| `status` | varchar(20) | ✗ |  | — |
| `updated_at` | timestamptz | ✗ |  | — |

**PK:** `PK_streak_progress (tenant_id, campaign_id, contact_key)`.

**FK:** `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `idx_streak_progress_tenant_contact (tenant_id, contact_key)` — CR 2026-10-02 (Customer 360): müşteri görünümünün müşteri bazlı sorguları

---

## admin_users

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | — |
| `email` | varchar(255) | ✗ |  | — |
| `password_hash` | varchar(500) | ✗ |  | — |
| `role` | varchar(30) | ✗ |  | — |
| `tenant_id` | uuid | ✓ |  | — |
| `status` | varchar(20) | ✗ |  | — |
| `created_at` | timestamptz | ✗ |  | — |

**PK:** `PK_admin_users (id)`.

**FK:** `tenant_id` → `tenants.id` (NO ACTION).

**Index:**

- `idx_admin_users_tenant_id (tenant_id)`
- `uq_admin_users_email (email)` UNIQUE

---

## config_versions

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | — |
| `tenant_id` | uuid | ✗ |  | — |
| `entity_type` | varchar(64) | ✗ |  | — |
| `entity_id` | uuid | ✗ |  | — |
| `version_number` | integer | ✗ |  | — |
| `snapshot` | jsonb | ✗ |  | — |
| `change_type` | varchar(20) | ✗ |  | — |
| `change_summary` | text | ✓ |  | — |
| `changed_by` | varchar(255) | ✗ |  | — |
| `changed_at` | timestamptz | ✗ |  | — |

**PK:** `PK_config_versions (id)`.

**FK:** `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `idx_config_versions_entity_timeline (tenant_id, entity_type, entity_id, changed_at)`
- `ux_config_versions_entity_version (tenant_id, entity_type, entity_id, version_number)` UNIQUE

---

## event_log

> **Partitioned** (`LIST (tenant_id)`, ham SQL ile oluşturulur): aşağıdaki anahtar/index listesi EF modelinden üretilir; veritabanındaki kısıtlar farklı olabilir (bkz. açıklama ve Partitioning Notu).

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `tenant_id` | varchar(100) | ✗ |  | — |
| `event_id` | varchar(255) | ✗ |  | — |
| `contact_key` | varchar(255) | ✗ |  | — |
| `event_type` | varchar(50) | ✗ |  | — |
| `occurred_at` | timestamptz | ✗ |  | — |

**PK:** `PK_event_log (tenant_id, event_id)`.

---

## streak_applied_event

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `tenant_id` | uuid | ✗ |  | — |
| `campaign_id` | uuid | ✗ |  | — |
| `event_id` | varchar(255) | ✗ |  | — |
| `applied_at` | timestamptz | ✗ |  | — |

**PK:** `PK_streak_applied_event (tenant_id, campaign_id, event_id)`.

**FK:** `tenant_id` → `tenants.id` (CASCADE).

---

## streak_campaigns

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | — |
| `tenant_id` | uuid | ✗ |  | — |
| `program_id` | uuid | ✗ |  | — |
| `name` | varchar(255) | ✗ |  | — |
| `trigger` | varchar(50) | ✗ |  | — |
| `target_account_type_id` | uuid | ✗ |  | — |
| `conditions` | jsonb | ✓ |  | — |
| `config` | jsonb | ✗ |  | — |
| `active_from` | timestamptz | ✓ |  | — |
| `active_to` | timestamptz | ✓ |  | — |
| `status` | varchar(20) | ✗ |  | — |
| `created_at` | timestamptz | ✗ |  | — |
| `updated_at` | timestamptz | ✗ |  | — |

**PK:** `PK_streak_campaigns (id)`.

**FK:** `program_id` → `programs.id` (CASCADE); `target_account_type_id` → `account_types.id` (CASCADE); `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `IX_streak_campaigns_program_id (program_id)`
- `IX_streak_campaigns_target_account_type_id (target_account_type_id)`
- `idx_streak_campaigns_tenant_program_status (tenant_id, program_id, status)`

---

## streak_period_state

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `tenant_id` | uuid | ✗ |  | — |
| `campaign_id` | uuid | ✗ |  | — |
| `contact_key` | varchar(255) | ✗ |  | — |
| `period_start` | date | ✗ |  | — |
| `agg_sum` | numeric(18,2) | ✗ |  | — |
| `agg_count` | integer | ✗ |  | — |
| `met` | boolean | ✗ |  | — |
| `updated_at` | timestamptz | ✗ |  | — |

**PK:** `PK_streak_period_state (tenant_id, campaign_id, contact_key, period_start)`.

**FK:** `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `idx_streak_period_state_rule_period (tenant_id, campaign_id, period_start)`

---

## tenant_api_keys

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | — |
| `tenant_id` | uuid | ✗ |  | — |
| `key_prefix` | varchar(80) | ✗ |  | — |
| `hashed_key` | varchar(255) | ✗ |  | — |
| `created_at` | timestamptz | ✗ |  | — |
| `last_used_at` | timestamptz | ✓ |  | — |
| `revoked_at` | timestamptz | ✓ |  | — |

**PK:** `PK_tenant_api_keys (id)`.

**FK:** `tenant_id` → `tenants.id` (CASCADE).

**Index:**

- `idx_tenant_api_keys_tenant_id (tenant_id)`
- `uq_tenant_api_keys_prefix (key_prefix)` UNIQUE

---

## tenants

| Kolon | Tip | Null | Varsayılan | Açıklama |
|-------|-----|------|------------|----------|
| `id` | uuid | ✗ |  | — |
| `slug` | varchar(50) | ✗ |  | — |
| `name` | varchar(255) | ✗ |  | — |
| `status` | varchar(20) | ✗ |  | — |
| `created_at` | timestamptz | ✗ |  | — |

**PK:** `PK_tenants (id)`.

**Index:**

- `ix_tenants_slug (slug)` UNIQUE

---

## __EFMigrationsHistory

EF Core'un migration takip tablosu (`MigrationId`, `ProductVersion`). Uygulama tarafından yönetilir; elle dokunulmaz.

---

## Partitioning Notu

`ledger_entries` ve `event_inbox` **LIST partition**'dır (`tenant_id`). Ana tabloya yazmadan önce ilgili tenant'ın partition'ı var olmalıdır:

```bash
psql -d loyalty_dev -v tenant_id=acme -f provision_tenant.sql
```

Diğer tüm tablolar partition gerektirmez. Yeni tenant onboarding sırası: şema (bir kez) → partition → tenant seed → RabbitMQ kuyruk/binding.
