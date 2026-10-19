/**
 * What the OpenAPI generator (tools/openapi) can't read from the C# types alone. Every entry names
 * the C# source it mirrors — when that source changes, change the entry in the same change.
 */
export interface Overrides {
  /** `Schema.wireName` → the allowed values of a C# `string` member. */
  stringUnions: Record<string, { values: string[]; source: string }>;
  /** Modules left out of the spec, with the reason. */
  excludeModules: string[];
}

const ROUNDING = {
  values: ['down', 'nearest', 'up'],
  source: 'Shared/Constants/RoundingDirection.cs; ProgramsValidators (RoundingDirection.All)',
};

const QUALIFYING_MODEL = {
  values: ['lifetime', 'periodic'],
  source: "TiersValidators: QualifyingModel must be 'periodic' or 'lifetime'",
};

const CHANGE_TYPE = {
  values: ['created', 'updated', 'deleted', 'published'],
  source: 'Shared/Constants/ConfigChangeType.cs',
};

const GROUP_JOIN = {
  values: ['AND', 'OR'],
  source: 'RuleEngine/Models/ConditionTree.cs (GroupedConditionOps.And / Or)',
};

const RULE_TYPES = [
  'SpendRule',
  'FixedBonusRule',
  'RedemptionRule',
  'TransferRule',
  'ReversalRule',
  'ManualAdjustmentRule',
];

const EVENT_CATEGORY = {
  values: ['Earn', 'Burn', 'Reverse', 'Adjust'],
  source: 'Shared/Events/EventTypes.cs (EventCategory, sent with ToString())',
};

/** Streak campaigns and card buckets: never pending (only CASH rules wait for approval). */
const ENTITY_STATUS = {
  values: ['active', 'disabled', 'deleted'],
  source: 'Shared/Constants/RuleStatus.cs (streak campaigns and card buckets are created active)',
};

const SETTABLE_STATUS = {
  values: ['active', 'disabled'],
  source: 'Set*StatusRequestValidator: Status is RuleStatus.Active or RuleStatus.Disabled',
};

const COUNTRY_MODE = {
  values: ['in', 'not_in'],
  source: "CardBucketsValidators: CountryMode is 'in' or 'not_in'",
};

const INBOX_STATUS = {
  values: ['pending', 'processed', 'failed'],
  source: 'Shared/Constants/InboxStatus.cs',
};

const ADMIN_ROLE = {
  values: ['platform_admin', 'tenant_admin'],
  source: "PlatformValidators: Role is 'platform_admin' or 'tenant_admin'",
};

