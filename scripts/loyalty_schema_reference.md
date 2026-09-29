# Loyalty DB Şema Referansı

Bu doküman [`loyalty_schema.sql`](loyalty_schema.sql) içindeki tüm tabloları ve kolonları açıklar.

- **Kaynak:** `src/dEngage.Loyalty.Schema` EF Core migration'ları (son migration: `20260921161222_CustomerBirthdayCr10`). Script `dotnet ef migrations script --idempotent` ile üretilmiştir; şema değişirse aynı komutla yeniden üretilir, elle düzenlenmez.
- **Kurulum:** boş bir PostgreSQL veritabanına `psql -d loyalty_dev -f loyalty_schema.sql`. Ardından **her tenant için** [`provision_tenant.sql`](provision_tenant.sql) (partition oluşturur) ve tenant'ın seed script'i (örn. [`starbucks_loyalty.sql`](starbucks_loyalty.sql)) çalıştırılır.
- Tüm parasal/puan kolonları `numeric(20,4)`'tür; tüm zaman kolonları `timestamptz` (UTC) tutulur.
- Event/mesaj davranışlarının detayı için: `docsv2/01_inbound_events.md`, `docsv2/02_outbound_events.md`.

## Tablolar (özet)

| Tablo | Rol |
|-------|-----|
| [`programs`](#programs) | Tenant'ın loyalty programı (kök tanım) |
| [`account_types`](#account_types) | Cüzdan tanımları — POINTS / STAMP / CASH + tip bazlı `config` |
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
| `__EFMigrationsHistory` | EF Core migration takibi (dokunulmaz) |

İlişki iskeleti:

```
programs ─┬─< account_types ─┬─< customer_accounts >── tier_definitions
          │                  ├─< rules (target_account_type_id)
          │                  └─< reward_definitions (stamp/points hesabı)
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

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | varchar(100) | ✗ | Tenant tanımlayıcısı (küçük harf, örn. `starbucks`) |
| `name` | varchar(255) | ✗ | Program adı |
| `status` | varchar(20) | ✗ | `active` / pasif değerler. **Bilinen sınırlama:** consumer şu an bu alanı filtrelemiyor — programı durdurmak için kuralları `disabled` yapın |
| `qualifying_account_type_id` | uuid | ✓ | Tier hesabında hangi cüzdanın puanı sayılır (FK → `account_types`) |
| `warning_days` | integer | ✓ | Puan sönmeden kaç gün önce `loyalty.points.expiring` uyarısı üretilir; null = uyarı yok |
| `created_at` | timestamptz | ✗ | Oluşturulma zamanı |

**Index:** `idx_programs_tenant_id (tenant_id)`.

## account_types

Cüzdan (hesap tipi) tanımları. Bir programda birden fazla cüzdan olur; müşteri bakiyeleri bu tanımlara açılır.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK — inbound event'lerdeki `account_type_id` / `source_account_type_id` budur |
| `tenant_id` | varchar(100) | ✗ | Tenant |
| `program_id` | uuid | ✗ | FK → `programs` (CASCADE) |
| `type` | varchar(20) | ✗ | `POINTS` / `STAMP` / `CASH` |
| `name` | varchar(255) | ✗ | Görünen ad (örn. "Stars", "Cashback Wallet") |
| `config` | jsonb | ✗ | Tip bazlı konfigürasyon (aşağıda); boş olabilir: `{}` |
| `created_at` | timestamptz | ✗ | |

**`config` şemaları (tipine göre, tüm anahtarlar opsiyonel):**

| type | Anahtarlar | Örnek |
|------|-----------|-------|
| `POINTS` | `decimal_places`, `expiration_days` (null/yok = süresiz), `redemption { target_account_type_id, rate, min_points }` | `{"expiration_days": 180, "redemption": {"target_account_type_id": "…", "rate": 0.10, "min_points": 200}}` |
| `STAMP` | `stamp_target` (kart kaç damgada dolar), `reward_type` (doluşta verilecek ödül/kupon tipi) | `{"stamp_target": 10, "reward_type": "free_drink"}` |
| `CASH` | `currency`, `decimal_places` | `{"currency": "USD", "decimal_places": 2}` |

`redemption` bloğu `points.redeem` akışının sözleşmesidir: `rate` (1 puanın para karşılığı), `min_points` (tek seferde bozdurulabilecek asgari puan), `target_account_type_id` (tutarın yazılacağı CASH cüzdan). Blok yoksa redeem `redemption_not_configured` ile reddedilir.

**Index:** `idx_account_types_tenant_program (tenant_id, program_id)`, `IX_account_types_program_id`.

## customer_accounts

Müşteri × cüzdan başına tek bakiye satırı. Bakiye, ledger yazımıyla **aynı transaction'da** güncellenir ve asla negatife düşmez. Tier alanları yalnızca programın qualifying cüzdanında anlamlıdır.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK — `ledger_entries.customer_account_id` buna işaret eder |
| `tenant_id` | varchar(100) | ✗ | Tenant |
| `contact_key` | varchar(255) | ✗ | Müşterinin CRM kimliği (PII değil) |
| `account_type_id` | uuid | ✗ | FK → `account_types` (CASCADE) |
| `balance` | numeric(20,4) | ✗ | Güncel bakiye (puan / damga adedi / para) |
| `tier_id` | uuid | ✓ | Müşterinin güncel tier'ı (FK → `tier_definitions`); null = tier atanmamış |
| `tier_qualifying_pts` | numeric(20,4) | ✗ | Güncel dönemin qualifying puan sayacı (default 0) |
| `tier_period_start` | date | ✓ | Periyodik tier penceresinin başlangıcı (lifetime stratejide null) |
| `tier_expires_at` | date | ✓ | Pencere+grace sonu — downgrade job'ının kontrol tarihi |
| `updated_at` | timestamptz | ✗ | Son bakiye/tier güncellemesi |

**Kısıt/Index:** `uq_customer_accounts_tenant_contact_accounttype (tenant_id, contact_key, account_type_id)` UNIQUE — müşteri başına cüzdan tekliği; `idx_customer_accounts_tenant_contact`, `IX_customer_accounts_account_type_id`, `IX_customer_accounts_tier_id`.

## rules

Kazanım kuralları. Rule engine bunları bellekte cache'ler (30 sn delta / saatlik full sync,
yalnızca `active` satırlar — `pending_approval` eşleşmeye girmez).

> **CR-01..CR-11 (2026-09-22):** bu tablo `docs/scope-changes/2026-09-22-rules-engine-taxonomy.md`
> ile 3 tipten 8 tipe, düz AND koşul listesinden gruplu AND/OR ağacına, ve tek versiyondan
> versiyonlanan bir modele genişledi. Aşağıdaki açıklama güncel (post-CR) şemadır.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | uuid | ✗ | Tenant (iç kimlik, slug değil — `20260903200505_TenantIdGuidForeignKeys` ile bu branch'ten önce `varchar`'dan dönüştürüldü) |
| `program_id` | uuid | ✗ | FK → `programs` (CASCADE) |
| `name` | varchar(255) | ✗ | Kural adı — outbound `applied_rules[].name` olarak dışarı gider |
| `type` | varchar(50) | ✗ | `SpendRule` / `StampRule` / `FixedBonusRule` / `RedemptionRule` / `TransferRule` / `ReversalRule` / `ExpiryRule` / `ManualAdjustmentRule` (CR-02) — oluşturulduktan sonra değiştirilemez |
| `trigger` | varchar(50) | ✗ | Tetikleyici event tipi — built-in event kataloğundaki 14 tipten biri ya da tenant onaylı generic tip |
| `conditions` | jsonb | ✓ | Gruplu AND/OR koşul ağacı (CR-05); null = koşulsuz (aşağıda) |
| `calculation` | jsonb | ✗ | Tipe özgü kazanım/hesap parametreleri (aşağıda) |
| `target_account_type_id` | uuid | ✓ | Kazanımın yazılacağı cüzdan (FK → `account_types`) — yalnızca `ReversalRule` için NULL: hedef, orijinal postalamadan miras alınır, burada yapılandırılmaz |
| `limits` | jsonb | ✓ | Limitler (CR-07, aşağıda); null = limitsiz |
| `priority` | integer | ✗ | Exclusivity group içindeki çakışmada büyük olan kazanır |
| `stackable` | boolean | ✗ | `false` = exclusive (bir `exclusivity_group` içinde tek kazanan), `true` = kazananın üstüne eklenir/çarpar (`stack_mode`) |
| `exclusivity_group` | varchar(100) | ✓ | `stackable=false` iken zorunlu — aynı grup içindeki kurallar birbiriyle yarışır; `stackable=true` iken her zaman NULL'a zorlanır |
| `stack_mode` | varchar(20) | ✗ | `Additive` (kendi delta'sını postalar, varsayılan) / `Multiplier` (cüzdanın kazanan taban tutarını çarpar) — yalnızca `stackable=true` iken anlamlı |
| `configuration` | jsonb | ✓ | Kural bazlı davranış ayarları (CR-08, aşağıda); null = tüm ayarlar varsayılan |
| `current_version` | integer | ✗ | CR-09: her düzenlemede artar; postalamalar ve iadeler `rule_id + rule_version` ikilisine referans verir |
| `created_by` / `approved_by` | varchar(100) | ✓ | CR-04: `approved_by`, `created_by` ile aynı olamaz (self-approval engeli) |
| `active_from` / `active_to` | date | ✓ | Kampanya penceresi; null = açık uçlu |
| `status` | varchar(20) | ✗ | `active` / `disabled` / `deleted` / `pending_approval` (CR-04: CASH hedefli yeni kurallar burada başlar, `PATCH .../approve` ile `active`'e geçer) |
| `created_at` / `updated_at` | timestamptz | ✗ | |

**JSON şemaları:**

| Kolon | Şema | Örnekler |
|-------|------|----------|
| `calculation` | Tipe göre alt küme — `rate` (Spend), `amount` (FixedBonus, ManualAdjustment'ta opsiyonel fallback), `ratio`+`minRedeem` (Redemption), `ratio`+`fee`+`maxPerDay` (Transfer), `mode`("proportional"\|"full")+`allowNegative`("allow negative"\|"clamp to zero") (Reversal), `ageDays`+`order`("FIFO"\|"LIFO") (Expiry), `reason`("goodwill"\|"correction"\|"dispute"\|"migration") (ManualAdjustment) — StampRule boş obje kullanır | `{"rate": 0.10}` |
| `conditions` | Gruplu AND/OR ağacı: `{"op":"AND\|OR","groups":[{"op":"AND\|OR","conditions":[{"field","operator","value":{"type","data","currency?","inferred?"}}]}]}`. `operator` ∈ `gte,lte,between,eq,neq,in,not_in,starts_with,exists,is_null`. Alan bulunamazsa: pozitif operatörler `false`, `not_in`/`neq`/`is_null` `true` döner. | `{"op":"AND","groups":[{"op":"AND","conditions":[{"field":"amount","operator":"gte","value":{"type":"money","data":100}}]}]}` |
| `limits` | `per_customer_total`, `per_customer_per_day`, `max_per_event`, `min_event_amount`, `cooldown_hours`, `max_customers`, `rule_budget_total`, `rule_budget_per_period`, `per_customer_per_period`, `period`("Day\|Week\|Month\|Year"), `reset_window`("Calendar\|Rolling"), `on_breach`("Clamp\|Skip", varsayılan Clamp) — bütçe alanları `rule_limit_counters`'a karşı postalama transaction'ı içinde rezerve edilir (CR-07/CR-09) | `{"rule_budget_per_period": 10000, "period": "Month", "on_breach": "Skip"}` |
| `configuration` | `rounding`("down\|nearest\|up", null=programdan miras), `posting`("Immediate\|Delayed" — "Pending" henüz desteklenmiyor), `holdDays` (Delayed iken zorunlu), `expiryOverrideDays`, `reversible`(varsayılan true), `testMode`(varsayılan false — postalamadan sadece audit), `notifyOnAward`(varsayılan false) | `{"posting": "Delayed", "holdDays": 3}` |

**Soft-delete trigger'ı (`trg_rules_soft_delete`):** `rules` üzerinde fiziksel `DELETE`, satırın `status`'u `deleted` değilse iptal edilir ve satır `status='deleted'` olarak güncellenir (kural geçmişi ledger'dan izlenebilir kalır). Zaten `deleted` olan satırın DELETE'i gerçekten siler (purge).

**Index:** `idx_rules_tenant_program_status (tenant_id, program_id, status)`, `IX_rules_program_id`, `IX_rules_target_account_type_id`.

## rule_versions

CR-09 kural versiyonlama: bir kural düzenlendiğinde mevcut satır yerine buraya bir önceki halinin
tam anlık görüntüsü (snapshot) yazılır — `rules` satırı her zaman güncel hali tutar. Bir postalama
ya da iade, ateşlendiği andaki kural halini burada arar (`rule_id + version_number`), böylece
düzenlemeler geçmiş postalamaların hesaplama mantığını değiştirmez.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | uuid | ✗ | Tenant (iç kimlik, slug değil) |
| `rule_id` | uuid | ✗ | Hangi kuralın versiyonu (FK yok — `rules` fiziksel silinse de versiyon geçmişi kalır) |
| `version_number` | integer | ✗ | Bu snapshot'ın versiyon numarası |
| `name`, `type`, `trigger`, `conditions`, `calculation`, `target_account_type_id`, `limits`, `configuration`, `priority`, `stackable`, `exclusivity_group`, `stack_mode`, `active_from`, `active_to` | — | — | O andaki `rules` satırının birebir kopyası |
| `effective_from` | timestamptz | ✗ | Bu versiyonun geçerli olduğu tarih aralığının başlangıcı |
| `created_at` | timestamptz | ✗ | Snapshot'ın yazıldığı an (= sonraki düzenlemenin anı) |

**Index:** `ux_rule_versions_tenant_rule_version (tenant_id, rule_id, version_number)` UNIQUE.

## rule_limit_counters

CR-07/CR-09: kural bütçesi (`rule_budget_total` / `rule_budget_per_period`) ve kardinalite
sayaçlarının kalıcı kaynağı — Redis bunun önünde önbellek olarak durur, gerçek sayaç burada.
Postalama transaction'ı içinde okunup güncellenir (rezervasyon deseni), böylece eşzamanlı iki
postalama aynı bütçeyi iki kez harcayamaz.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | uuid | ✗ | Tenant (iç kimlik, slug değil) |
| `rule_id` | uuid | ✗ | Hangi kural |
| `counter_type` | varchar(30) | ✗ | Sayaç türü (örn. bütçe toplamı, periyot bazlı bütçe) |
| `period_key` | varchar(20) | ✗ | Periyodik sayaçlar için dönem anahtarı (örn. `2026-09`); periyotsuz sayaçlarda sabit bir değer |
| `value` | numeric(20,4) | ✗ | Biriken tutar |
| `updated_at` | timestamptz | ✗ | |

**Index:** `ux_rule_limit_counters_key (tenant_id, rule_id, counter_type, period_key)` UNIQUE —
tek bir sayacın eşzamanlı güncellemesi bu satır üzerinden serialize edilir.

**Bilinen sınırlama:** kardinalite kilitleri (`OncePerCustomer`/`OncePerPeriod`) bu tablo üzerinde
check-then-write ile uygulanır, gerçek bir DB unique index ile değil — gerçek eşzamanlılık
altında dar bir yarış penceresi var. Bütçe dışı limitler (per-customer/per-event) hâlâ Redis
cache'i okuyor, postalama transaction'ı içindeki bir sayaç değil. Bkz.
`docs/scope-changes/2026-09-22-rules-engine-taxonomy.md`.

## held_postings

CR-08 gecikmeli (Delayed) postalama: `configuration.posting="Delayed"` olan bir kural
ateşlendiğinde ledger'a hemen yazılmaz, burada `hold_until` tarihine kadar bekler; nightly bir
job (`DelayedPostingPromotionWorker`, mevcut `PointsExpirationWorker` deseniyle aynı
self-scheduling + idempotent SQL yaklaşımı) süresi dolanları `ledger_entries`'e taşır.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | uuid | ✗ | Tenant (iç kimlik, slug değil) |
| `rule_id` | uuid | ✗ | Postalamayı üreten kural |
| `customer_account_id` | uuid | ✗ | Hedef bakiye |
| `contact_key` | varchar(255) | ✗ | Müşteri (denormalize) |
| `delta` | numeric(20,4) | ✗ | Postalanacak tutar |
| `reason` | varchar(50) | ✗ | Ledger reason kodu (promote edildiğinde `ledger_entries.reason`'a aynen geçer) |
| `source_event_id` | varchar(255) | ✗ | Bu postalamayı tetikleyen event |
| `idempotency_key` | varchar(255) | ✗ | Çift promote engeli |
| `metadata` | jsonb | ✓ | Postalamaya özgü ek bilgi |
| `hold_until` | timestamptz | ✗ | Bu tarihten önce promote edilmez |
| `created_at` | timestamptz | ✗ | |
| `posted_at` | timestamptz | ✓ | Promote edildiği an; null = hâlâ bekliyor |
| `ledger_entry_id` | uuid | ✓ | Promote sonrası oluşan `ledger_entries` satırı |

**Index:** `idx_held_postings_due (hold_until, posted_at)` — job'un "süresi dolmuş, henüz
promote edilmemiş" sorgusu; `ux_held_postings_tenant_idempotency (tenant_id, idempotency_key)`
UNIQUE.

## customer_birthdays

CR-10: doğum günü bonus kuralının/job'unun girdisi. Customers modülünün salt-okunur kapsamına
bilinçli tek istisna: `POST customers/{ref}/birthday` (yalnızca MM-DD) bu tabloya yazar, başka
hiçbir müşteri alanı admin API'den yazılamaz.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | uuid | ✗ | Tenant (iç kimlik, slug değil) |
| `contact_key` | varchar(255) | ✗ | Müşteri |
| `month_day` | varchar(5) | ✗ | `MM-DD` formatında (yıl tutulmaz) |
| `created_at` / `updated_at` | timestamptz | ✗ | |

**Index:** `idx_customer_birthdays_month_day (tenant_id, month_day)` — job'un günlük taraması bu
üzerinden çalışır; `ux_customer_birthdays_tenant_contact (tenant_id, contact_key)` UNIQUE.

**29 Şubat politikası:** artık olmayan bir yılda doğum günü bonusu 28 Şubat'ta ödenir
(`BirthdayBonusJob`, deterministik `eventId` ile yıl başına idempotent).

## tier_definitions

Tier basamakları. Tier isimleri tenant sözlüğüdür.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | varchar(100) | ✗ | Tenant |
| `program_id` | uuid | ✗ | FK → `programs` (CASCADE) |
| `name` | varchar(100) | ✗ | Slug ad (örn. `gold`) — event'lerde `from_tier`/`to_tier` olarak gider |
| `display_name` | varchar(255) | ✗ | Görünen ad (örn. "Gold") |
| `min_points` | numeric(20,4) | ✗ | Bu tier için qualifying puan eşiği |
| `qualifying_days` | integer | ✓ | null = **lifetime** (ömür boyu toplam); dolu = **periyodik** pencere (son N gün) |
| `grace_days` | integer | ✗ | Pencere kapandıktan sonra koruma süresi (default 0) |
| `sort_order` | integer | ✗ | Basamak sırası (merdiven) |
| `created_at` | timestamptz | ✗ | |

**Kısıt/Index:** `uq_tier_definitions_program_name (program_id, name)` UNIQUE; `idx_tier_definitions_tenant_program`.

## tier_upgrade_log

Tier değişimlerinin audit kaydı (yükselme gerçek zamanlı, düşme gece batch).

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | varchar(100) | ✗ | Tenant |
| `contact_key` | varchar(255) | ✗ | Müşteri |
| `from_tier_id` | uuid | ✓ | Önceki tier (FK); null = ilk atama |
| `to_tier_id` | uuid | ✗ | Yeni tier (FK, CASCADE) |
| `qualifying_pts` | numeric(20,4) | ✗ | Değişim anındaki qualifying puan |
| `source_event_id` | varchar(255) | ✗ | Değişime sebep olan event (`order.created` eventId'si ya da gece job kimliği) |
| `created_at` | timestamptz | ✗ | |

**Kısıt/Index:** `ux_tier_upgrade_log_source_event (tenant_id, contact_key, source_event_id)` UNIQUE — aynı event'ten çift tier yazımını engeller (idempotency); `idx_tier_upgrade_log_tenant_contact`.

## reward_definitions

Ödül kataloğu. İki bağımsız eksen taşır: **`acquisition`** (ödül NASIL kazanılır — damga doluşu /
puanla satın alma / streak tamamlama) ve **`reward_type`** (ödül NE'dir — bir kayıt registry'si
ile doğrulanır, bkz. `dEngage.Loyalty.Api.Rewards.RewardTypeRegistry`). Tip'e özgü alanlar
`type_config` (jsonb) içinde tutulur; yeni bir `reward_type` eklemek registry'ye bir giriş
eklemek demektir, şema migration'ı gerekmez. Detay: `docs/scope-changes/2026-09-17-reward-type-taxonomy.md`.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | varchar(100) | ✗ | Tenant |
| `program_id` | uuid | ✗ | FK → `programs` (CASCADE) |
| `name` | varchar(100) | ✗ | Slug ad — inbound `reward.purchase`'taki `reward_name` bununla eşleşir |
| `display_name` | varchar(255) | ✗ | Görünen ad |
| `acquisition` | varchar(30) | ✗ | Edinim tipi (NASIL kazanılır): `stamp_completion` (kart dolunca otomatik) / `points_purchase` (puanla satın alma) / `streak_completion` (streak kampanyası tamamlanınca) |
| `reward_type` | varchar(30) | ✗ | Ödül tipi (ödül NE'dir): `points_bonus` / `discount` / `cashback` / `free_product` / `gift_card` / `tier_upgrade` — registry'de doğrulanır, kapalı bir liste değildir |
| `stamp_account_type_id` | uuid | ✓ | `stamp_completion` için: hangi STAMP cüzdanının doluşu (FK); diğer acquisition tiplerinde NULL olmalı |
| `points_price` | numeric(20,4) | ✓ | `points_purchase` için: puan fiyatı; diğer acquisition tiplerinde NULL olmalı |
| `points_account_type_id` | uuid | ✓ | `points_purchase` için: puanın düşüleceği POINTS cüzdanı (FK); diğer acquisition tiplerinde NULL olmalı |
| `type_config` | jsonb | ✗ | `reward_type`'a özgü alanlar (örn. discount için `discount_kind`/`value`, free_product için `product_sku`/`quantity`) — registry tarafından doğrulanır, default `{}` |
| `is_active` | boolean | ✗ | Pasif ödül satın alınamaz (default TRUE) |
| `created_at` | timestamptz | ✗ | |

**Kısıt/Index:** `uq_reward_definitions_tenant_program_name (tenant_id, program_id, name)` UNIQUE; `ux_reward_definitions_active_stamp (tenant_id, stamp_account_type_id) WHERE acquisition='stamp_completion' AND is_active` — bir damga cüzdanında aynı anda **tek** aktif doluş ödülü olabilir; ayrıca program/points/stamp FK index'leri.

> **Not:** `external_coupon_type` kolonu kaldırıldı (2026-09-17, `RewardTypeTaxonomy` migration'ı) —
> outbound `loyalty.reward.earned` event'inde artık `coupon_type` yerine `reward_type` gider.
> Gerekçe ve etki analizi: `docs/scope-changes/2026-09-17-reward-type-taxonomy.md`.

## reward_log

Kazanılan ödüllerin kaydı (damga doluşu, puanla satın alma ya da streak tamamlama).

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | PK |
| `tenant_id` | varchar(100) | ✗ | Tenant |
| `contact_key` | varchar(255) | ✗ | Müşteri |
| `account_type_id` | uuid | ✗ | İlgili cüzdan (damga doluşunda STAMP, satın almada POINTS; FK, CASCADE) |
| `reward_name` | varchar(100) | ✗ | Ödülün adı (STAMP config `reward_type` ya da tanımın `name`'i) — 2026-09-17 öncesi `reward_type` olarak adlandırılıyordu; bir kategori değil, ödülün adını tutar, bu yüzden yeniden adlandırıldı |
| `source_event_id` | varchar(255) | ✗ | Ödülü doğuran inbound event |
| `ledger_reset_entry_id` | uuid | ✗ | Bağlı ledger kaydı (damga doluşunda `stamp_reset`, satın almada `reward_purchase` satırı) |
| `completion_count` | integer | ✗ | Kaçıncı kart doluşu (satın almada 0) |
| `reward_definition_id` | uuid | ✓ | FK → `reward_definitions`; eski kayıtlar için null olabilir |
| `status` | varchar(20) | ✗ | Yaşam döngüsü; `pending` ile başlar (teslimat dış sistemde takip edilir) |
| `created_at` | timestamptz | ✗ | Kazanım anı |
| `delivered_at` | timestamptz | ✓ | Teslim edildi işareti (dış sistem onayıyla güncellenebilir) |

**Kısıt/Index:** `ux_reward_log_source_event (tenant_id, account_type_id, source_event_id)` UNIQUE — aynı event'ten çift ödül engeli; `idx_reward_log_tenant_contact_status`.

## ledger_entries

**Append-only** bakiye hareket defteri — sistemin denetim kaynağı. Kayıtlar güncellenmez/silinmez; her hareket bir satırdır ve bakiye aynı transaction'da güncellenir.

> `PARTITION BY LIST (tenant_id)` — her tenant için partition'ı `provision_tenant.sql` oluşturur. Partition'lı olduğu için tabloda PK ve FK yoktur; teklik `uq_ledger_entries_idempotency_key` ile sağlanır.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | uuid | ✗ | Satır kimliği |
| `tenant_id` | varchar(100) | ✗ | Tenant (partition anahtarı) |
| `customer_account_id` | uuid | ✗ | Hareket eden bakiye (→ `customer_accounts.id`) |
| `contact_key` | varchar(255) | ✗ | Müşteri (denormalize — sorgu kolaylığı) |
| `delta` | numeric(20,4) | ✗ | Hareket miktarı; kazanımda +, düşümde − |
| `reason` | varchar(50) | ✗ | Hareket sebebi — 10 değerli sözlük (aşağıda) |
| `source_event_id` | varchar(255) | ✗ | Harekete sebep olan inbound event / job kimliği |
| `rule_id` | uuid | ✓ | Kazanımı üreten kural (yalnızca rule engine hareketlerinde) |
| `idempotency_key` | varchar(500) | ✗ | Genelde `{eventId}:{reason}` — çift işlem engeli |
| `metadata` | jsonb | ✓ | Harekete özgü ek bilgi (örn. redeem'de `{redeemed_points, cash_amount, rate}`) |
| `created_at` | timestamptz | ✗ | FIFO expire modelinin yaş referansı |

**`reason` sözlüğü:** `earn` (kural kazanımı) · `stamp_earn` (+1 damga) · `stamp_reset` (kart doluşunda sıfırlama) · `cash_load` / `cash_spend` (nakit yükleme/harcama) · `points_redeemed` / `points_redeemed_cash` (puan→nakit dönüşümün iki bacağı) · `refund` (iade geri alımı) · `points_expired` (gece sönmesi) · `reward_purchase` (puanla ödül alımı).

**Index:** `uq_ledger_entries_idempotency_key (tenant_id, idempotency_key)` UNIQUE; `idx_ledger_entries_tenant_account_date (tenant_id, customer_account_id, created_at)` — FIFO/ekstre sorguları; `idx_ledger_entries_tenant_source_event` — iade akışının "orijinal siparişin kazanımları" araması; `IX_ledger_entries_customer_account_id`, `IX_ledger_entries_rule_id`.

## event_inbox

Gelen her event'in kaydı: idempotency kapısı ve hata izi.

> `PARTITION BY LIST (tenant_id)` — partition'ı `provision_tenant.sql` oluşturur.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `event_id` | varchar(255) | ✗ | Zarftaki `eventId` — PK'nın parçası; aynı id ikinci kez gelirse işlenmeden ACK'lenir |
| `tenant_id` | varchar(100) | ✗ | Tenant (partition anahtarı, PK'nın parçası) |
| `event_type` | varchar(50) | ✗ | `order.created`, `points.redeem`, … |
| `payload` | jsonb | ✗ | Zarfın tamamı (yeniden işleme/inceleme için) |
| `received_at` | timestamptz | ✗ | Alınma zamanı |
| `processed_at` | timestamptz | ✓ | İşlenme zamanı |
| `status` | varchar(20) | ✗ | `pending` → `processed` / `failed` (failed olanlar DLQ'ya da düşer) |
| `error` | text | ✓ | Hata detayı (örn. `insufficient_points: 500 < 2000`) |

**PK:** `(tenant_id, event_id)`.

## outbox_events

Transactional outbox: dışarı gidecek bildirimler, iş verisiyle **aynı transaction'da** buraya yazılır; publisher worker RabbitMQ `loyalty.outbound` exchange'ine basar.

| Kolon | Tip | Null | Açıklama |
|-------|-----|------|----------|
| `id` | bigint identity | ✗ | PK (sıralı) |
| `event_id` | uuid | ✗ | Outbound zarfın `eventId`'si — UNIQUE; tüketici dedup anahtarı |
| `tenant_id` | varchar(100) | ✗ | Tenant — routing key'in ilk parçası |
| `event_type` | varchar(100) | ✗ | `loyalty.points.earned`, `loyalty.tier.changed`, … (7 tip) |
| `contact_key` | varchar(255) | ✗ | İlgili müşteri |
| `payload` | jsonb | ✗ | Yayınlanacak zarfın tamamı |
| `dedup_key` | varchar(255) | ✓ | İş olayı tekilleştirme anahtarı (örn. `points_earned:{eventId}`, `expiring:{account_id}:{expires_on}`); üreticiler `ON CONFLICT DO NOTHING` ile yazar |
| `status` | varchar(20) | ✗ | `pending` → `published` / `failed` (8. denemeden sonra) |
| `attempts` | integer | ✗ | Yayın deneme sayısı |
| `next_attempt_at` | timestamptz | ✗ | Bir sonraki deneme zamanı (backoff: 1m → 5m → 30m → 2h → 12h) |
| `published_at` | timestamptz | ✓ | Yayınlanma zamanı; `published` kayıtlar 30 gün sonra purge edilir |
| `created_at` | timestamptz | ✗ | Enqueue zamanı |

**Index:** `idx_outbox_pending_next_attempt (next_attempt_at) WHERE status='pending'` — publisher'ın çalışma kuyruğu; `ux_outbox_dedup (tenant_id, dedup_key) WHERE dedup_key IS NOT NULL` UNIQUE; `ux_outbox_event_id (event_id)` UNIQUE.

## __EFMigrationsHistory

EF Core'un migration takip tablosu (`MigrationId`, `ProductVersion`). Uygulama tarafından yönetilir; elle dokunulmaz.

---

## Partitioning Notu

`ledger_entries` ve `event_inbox` **LIST partition**'dır (`tenant_id`). Ana tabloya yazmadan önce ilgili tenant'ın partition'ı var olmalıdır:

```bash
psql -d loyalty_dev -v tenant_id=acme -f provision_tenant.sql
```

Diğer tüm tablolar partition gerektirmez. Yeni tenant onboarding sırası: şema (bir kez) → partition → tenant seed → RabbitMQ kuyruk/binding.