export const OVERRIDES: Overrides = {
  stringUnions: {
    'ProgramResponse.status': {
      values: ['active', 'inactive', 'deleted'],
      source: 'Shared/Constants/ProgramStatus.cs',
    },
    'ProgramResponse.publicationStatus': {
      values: ['draft', 'published'],
      source: 'Shared/Constants/ProgramPublicationStatus.cs',
    },
    'ProgramResponse.defaultRounding': ROUNDING,
    'CreateProgramRequest.defaultRounding': ROUNDING,
    'UpdateProgramRequest.defaultRounding': ROUNDING,
    'UpdateProgramRequest.status': {
      values: ['active', 'inactive'],
      source: 'ProgramsValidators: Status must be ProgramStatus.Active or ProgramStatus.Inactive',
    },
    'TenantResponse.status': { values: ['active', 'suspended'], source: 'Shared/Constants/TenantStatus.cs' },
    'UpdateTenantRequest.status': {
      values: ['active', 'suspended'],
      source: 'PlatformValidators: Status must be TenantStatus.Active or TenantStatus.Suspended',
    },

    // ── phase 1: account types, tiers, rewards, history ──
    'AccountTypeResponse.type': {
      values: ['POINTS', 'CASH', 'STAMP'],
      source:
        'Shared/Constants/AccountType.cs (enum; STAMP is retired, hidden from the list since CR 2026-10-05)',
    },
    'CreateAccountTypeRequest.type': {
      values: ['POINTS', 'CASH'],
      source: "AccountTypesValidators: Type must be 'POINTS' or 'CASH'",
    },
    'TierResponse.qualifyingModel': QUALIFYING_MODEL,
    'CreateTierRequest.qualifyingModel': QUALIFYING_MODEL,
    'UpdateTierRequest.qualifyingModel': QUALIFYING_MODEL,
    'RewardResponse.acquisition': {
      values: ['points_purchase', 'stamp_completion', 'streak_completion'],
      source: 'Shared/Constants/RewardAcquisition.cs (stamp_completion is retired, read-only)',
    },
    'RewardResponse.rewardType': {
      values: ['points_bonus', 'discount', 'cashback', 'free_product', 'gift_card', 'tier_upgrade'],
      source: 'Shared/Constants/RewardType.cs (only cashback and tier_upgrade are creatable)',
    },
    'RewardResponse.status': {
      values: ['active', 'pending_approval'],
      source: 'Shared/Constants/RewardStatus.cs',
    },
    'CreateRewardRequest.acquisition': {
      values: ['points_purchase', 'streak_completion'],
      source: 'RewardsValidators: RewardAcquisition.IsCreatable (RewardAcquisition.Creatable)',
    },
    'CreateRewardRequest.rewardType': {
      values: ['cashback', 'tier_upgrade'],
      source: 'RewardsValidators + RewardType.Creatable',
    },
    'ConfigVersionSummary.changeType': CHANGE_TYPE,
    'ConfigVersionDetail.changeType': CHANGE_TYPE,

    // ── phase 2: rules, streak campaigns, card buckets ──
    'ConditionTree.op': GROUP_JOIN,
    'ConditionGroup.op': GROUP_JOIN,
    'ConditionLeaf.operator': {
      values: ['gte', 'lte', 'between', 'eq', 'neq', 'in', 'not_in', 'starts_with', 'exists', 'is_null'],
      source: 'RuleEngine/Models/ConditionTree.cs (GroupedConditionOps.All)',
    },
    'ConditionValue.type': {
      values: ['money', 'number', 'string', 'string[]', 'none'],
      source: 'RuleEngine/Models/ConditionTree.cs (ConditionValue.Type remark)',
    },
    'ConditionClause.op': {
      values: ['eq', 'ne', 'in', 'gte', 'lte', 'gt', 'lt', 'exists', 'occurred_within'],
      source: 'RuleEngine/Models/ConditionClause.cs (ConditionOps.All)',
    },
    'RuleResponse.type': {
      values: [...RULE_TYPES, 'StampRule', 'ExpiryRule'],
      source: 'Shared/Constants/RuleTypes.cs (StampRule and ExpiryRule are retired, read-only)',
    },
    'CreateRuleRequest.type': {
      values: RULE_TYPES,
      source: 'RulesValidators: RuleTypes.All.Contains(Type)',
    },
    'RuleResponse.status': {
      values: ['active', 'disabled', 'deleted', 'pending_approval'],
      source: 'Shared/Constants/RuleStatus.cs',
    },
    'SetRuleStatusRequest.status': SETTABLE_STATUS,
    'RuleCalculation.mode': {
      values: ['proportional', 'full'],
      source: "RulesValidators: Calculation.Mode is 'proportional' or 'full'",
    },
    'RuleCalculation.allowNegative': {
      values: ['allow negative', 'clamp to zero'],
      source: "RulesValidators: Calculation.AllowNegative is 'allow negative' or 'clamp to zero'",
    },
    'RuleCalculation.reason': {
      values: ['goodwill', 'correction', 'dispute', 'migration'],
      source: 'RulesValidators: Calculation.Reason',
    },
    'RuleLimits.period': {
      values: ['Day', 'Week', 'Month', 'Year'],
      source: 'RulesValidators (RuleLimitsValidator): Period',
    },
    'RuleLimits.reset_window': {
      values: ['Calendar', 'Rolling'],
      source: 'RulesValidators (RuleLimitsValidator): ResetWindow',
    },
    'RuleLimits.on_breach': {
      values: ['Clamp', 'Skip'],
      source: 'RulesValidators (RuleLimitsValidator): OnBreach; RuleEngine/Models/RuleLimits.cs',
    },
    'RuleSettings.rounding': ROUNDING,
    'RuleSettings.posting': {
      values: ['Immediate', 'Delayed'],
      source: "RulesValidators: Configuration.Posting is 'Immediate' or 'Delayed'",
    },
    'EventMetadataResponse.category': EVENT_CATEGORY,
    'RuleTypeMetadataResponse.category': EVENT_CATEGORY,
    'EventMetadataResponse.compatibleRuleTypes': {
      values: RULE_TYPES,
      source: 'RuleEngine/Metadata/RuleTypeCatalog.cs',
    },
    'ApplicableFieldsResponse.ruleType': {
      values: RULE_TYPES,
      source: 'RuleEngine/Metadata/RuleFieldCatalog.cs',
    },
    'EventFieldMetadata.kind': {
      values: ['Money', 'Number', 'String'],
      source: 'Shared/Events/EventTypes.cs (EventFieldKind, sent with ToString())',
    },
    'StreakCampaignResponse.status': ENTITY_STATUS,
    'SetStreakCampaignStatusRequest.status': SETTABLE_STATUS,
    'StreakConfig.period': {
      values: ['day', 'week', 'month'],
      source: 'RuleEngine/Campaigns/Streak/StreakConfig.cs (StreakPeriod)',
    },
    'StreakConfig.week_start': {
      values: ['monday', 'sunday'],
      source: 'RuleEngine/Campaigns/Streak/StreakConfig.cs (StreakWeekStart)',
    },
    'StreakConfig.on_complete': {
      values: ['restart', 'stop'],
      source: 'RuleEngine/Campaigns/Streak/StreakConfig.cs (StreakOnComplete)',
    },
    'StreakAggregate.metric': {
      values: ['sum', 'count'],
      source: 'RuleEngine/Campaigns/Streak/StreakConfig.cs (StreakMetric)',
    },
    'StreakReward.kind': {
      values: ['fixed_bonus', 'reward_definition'],
      source: 'RuleEngine/Campaigns/Streak/StreakConfig.cs (StreakRewardKind)',
    },
    'CardBucketResponse.status': ENTITY_STATUS,
    'SetCardBucketStatusRequest.status': SETTABLE_STATUS,
    'CardBucketResponse.countryMode': COUNTRY_MODE,
    'CreateCardBucketRequest.countryMode': COUNTRY_MODE,
    'UpdateCardBucketRequest.countryMode': COUNTRY_MODE,

    // ── phase 3: customers, Customer 360 ──
    'CustomerEventResponse.status': INBOX_STATUS,
    'InboundEventResponse.status': INBOX_STATUS,
    'SentMessageResponse.status': {
      values: ['pending', 'published', 'failed'],
      source: 'Shared/Constants/OutboxStatus.cs',
    },
    'CustomerRewardResponse.outcome': {
      values: ['cash_credited', 'tier_upgraded', 'none'],
      source: 'Customers/CustomersAppService.cs (reward outcome)',
    },
    'TierHistoryEntryResponse.cause': {
      values: ['points', 'reward', 'downgrade'],
      source: 'Customers/CustomerViewRules.cs (TierChangeCause)',
    },

    // ── phase 4: platform ──
    'AdminUserResponse.role': ADMIN_ROLE,
    'CreateAdminUserRequest.role': ADMIN_ROLE,
  },
  excludeModules: [
    // Health checks (`/api/v1/ping`, `/api/v1/ping/db`): they return anonymous objects, and the
    // portal never calls them.
    'PingModule',
  ],
};
