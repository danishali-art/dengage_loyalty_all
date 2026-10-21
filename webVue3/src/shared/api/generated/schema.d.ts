/**
 * GENERATED from openapi/openapi.json by `npm run api:types` — do not edit.
 * The spec itself is generated from the backend C# source (`npm run api:spec`).
 */

export interface paths {
    "/api/v1/auth/login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["auth_post_auth_login"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/platform/tenants": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["tenants_get_platform_tenants"];
        put?: never;
        post: operations["tenants_post_platform_tenants"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/platform/tenants/{tenantId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["tenants_get_platform_tenants_tenantId"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: operations["tenants_patch_platform_tenants_tenantId"];
        trace?: never;
    };
    "/api/v1/platform/tenants/{tenantId}/admin-users": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["tenants_get_platform_tenants_tenantId_admin_users"];
        put?: never;
        post: operations["tenants_post_platform_tenants_tenantId_admin_users"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/platform/tenants/{tenantId}/api-keys": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["tenants_get_platform_tenants_tenantId_api_keys"];
        put?: never;
        post: operations["tenants_post_platform_tenants_tenantId_api_keys"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/platform/tenants/{tenantId}/api-keys/{keyId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: operations["tenants_delete_platform_tenants_tenantId_api_keys_keyId"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/config-versions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["configVersions_get_tenants_tenantId_config_versions"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/config-versions/{versionId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["configVersions_get_tenants_tenantId_config_versions_versionId"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/cap-usage": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_cap_usage"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/card-buckets": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_card_buckets"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/events": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_events"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/events/{eventId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_events_eventId"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/ledger": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_ledger"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/messages": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_messages"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/rewards": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_rewards"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/rule-fires": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_rule_fires"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/streaks": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_streaks"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/customers/{contactKey}/tier-history": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["customers_get_tenants_tenantId_customers_contactKey_tier_history"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/dashboard/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["dashboard_get_tenants_tenantId_dashboard_summary"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/events/{eventId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["events_get_tenants_tenantId_events_eventId"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/events/simulate": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["events_post_tenants_tenantId_events_simulate"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/events/types": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["events_get_tenants_tenantId_events_types"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["programs_get_tenants_tenantId_programs"];
        put?: never;
        post: operations["programs_post_tenants_tenantId_programs"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["programs_get_tenants_tenantId_programs_programId"];
        put?: never;
        post?: never;
        delete: operations["programs_delete_tenants_tenantId_programs_programId"];
        options?: never;
        head?: never;
        patch: operations["programs_patch_tenants_tenantId_programs_programId"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/account-types": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["accountTypes_get_tenants_tenantId_programs_programId_account_types"];
        put?: never;
        post: operations["accountTypes_post_tenants_tenantId_programs_programId_account_types"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/account-types/{accountTypeId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["accountTypes_get_tenants_tenantId_programs_programId_account_types_accountTypeId"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: operations["accountTypes_patch_tenants_tenantId_programs_programId_account_types_accountTypeId"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["cardBuckets_get_tenants_tenantId_programs_programId_card_buckets"];
        put?: never;
        post: operations["cardBuckets_post_tenants_tenantId_programs_programId_card_buckets"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets/{bucketId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["cardBuckets_get_tenants_tenantId_programs_programId_card_buckets_bucketId"];
        put?: never;
        post?: never;
        delete: operations["cardBuckets_delete_tenants_tenantId_programs_programId_card_buckets_bucketId"];
        options?: never;
        head?: never;
        patch: operations["cardBuckets_patch_tenants_tenantId_programs_programId_card_buckets_bucketId"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/card-buckets/{bucketId}/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: operations["cardBuckets_patch_tenants_tenantId_programs_programId_card_buckets_bucketId_status"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/publish": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["programs_post_tenants_tenantId_programs_programId_publish"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/rewards": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["rewards_get_tenants_tenantId_programs_programId_rewards"];
        put?: never;
        post: operations["rewards_post_tenants_tenantId_programs_programId_rewards"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/rewards/{rewardId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: operations["rewards_delete_tenants_tenantId_programs_programId_rewards_rewardId"];
        options?: never;
        head?: never;
        patch: operations["rewards_patch_tenants_tenantId_programs_programId_rewards_rewardId"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/rewards/{rewardId}/active": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: operations["rewards_patch_tenants_tenantId_programs_programId_rewards_rewardId_active"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/rewards/{rewardId}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: operations["rewards_patch_tenants_tenantId_programs_programId_rewards_rewardId_approve"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/rules": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["rules_get_tenants_tenantId_programs_programId_rules"];
        put?: never;
        post: operations["rules_post_tenants_tenantId_programs_programId_rules"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/rules/{ruleId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["rules_get_tenants_tenantId_programs_programId_rules_ruleId"];
        put?: never;
        post?: never;
        delete: operations["rules_delete_tenants_tenantId_programs_programId_rules_ruleId"];
        options?: never;
        head?: never;
        patch: operations["rules_patch_tenants_tenantId_programs_programId_rules_ruleId"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/rules/{ruleId}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: operations["rules_patch_tenants_tenantId_programs_programId_rules_ruleId_approve"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/rules/{ruleId}/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: operations["rules_patch_tenants_tenantId_programs_programId_rules_ruleId_status"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/rules/metadata": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["rules_get_tenants_tenantId_programs_programId_rules_metadata"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["streakCampaigns_get_tenants_tenantId_programs_programId_streak_campaigns"];
        put?: never;
        post: operations["streakCampaigns_post_tenants_tenantId_programs_programId_streak_campaigns"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns/{campaignId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["streakCampaigns_get_tenants_tenantId_programs_programId_streak_campaigns_campaignId"];
        put?: never;
        post?: never;
        delete: operations["streakCampaigns_delete_tenants_tenantId_programs_programId_streak_campaigns_campaignId"];
        options?: never;
        head?: never;
        patch: operations["streakCampaigns_patch_tenants_tenantId_programs_programId_streak_campaigns_campaignId"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/streak-campaigns/{campaignId}/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: operations["streakCampaigns_patch_tenants_tenantId_programs_programId_streak_campaigns_campaignId_status"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/tiers": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["tiers_get_tenants_tenantId_programs_programId_tiers"];
        put?: never;
        post: operations["tiers_post_tenants_tenantId_programs_programId_tiers"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/tiers/{tierId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: operations["tiers_delete_tenants_tenantId_programs_programId_tiers_tierId"];
        options?: never;
        head?: never;
        patch: operations["tiers_patch_tenants_tenantId_programs_programId_tiers_tierId"];
        trace?: never;
    };
    "/api/v1/tenants/{tenantId}/programs/{programId}/tiers/reorder": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["tiers_post_tenants_tenantId_programs_programId_tiers_reorder"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        AccountBalanceResponse: {
            /** Format: uuid */
            accountTypeId: string;
            accountTypeName: string;
            accountTypeType: string;
            /** Format: decimal */
            balance: string;
            /** Format: int32 */
            expirationDays?: number | null;
            /** Format: uuid */
            programId: string;
            programName: string;
        };
        AccountTypeResponse: {
            config: unknown;
            /** Format: date-time */
            createdAt: string;
            /** Format: uuid */
            id: string;
            isTierQualifying: boolean;
            name: string;
            /** @enum {string} */
            type: "POINTS" | "CASH" | "STAMP";
        };
        AdminUserResponse: {
            /** Format: date-time */
            createdAt: string;
            email: string;
            /** Format: uuid */
            id: string;
            /** @enum {string} */
            role: "platform_admin" | "tenant_admin";
            status: string;
            tenantId?: string | null;
        };
        ApiKeyResponse: {
            /** Format: date-time */
            createdAt: string;
            /** Format: uuid */
            id: string;
            /** Format: date-time */
            lastUsedAt?: string | null;
            prefix: string;
            /** Format: date-time */
            revokedAt?: string | null;
        };
        ApplicableFieldsResponse: {
            configuration: string[];
            limits: string[];
            /** @enum {string} */
            ruleType: "SpendRule" | "FixedBonusRule" | "RedemptionRule" | "TransferRule" | "ReversalRule" | "ManualAdjustmentRule";
        };
        CardBucketResponse: {
            /** Format: date-time */
            activeFrom?: string | null;
            /** Format: date-time */
            activeTo?: string | null;
            additionalConditions?: components["schemas"]["ConditionLeaf"][] | null;
            /** Format: decimal */
            amountMax?: string | null;
            /** Format: decimal */
            amountMin?: string | null;
            countries?: string[] | null;
            /** @enum {string|null} */
            countryMode?: "in" | "not_in" | null;
            /** Format: date-time */
            createdAt: string;
            daysOfWeek?: string[] | null;
            /** Format: int32 */
            hourFrom?: number | null;
            /** Format: int32 */
            hourTo?: number | null;
            /** Format: uuid */
            id: string;
            mccCodes?: string[] | null;
            name: string;
            /** Format: decimal */
            perCustomerPerDay?: string | null;
            /** Format: decimal */
            perCustomerTotal?: string | null;
            /** Format: int32 */
            priority: number;
            requireCaptured: boolean;
            /** Format: decimal */
            rewardAmount: string;
            /** @enum {string} */
            status: "active" | "disabled" | "deleted";
            /** Format: uuid */
            targetAccountTypeId: string;
            /** Format: date-time */
            updatedAt: string;
        };
        ConditionClause: {
            field: string;
            /** @enum {string} */
            op: "eq" | "ne" | "in" | "gte" | "lte" | "gt" | "lt" | "exists" | "occurred_within";
            value: unknown;
        };
        ConditionGroup: {
            conditions: components["schemas"]["ConditionLeaf"][];
            /** @enum {string} */
            op: "AND" | "OR";
        };
        ConditionLeaf: {
            field: string;
            /** @enum {string} */
            operator: "gte" | "lte" | "between" | "eq" | "neq" | "in" | "not_in" | "starts_with" | "exists" | "is_null";
            value: components["schemas"]["ConditionValue"];
        };
        ConditionTree: {
            groups: components["schemas"]["ConditionGroup"][];
            /** @enum {string} */
            op: "AND" | "OR";
        };
        ConditionValue: {
            currency?: string | null;
            data: unknown;
            inferred?: boolean | null;
            /** @enum {string} */
            type: "money" | "number" | "string" | "string[]" | "none";
        };
        ConfigVersionDetail: {
            /** Format: date-time */
            changedAt: string;
            changedBy: string;
            changeSummary?: string | null;
            /** @enum {string} */
            changeType: "created" | "updated" | "deleted" | "published";
            /** Format: uuid */
            entityId: string;
            entityType: string;
            /** Format: uuid */
            id: string;
            snapshot: string;
            /** Format: int32 */
            versionNumber: number;
        };
        ConfigVersionSummary: {
            /** Format: date-time */
            changedAt: string;
            changedBy: string;
            changeSummary?: string | null;
            /** @enum {string} */
            changeType: "created" | "updated" | "deleted" | "published";
            /** Format: uuid */
            entityId: string;
            entityType: string;
            /** Format: uuid */
            id: string;
            /** Format: int32 */
            versionNumber: number;
        };
        CreateAccountTypeRequest: {
            config: unknown;
            isTierQualifying?: boolean | null;
            name: string;
            /** @enum {string} */
            type: "POINTS" | "CASH";
        };
        CreateAdminUserRequest: {
            email: string;
            password: string;
            /** @enum {string} */
            role: "platform_admin" | "tenant_admin";
        };
        CreateApiKeyResponse: {
            /** Format: date-time */
            createdAt: string;
            /** Format: uuid */
            id: string;
            prefix: string;
            rawKey: string;
        };
        CreateCardBucketRequest: {
            /** Format: date-time */
            activeFrom?: string | null;
            /** Format: date-time */
            activeTo?: string | null;
            additionalConditions?: components["schemas"]["ConditionLeaf"][] | null;
            /** Format: decimal */
            amountMax?: string | null;
            /** Format: decimal */
            amountMin?: string | null;
            countries?: string[] | null;
            /** @enum {string|null} */
            countryMode?: "in" | "not_in" | null;
            daysOfWeek?: string[] | null;
            /** Format: int32 */
            hourFrom?: number | null;
            /** Format: int32 */
            hourTo?: number | null;
            mccCodes?: string[] | null;
            name: string;
            /** Format: decimal */
            perCustomerPerDay?: string | null;
            /** Format: decimal */
            perCustomerTotal?: string | null;
            /** Format: int32 */
            priority: number;
            requireCaptured: boolean;
            /** Format: decimal */
            rewardAmount: string;
            /** Format: uuid */
            targetAccountTypeId: string;
        };
        CreateProgramRequest: {
            /** @enum {string|null} */
            defaultRounding?: "down" | "nearest" | "up" | null;
            description?: string | null;
            name: string;
            /** Format: uuid */
            qualifyingAccountTypeId?: string | null;
            slug?: string | null;
            status?: string | null;
            /** Format: int32 */
            warningDays?: number | null;
        };
        CreateRewardRequest: {
            /** @enum {string} */
            acquisition: "points_purchase" | "streak_completion";
            displayName: string;
            isActive: boolean;
            name: string;
            /** Format: uuid */
            pointsAccountTypeId?: string | null;
            /** Format: decimal */
            pointsPrice?: string | null;
            /** @enum {string} */
            rewardType: "cashback" | "tier_upgrade";
            typeConfig?: unknown;
        };
        CreateRuleRequest: {
            /** Format: date-time */
            activeFrom?: string | null;
            /** Format: date-time */
            activeTo?: string | null;
            calculation?: components["schemas"]["RuleCalculation"] | null;
            conditions?: components["schemas"]["ConditionTree"] | null;
            configuration?: components["schemas"]["RuleSettings"] | null;
            exclusivityGroup?: string | null;
            limits?: components["schemas"]["RuleLimits"] | null;
            name: string;
            /** Format: int32 */
            priority: number;
            stackable: boolean;
            stackMode?: string | null;
            /** Format: uuid */
            targetAccountTypeId?: string | null;
            trigger: string;
            /** @enum {string} */
            type: "SpendRule" | "FixedBonusRule" | "RedemptionRule" | "TransferRule" | "ReversalRule" | "ManualAdjustmentRule";
        };
        CreateStreakCampaignRequest: {
            /** Format: date-time */
            activeFrom?: string | null;
            /** Format: date-time */
            activeTo?: string | null;
            conditions?: components["schemas"]["ConditionClause"][] | null;
            config: components["schemas"]["StreakConfig"];
            name: string;
            /** Format: uuid */
            targetAccountTypeId: string;
            trigger: string;
        };
        CreateTenantRequest: {
            id: string;
            name: string;
        };
        CreateTierRequest: {
            displayName: string;
            /** Format: int32 */
            graceDays: number;
            /** Format: decimal */
            minPoints: string;
            name: string;
            /** @enum {string} */
            qualifyingModel: "lifetime" | "periodic";
            /** Format: int32 */
            qualifyingPeriodDays?: number | null;
            /** Format: int32 */
            sortOrder: number;
        };
        CursorPage_CustomerEventResponse: {
            data: components["schemas"]["CustomerEventResponse"][];
            nextCursor?: string | null;
            /** Format: int32 */
            total?: number | null;
        };
        CursorPage_CustomerRuleFireResponse: {
            data: components["schemas"]["CustomerRuleFireResponse"][];
            nextCursor?: string | null;
            /** Format: int32 */
            total?: number | null;
        };
        CursorPage_LedgerEntryResponse: {
            data: components["schemas"]["LedgerEntryResponse"][];
            nextCursor?: string | null;
            /** Format: int32 */
            total?: number | null;
        };
        CursorPage_SentMessageResponse: {
            data: components["schemas"]["SentMessageResponse"][];
            nextCursor?: string | null;
            /** Format: int32 */
            total?: number | null;
        };
        CustomerCardBucketResponse: {
            /** Format: date-time */
            lastPostedAt?: string | null;
            name: string;
            /** Format: decimal */
            perCustomerPerDay?: string | null;
            /** Format: decimal */
            perCustomerTotal?: string | null;
            /** Format: int32 */
            postings: number;
            /** Format: uuid */
            programId: string;
            programName: string;
            /** Format: decimal */
            rewardAmount?: string | null;
            /** Format: uuid */
            ruleId: string;
            status: string;
            /** Format: decimal */
            usedToday: string;
            /** Format: decimal */
            usedTotal: string;
        };
        CustomerEventDetailResponse: {
            event?: components["schemas"]["InboundEventResponse"] | null;
            eventId: string;
            heldPostings: components["schemas"]["HeldPostingResponse"][];
            messages: components["schemas"]["SentMessageResponse"][];
            onceOnlySkips?: components["schemas"]["OnceOnlySkipResponse"][] | null;
            postings: components["schemas"]["EventPostingResponse"][];
            rewards: components["schemas"]["RewardLogResponse"][];
            ruleFires: components["schemas"]["RuleFireResponse"][];
            streakCompletions: components["schemas"]["StreakCompletionResponse"][];
            streaksApplied: components["schemas"]["StreakAppliedResponse"][];
            tierChanges: components["schemas"]["TierChangeResponse"][];
        };
        CustomerEventResponse: {
            error?: string | null;
            eventId: string;
            eventType: string;
            /** Format: date-time */
            occurredAt?: string | null;
            outcome: components["schemas"]["EventWalletOutcomeResponse"][];
            /** Format: date-time */
            processedAt?: string | null;
            /** Format: date-time */
            receivedAt: string;
            /** @enum {string} */
            status: "pending" | "processed" | "failed";
        };
        CustomerProfileResponse: {
            balances: components["schemas"]["AccountBalanceResponse"][];
            contactKey: string;
            programs?: components["schemas"]["ProgramOverviewResponse"][] | null;
            summary?: components["schemas"]["CustomerSummaryInfoResponse"] | null;
            tierProgress?: components["schemas"]["TierProgressResponse"] | null;
        };
        CustomerRewardResponse: {
            /** Format: decimal */
            cashAmount?: string | null;
            cashWalletName?: string | null;
            /** Format: decimal */
            cost?: string | null;
            costWalletName?: string | null;
            /** Format: date-time */
            createdAt: string;
            /** @enum {string} */
            outcome: "cash_credited" | "tier_upgraded" | "none";
            /** Format: uuid */
            rewardDefinitionId?: string | null;
            rewardName: string;
            rewardType?: string | null;
            source: string;
            sourceEventId: string;
            status?: string | null;
            tierName?: string | null;
        };
        CustomerRuleFireResponse: {
            calculationSnapshot: string;
            conditionsSnapshot?: string | null;
            /** Format: date-time */
            createdAt: string;
            eventType?: string | null;
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            ledgerEntryId?: string | null;
            resolutionSnapshot?: string | null;
            /** Format: decimal */
            resultingDelta: string;
            /** Format: uuid */
            ruleId: string;
            ruleName?: string | null;
            /** Format: int32 */
            ruleVersion: number;
            sourceEventId: string;
        };
        CustomerStreakResponse: {
            /** Format: uuid */
            campaignId: string;
            campaignName: string;
            campaignStatus: string;
            /** Format: int32 */
            completions: number;
            history: components["schemas"]["StreakCompletionResponse"][];
            /** Format: date */
            lastMetPeriod?: string | null;
            /** Format: int32 */
            latestAggCount?: number | null;
            /** Format: decimal */
            latestAggSum?: string | null;
            latestPeriodMet?: boolean | null;
            /** Format: date */
            latestPeriodStart?: string | null;
            metric: string;
            period: string;
            /** Format: uuid */
            programId: string;
            programName: string;
            rewardKind: string;
            status: string;
            /** Format: int32 */
            streakCount: number;
            /** Format: int32 */
            targetPeriods: number;
            /** Format: decimal */
            threshold: string;
        };
        CustomerSummaryInfoResponse: {
            /** Format: int32 */
            activeStreakCount: number;
            /** Format: int32 */
            failedEventsLast7Days: number;
            /** Format: date-time */
            firstSeenAt?: string | null;
            /** Format: date-time */
            lastActivityAt: string;
        };
        CustomerSummaryResponse: {
            /** Format: int32 */
            accountCount: number;
            contactKey: string;
            /** Format: date-time */
            lastActivityAt: string;
        };
        DashboardSummaryResponse: {
            /** Format: int32 */
            customerAccountCount: number;
            /** Format: int32 */
            programCount: number;
            /** Format: int32 */
            redemptionCount: number;
            /** Format: int32 */
            streakActiveCount: number;
            /** Format: int32 */
            streakCompletedInRange: number;
            /** Format: int32 */
            tierCount: number;
            /** Format: decimal */
            totalBalance: string;
        };
        EventAcceptedResponse: {
            eventId: string;
            status: string;
        };
        EventFieldMetadata: {
            /** @enum {string} */
            kind: "Money" | "Number" | "String";
            path: string;
        };
        EventMetadataResponse: {
            applicableFields?: components["schemas"]["ApplicableFieldsResponse"][] | null;
            cardinality: string;
            /** @enum {string} */
            category: "Earn" | "Burn" | "Reverse" | "Adjust";
            compatibleRuleTypes: ("SpendRule" | "FixedBonusRule" | "RedemptionRule" | "TransferRule" | "ReversalRule" | "ManualAdjustmentRule")[];
            eventType: string;
            fields: components["schemas"]["EventFieldMetadata"][];
            period?: string | null;
            source: string;
        };
        EventPostingResponse: {
            /** Format: uuid */
            accountTypeId: string;
            accountTypeName: string;
            accountTypeType: string;
            /** Format: uuid */
            campaignId?: string | null;
            campaignName?: string | null;
            contactKey: string;
            /** Format: date-time */
            createdAt: string;
            /** Format: decimal */
            delta: string;
            /** Format: uuid */
            id: string;
            metadata?: string | null;
            /** Format: uuid */
            programId: string;
            programName: string;
            reason: string;
            /** Format: uuid */
            ruleId?: string | null;
            ruleName?: string | null;
            /** Format: int32 */
            ruleVersion?: number | null;
        };
        EventStatusResponse: {
            error?: string | null;
            eventId: string;
            eventType: string;
            /** Format: date-time */
            processedAt?: string | null;
            /** Format: date-time */
            receivedAt: string;
            status: string;
        };
        EventTypesResponse: {
            builtIn: string[];
            generic: string[];
            publishable: string[];
        };
        EventWalletOutcomeResponse: {
            /** Format: uuid */
            accountTypeId: string;
            accountTypeName: string;
            accountTypeType: string;
            /** Format: decimal */
            netDelta: string;
            /** Format: int32 */
            postings: number;
        };
        GenericEventRequest: {
            data: unknown;
            eventType: string;
        };
        HeldPostingResponse: {
            /** Format: uuid */
            accountTypeId: string;
            accountTypeName: string;
            /** Format: date-time */
            cancelledAt?: string | null;
            /** Format: decimal */
            delta: string;
            /** Format: date-time */
            holdUntil: string;
            /** Format: uuid */
            id: string;
            /** Format: date-time */
            postedAt?: string | null;
            reason: string;
            /** Format: decimal */
            refundedDelta?: string | null;
            /** Format: uuid */
            ruleId: string;
            ruleName?: string | null;
        };
        InboundEventResponse: {
            data?: unknown;
            error?: string | null;
            eventId: string;
            eventType: string;
            /** Format: date-time */
            occurredAt?: string | null;
            /** Format: date-time */
            processedAt?: string | null;
            /** Format: date-time */
            receivedAt: string;
            /** @enum {string} */
            status: "pending" | "processed" | "failed";
        };
        LedgerEntryResponse: {
            /** Format: uuid */
            accountTypeId: string;
            accountTypeName: string;
            accountTypeType: string;
            /** Format: uuid */
            campaignId?: string | null;
            campaignName?: string | null;
            /** Format: date-time */
            createdAt: string;
            /** Format: decimal */
            delta: string;
            eventType?: string | null;
            /** Format: uuid */
            id: string;
            metadata?: string | null;
            /** Format: uuid */
            programId: string;
            programName: string;
            reason: string;
            /** Format: uuid */
            ruleId?: string | null;
            ruleName?: string | null;
            /** Format: int32 */
            ruleVersion?: number | null;
            sourceEventId: string;
        };
        LoginRequest: {
            email: string;
            password: string;
        };
        LoginResponse: {
            /** Format: int32 */
            expiresInSeconds: number;
            token: string;
            tokenType: string;
        };
        OnceOnlySkipResponse: {
            /** Format: date-time */
            earlierAt: string;
            earlierEventId: string;
            /** Format: uuid */
            ruleId: string;
            ruleName?: string | null;
        };
        PagedResult_AccountTypeResponse: {
            data: components["schemas"]["AccountTypeResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_AdminUserResponse: {
            data: components["schemas"]["AdminUserResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_ApiKeyResponse: {
            data: components["schemas"]["ApiKeyResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_CardBucketResponse: {
            data: components["schemas"]["CardBucketResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_ConfigVersionSummary: {
            data: components["schemas"]["ConfigVersionSummary"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_CustomerSummaryResponse: {
            data: components["schemas"]["CustomerSummaryResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_ProgramResponse: {
            data: components["schemas"]["ProgramResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_RewardResponse: {
            data: components["schemas"]["RewardResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_RuleResponse: {
            data: components["schemas"]["RuleResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_StreakCampaignResponse: {
            data: components["schemas"]["StreakCampaignResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_TenantResponse: {
            data: components["schemas"]["TenantResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        PagedResult_TierResponse: {
            data: components["schemas"]["TierResponse"][];
            /** Format: int32 */
            page: number;
            /** Format: int32 */
            pageSize: number;
            /** Format: int32 */
            total: number;
        };
        ProblemDetailsBody: {
            code: string;
            errors?: {
                [key: string]: string[];
            } | null;
            /** Format: int32 */
            status: number;
            title: string;
            traceId: string;
            type: string;
        };
        ProgramOverviewResponse: {
            /** Format: uuid */
            programId: string;
            programName: string;
            streaks: components["schemas"]["StreakSummaryResponse"][];
            tier?: components["schemas"]["ProgramTierStatusResponse"] | null;
            wallets: components["schemas"]["WalletOverviewResponse"][];
        };
        ProgramResponse: {
            /** Format: int32 */
            accountTypeCount: number;
            /** Format: date-time */
            createdAt: string;
            /** @enum {string} */
            defaultRounding: "down" | "nearest" | "up";
            description?: string | null;
            hasUnpublishedChanges: boolean;
            /** Format: uuid */
            id: string;
            name: string;
            /** @enum {string} */
            publicationStatus: "draft" | "published";
            /** Format: date-time */
            publishedAt?: string | null;
            publishedBy?: string | null;
            /** Format: int32 */
            publishedVersion?: number | null;
            /** Format: uuid */
            qualifyingAccountTypeId?: string | null;
            /** Format: int32 */
            ruleCount: number;
            slug: string;
            /** @enum {string} */
            status: "active" | "inactive" | "deleted";
            /** Format: int32 */
            warningDays?: number | null;
        };
        ProgramTierStatusResponse: {
            /** Format: date */
            graceEndsAt?: string | null;
            /** Format: date */
            lockedUntil?: string | null;
            nextTierDisplayName?: string | null;
            /** Format: decimal */
            nextTierMinPoints?: string | null;
            nextTierName?: string | null;
            /** Format: date */
            periodStart?: string | null;
            /** Format: decimal */
            qualifyingPoints: string;
            tierDisplayName?: string | null;
            tierName?: string | null;
        };
        ReorderTiersRequest: {
            tierIds: string[];
        };
        RewardLogResponse: {
            /** Format: int32 */
            completionCount: number;
            /** Format: date-time */
            createdAt: string;
            /** Format: date-time */
            deliveredAt?: string | null;
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            rewardDefinitionId?: string | null;
            rewardName: string;
            status: string;
        };
        RewardResponse: {
            /** @enum {string} */
            acquisition: "points_purchase" | "stamp_completion" | "streak_completion";
            approvedBy?: string | null;
            /** Format: date-time */
            createdAt: string;
            createdBy?: string | null;
            displayName: string;
            /** Format: uuid */
            id: string;
            isActive: boolean;
            name: string;
            /** Format: uuid */
            pointsAccountTypeId?: string | null;
            /** Format: decimal */
            pointsPrice?: string | null;
            /** @enum {string} */
            rewardType: "points_bonus" | "discount" | "cashback" | "free_product" | "gift_card" | "tier_upgrade";
            /** @enum {string} */
            status: "active" | "pending_approval";
            typeConfig: unknown;
        };
        RuleCalculation: {
            /** @enum {string|null} */
            allowNegative?: "allow negative" | "clamp to zero" | null;
            /** Format: decimal */
            amount?: string | null;
            /** Format: uuid */
            cashAccountTypeId?: string | null;
            /** Format: decimal */
            fee?: string | null;
            /** Format: decimal */
            maxPerDay?: string | null;
            /** Format: decimal */
            minRedeem?: string | null;
            /** @enum {string|null} */
            mode?: "proportional" | "full" | null;
            /** Format: decimal */
            rate?: string | null;
            /** Format: decimal */
            ratio?: string | null;
            /** @enum {string|null} */
            reason?: "goodwill" | "correction" | "dispute" | "migration" | null;
        };
        RuleCapUsageResponse: {
            isCardBucket: boolean;
            /** Format: decimal */
            perCustomerPerDay?: string | null;
            /** Format: decimal */
            perCustomerPerPeriod?: string | null;
            /** Format: decimal */
            perCustomerTotal?: string | null;
            period?: string | null;
            /** Format: date-time */
            periodStart?: string | null;
            /** Format: uuid */
            programId: string;
            programName: string;
            resetWindow?: string | null;
            /** Format: uuid */
            ruleId: string;
            ruleName: string;
            status: string;
            /** Format: decimal */
            usedThisPeriod?: string | null;
            /** Format: decimal */
            usedToday: string;
            /** Format: decimal */
            usedTotal: string;
        };
        RuleFireResponse: {
            calculationSnapshot: string;
            conditionsSnapshot?: string | null;
            /** Format: date-time */
            createdAt: string;
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            ledgerEntryId?: string | null;
            resolutionSnapshot?: string | null;
            /** Format: decimal */
            resultingDelta: string;
            /** Format: uuid */
            ruleId: string;
            ruleName?: string | null;
            /** Format: int32 */
            ruleVersion: number;
        };
        RuleLimits: {
            /** Format: decimal */
            cooldown_hours?: string | null;
            /** Format: int32 */
            max_customers?: number | null;
            /** Format: decimal */
            max_per_event?: string | null;
            /** Format: decimal */
            min_event_amount?: string | null;
            /** @enum {string} */
            on_breach: "Clamp" | "Skip";
            /** Format: decimal */
            per_customer_per_day?: string | null;
            /** Format: decimal */
            per_customer_per_period?: string | null;
            /** Format: decimal */
            per_customer_total?: string | null;
            /** @enum {string|null} */
            period?: "Day" | "Week" | "Month" | "Year" | null;
            /** @enum {string|null} */
            reset_window?: "Calendar" | "Rolling" | null;
            /** Format: decimal */
            rule_budget_per_period?: string | null;
            /** Format: decimal */
            rule_budget_total?: string | null;
        };
        RuleResponse: {
            /** Format: date-time */
            activeFrom?: string | null;
            /** Format: date-time */
            activeTo?: string | null;
            approvedBy?: string | null;
            calculation?: components["schemas"]["RuleCalculation"] | null;
            conditions?: components["schemas"]["ConditionTree"] | null;
            configuration?: components["schemas"]["RuleSettings"] | null;
            /** Format: date-time */
            createdAt: string;
            createdBy?: string | null;
            exclusivityGroup?: string | null;
            /** Format: uuid */
            id: string;
            limits?: components["schemas"]["RuleLimits"] | null;
            name: string;
            /** Format: int32 */
            priority: number;
            stackable: boolean;
            stackMode: string;
            /** @enum {string} */
            status: "active" | "disabled" | "deleted" | "pending_approval";
            /** Format: uuid */
            targetAccountTypeId?: string | null;
            trigger: string;
            /** @enum {string} */
            type: "SpendRule" | "FixedBonusRule" | "RedemptionRule" | "TransferRule" | "ReversalRule" | "ManualAdjustmentRule" | "StampRule" | "ExpiryRule";
            /** Format: date-time */
            updatedAt: string;
            /** Format: int32 */
            version: number;
        };
        RuleSettings: {
            /** Format: int32 */
            expiryOverrideDays?: number | null;
            /** Format: int32 */
            holdDays?: number | null;
            notifyOnAward: boolean;
            /** @enum {string} */
            posting: "Immediate" | "Delayed";
            reversible: boolean;
            /** @enum {string|null} */
            rounding?: "down" | "nearest" | "up" | null;
            testMode: boolean;
        };
        RulesMetadataResponse: {
            events: components["schemas"]["EventMetadataResponse"][];
            ruleTypes: components["schemas"]["RuleTypeMetadataResponse"][];
        };
        RuleTypeMetadataResponse: {
            /** @enum {string} */
            category: "Earn" | "Burn" | "Reverse" | "Adjust";
            note: string;
            requiredKinds: string[];
            ruleType: string;
            validTargetAccountKinds: string[];
        };
        SentMessageResponse: {
            /** Format: int32 */
            attempts: number;
            /** Format: date-time */
            createdAt: string;
            dedupKey?: string | null;
            /** Format: uuid */
            eventId: string;
            eventType: string;
            /** Format: date-time */
            publishedAt?: string | null;
            reason?: string | null;
            /** @enum {string} */
            status: "pending" | "published" | "failed";
        };
        SetActiveRequest: {
            isActive: boolean;
        };
        SetCardBucketStatusRequest: {
            /** @enum {string} */
            status: "active" | "disabled";
        };
        SetRuleStatusRequest: {
            /** @enum {string} */
            status: "active" | "disabled";
        };
        SetStreakCampaignStatusRequest: {
            /** @enum {string} */
            status: "active" | "disabled";
        };
        StreakAggregate: {
            /** @enum {string} */
            metric: "sum" | "count";
            /** Format: decimal */
            threshold: string;
        };
        StreakAppliedResponse: {
            /** Format: date-time */
            appliedAt: string;
            /** Format: uuid */
            campaignId: string;
            campaignName?: string | null;
        };
        StreakCampaignResponse: {
            /** Format: date-time */
            activeFrom?: string | null;
            /** Format: date-time */
            activeTo?: string | null;
            conditions?: components["schemas"]["ConditionClause"][] | null;
            config: components["schemas"]["StreakConfig"];
            /** Format: date-time */
            createdAt: string;
            /** Format: uuid */
            id: string;
            name: string;
            /** @enum {string} */
            status: "active" | "disabled" | "deleted";
            /** Format: uuid */
            targetAccountTypeId: string;
            trigger: string;
            /** Format: date-time */
            updatedAt: string;
        };
        StreakCompletionResponse: {
            /** Format: uuid */
            campaignId: string;
            campaignName?: string | null;
            /** Format: date */
            completedPeriod: string;
            /** Format: int32 */
            completionNo: number;
            /** Format: date-time */
            createdAt: string;
            /** Format: int32 */
            periods: number;
            rewardKind: string;
            rewardRef?: string | null;
        };
        StreakConfig: {
            aggregate: components["schemas"]["StreakAggregate"];
            /** @enum {string} */
            on_complete: "restart" | "stop";
            /** @enum {string} */
            period: "day" | "week" | "month";
            reward: components["schemas"]["StreakReward"];
            /** Format: int32 */
            target_periods: number;
            timezone: string;
            /** @enum {string} */
            week_start: "monday" | "sunday";
        };
        StreakReward: {
            /** Format: decimal */
            amount?: string | null;
            /** @enum {string} */
            kind: "fixed_bonus" | "reward_definition";
            /** Format: uuid */
            reward_definition_id?: string | null;
        };
        StreakSummaryResponse: {
            /** Format: uuid */
            campaignId: string;
            campaignName: string;
            /** Format: int32 */
            completions: number;
            status: string;
            /** Format: int32 */
            streakCount: number;
            /** Format: int32 */
            targetPeriods: number;
        };
        TenantResponse: {
            /** Format: date-time */
            createdAt: string;
            id: string;
            name: string;
            /** @enum {string} */
            status: "active" | "suspended";
        };
        TierChangeResponse: {
            /** Format: date-time */
            createdAt: string;
            fromTierName?: string | null;
            /** Format: uuid */
            id: string;
            /** Format: decimal */
            qualifyingPoints: string;
            toTierName: string;
        };
        TierHistoryEntryResponse: {
            /** @enum {string|null} */
            cause?: "points" | "reward" | "downgrade" | null;
            /** Format: date-time */
            createdAt: string;
            fromTierName?: string | null;
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            programId?: string | null;
            programName?: string | null;
            /** Format: decimal */
            qualifyingPoints: string;
            sourceEventId?: string | null;
            toTierName: string;
        };
        TierProgressResponse: {
            currentTierDisplayName?: string | null;
            currentTierName?: string | null;
            /** Format: date */
            expiresAt?: string | null;
            nextTierDisplayName?: string | null;
            /** Format: decimal */
            nextTierMinPoints?: string | null;
            nextTierName?: string | null;
            /** Format: date */
            periodStart?: string | null;
            /** Format: decimal */
            qualifyingPoints: string;
        };
        TierResponse: {
            /** Format: date-time */
            createdAt: string;
            displayName: string;
            /** Format: int32 */
            graceDays: number;
            hasAssignedAccounts: boolean;
            /** Format: uuid */
            id: string;
            /** Format: decimal */
            minPoints: string;
            name: string;
            /** @enum {string} */
            qualifyingModel: "lifetime" | "periodic";
            /** Format: int32 */
            qualifyingPeriodDays?: number | null;
            /** Format: int32 */
            sortOrder: number;
        };
        UpdateAccountTypeRequest: {
            config?: unknown;
            isTierQualifying?: boolean | null;
            name?: string | null;
        };
        UpdateCardBucketRequest: {
            /** Format: date-time */
            activeFrom?: string | null;
            /** Format: date-time */
            activeTo?: string | null;
            additionalConditions?: components["schemas"]["ConditionLeaf"][] | null;
            /** Format: decimal */
            amountMax?: string | null;
            /** Format: decimal */
            amountMin?: string | null;
            countries?: string[] | null;
            /** @enum {string|null} */
            countryMode?: "in" | "not_in" | null;
            daysOfWeek?: string[] | null;
            /** Format: int32 */
            hourFrom?: number | null;
            /** Format: int32 */
            hourTo?: number | null;
            mccCodes?: string[] | null;
            name?: string | null;
            /** Format: decimal */
            perCustomerPerDay?: string | null;
            /** Format: decimal */
            perCustomerTotal?: string | null;
            /** Format: int32 */
            priority?: number | null;
            requireCaptured?: boolean | null;
            /** Format: decimal */
            rewardAmount?: string | null;
            /** Format: uuid */
            targetAccountTypeId?: string | null;
        };
        UpdateProgramRequest: {
            /** @enum {string|null} */
            defaultRounding?: "down" | "nearest" | "up" | null;
            description?: string | null;
            name?: string | null;
            /** Format: uuid */
            qualifyingAccountTypeId?: string | null;
            slug?: string | null;
            /** @enum {string|null} */
            status?: "active" | "inactive" | null;
            /** Format: int32 */
            warningDays?: number | null;
        };
        UpdateRewardRequest: {
            displayName?: string | null;
            name?: string | null;
            /** Format: uuid */
            pointsAccountTypeId?: string | null;
            /** Format: decimal */
            pointsPrice?: string | null;
            typeConfig?: unknown;
        };
        UpdateRuleRequest: {
            /** Format: date-time */
            activeFrom?: string | null;
            /** Format: date-time */
            activeTo?: string | null;
            calculation?: components["schemas"]["RuleCalculation"] | null;
            conditions?: components["schemas"]["ConditionTree"] | null;
            configuration?: components["schemas"]["RuleSettings"] | null;
            exclusivityGroup?: string | null;
            limits?: components["schemas"]["RuleLimits"] | null;
            name?: string | null;
            /** Format: int32 */
            priority?: number | null;
            stackable?: boolean | null;
            stackMode?: string | null;
            /** Format: uuid */
            targetAccountTypeId?: string | null;
            trigger?: string | null;
        };
        UpdateStreakCampaignRequest: {
            /** Format: date-time */
            activeFrom?: string | null;
            /** Format: date-time */
            activeTo?: string | null;
            conditions?: components["schemas"]["ConditionClause"][] | null;
            config?: components["schemas"]["StreakConfig"] | null;
            name?: string | null;
            /** Format: uuid */
            targetAccountTypeId?: string | null;
            trigger?: string | null;
        };
        UpdateTenantRequest: {
            name?: string | null;
            /** @enum {string|null} */
            status?: "active" | "suspended" | null;
        };
        UpdateTierRequest: {
            displayName?: string | null;
            /** Format: int32 */
            graceDays?: number | null;
            /** Format: decimal */
            minPoints?: string | null;
            name?: string | null;
            /** @enum {string|null} */
            qualifyingModel?: "lifetime" | "periodic" | null;
            /** Format: int32 */
            qualifyingPeriodDays?: number | null;
        };
        WalletOverviewResponse: {
            /** Format: uuid */
            accountTypeId: string;
            /** Format: decimal */
            balance: string;
            currency?: string | null;
            /** Format: date */
            expiresOn?: string | null;
            /** Format: decimal */
            expiringAmount?: string | null;
            name: string;
            /** Format: decimal */
            pendingAmount: string;
            type: string;
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    auth_post_auth_login: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["LoginRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["LoginResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tenants_get_platform_tenants: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
                search?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_TenantResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tenants_post_platform_tenants: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateTenantRequest"];
            };
        };
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TenantResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tenants_get_platform_tenants_tenantId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TenantResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tenants_patch_platform_tenants_tenantId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UpdateTenantRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TenantResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tenants_get_platform_tenants_tenantId_admin_users: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
            };
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_AdminUserResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tenants_post_platform_tenants_tenantId_admin_users: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateAdminUserRequest"];
            };
        };
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdminUserResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tenants_get_platform_tenants_tenantId_api_keys: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
            };
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_ApiKeyResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tenants_post_platform_tenants_tenantId_api_keys: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CreateApiKeyResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tenants_delete_platform_tenants_tenantId_api_keys_keyId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                keyId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    configVersions_get_tenants_tenantId_config_versions: {
        parameters: {
            query?: {
                entityId?: string;
                entityType?: string;
                page?: number;
                pageSize?: number;
            };
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_ConfigVersionSummary"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    configVersions_get_tenants_tenantId_config_versions_versionId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                tenantId: string;
                versionId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ConfigVersionDetail"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
                search?: string;
            };
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_CustomerSummaryResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CustomerProfileResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_cap_usage: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RuleCapUsageResponse"][];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_card_buckets: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CustomerCardBucketResponse"][];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_events: {
        parameters: {
            query?: {
                cursor?: string;
                eventType?: string;
                from?: string;
                limit?: number;
                status?: string;
                to?: string;
            };
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CursorPage_CustomerEventResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_events_eventId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                contactKey: string;
                eventId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CustomerEventDetailResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_ledger: {
        parameters: {
            query?: {
                accountTypeId?: string;
                cursor?: string;
                eventId?: string;
                from?: string;
                limit?: number;
                programId?: string;
                reasonGroup?: string;
                ruleId?: string;
                to?: string;
            };
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CursorPage_LedgerEntryResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_messages: {
        parameters: {
            query?: {
                cursor?: string;
                eventType?: string;
                from?: string;
                limit?: number;
                status?: string;
                to?: string;
            };
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CursorPage_SentMessageResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_rewards: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CustomerRewardResponse"][];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_rule_fires: {
        parameters: {
            query?: {
                cursor?: string;
                from?: string;
                limit?: number;
                ruleId?: string;
                to?: string;
            };
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CursorPage_CustomerRuleFireResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_streaks: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CustomerStreakResponse"][];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    customers_get_tenants_tenantId_customers_contactKey_tier_history: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                contactKey: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TierHistoryEntryResponse"][];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    dashboard_get_tenants_tenantId_dashboard_summary: {
        parameters: {
            query?: {
                fromDate?: string;
                programId?: string;
                toDate?: string;
            };
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DashboardSummaryResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    events_get_tenants_tenantId_events_eventId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                eventId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["EventStatusResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    events_post_tenants_tenantId_events_simulate: {
        parameters: {
            query?: never;
            header?: {
                "Idempotency-Key"?: string;
            };
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["GenericEventRequest"];
            };
        };
        responses: {
            /** @description OK */
            202: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["EventAcceptedResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    events_get_tenants_tenantId_events_types: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["EventTypesResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    programs_get_tenants_tenantId_programs: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
            };
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_ProgramResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    programs_post_tenants_tenantId_programs: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateProgramRequest"];
            };
        };
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProgramResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    programs_get_tenants_tenantId_programs_programId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProgramResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    programs_delete_tenants_tenantId_programs_programId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    programs_patch_tenants_tenantId_programs_programId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UpdateProgramRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProgramResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    accountTypes_get_tenants_tenantId_programs_programId_account_types: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
            };
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_AccountTypeResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    accountTypes_post_tenants_tenantId_programs_programId_account_types: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateAccountTypeRequest"];
            };
        };
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AccountTypeResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    accountTypes_get_tenants_tenantId_programs_programId_account_types_accountTypeId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                accountTypeId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AccountTypeResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    accountTypes_patch_tenants_tenantId_programs_programId_account_types_accountTypeId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                accountTypeId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UpdateAccountTypeRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AccountTypeResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    cardBuckets_get_tenants_tenantId_programs_programId_card_buckets: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
                status?: string;
            };
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_CardBucketResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    cardBuckets_post_tenants_tenantId_programs_programId_card_buckets: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateCardBucketRequest"];
            };
        };
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CardBucketResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    cardBuckets_get_tenants_tenantId_programs_programId_card_buckets_bucketId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                bucketId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CardBucketResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    cardBuckets_delete_tenants_tenantId_programs_programId_card_buckets_bucketId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                bucketId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    cardBuckets_patch_tenants_tenantId_programs_programId_card_buckets_bucketId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                bucketId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UpdateCardBucketRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CardBucketResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    cardBuckets_patch_tenants_tenantId_programs_programId_card_buckets_bucketId_status: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                bucketId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SetCardBucketStatusRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CardBucketResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    programs_post_tenants_tenantId_programs_programId_publish: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProgramResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rewards_get_tenants_tenantId_programs_programId_rewards: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
            };
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_RewardResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rewards_post_tenants_tenantId_programs_programId_rewards: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateRewardRequest"];
            };
        };
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RewardResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rewards_delete_tenants_tenantId_programs_programId_rewards_rewardId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                rewardId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rewards_patch_tenants_tenantId_programs_programId_rewards_rewardId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                rewardId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UpdateRewardRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RewardResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rewards_patch_tenants_tenantId_programs_programId_rewards_rewardId_active: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                rewardId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SetActiveRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RewardResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rewards_patch_tenants_tenantId_programs_programId_rewards_rewardId_approve: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                rewardId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RewardResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rules_get_tenants_tenantId_programs_programId_rules: {
        parameters: {
            query?: {
                event?: string;
                page?: number;
                pageSize?: number;
                stackable?: boolean;
                status?: string;
                targetAccountTypeId?: string;
                type?: string;
            };
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_RuleResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rules_post_tenants_tenantId_programs_programId_rules: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateRuleRequest"];
            };
        };
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RuleResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rules_get_tenants_tenantId_programs_programId_rules_ruleId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                ruleId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RuleResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rules_delete_tenants_tenantId_programs_programId_rules_ruleId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                ruleId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rules_patch_tenants_tenantId_programs_programId_rules_ruleId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                ruleId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UpdateRuleRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RuleResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rules_patch_tenants_tenantId_programs_programId_rules_ruleId_approve: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                ruleId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RuleResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rules_patch_tenants_tenantId_programs_programId_rules_ruleId_status: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                ruleId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SetRuleStatusRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RuleResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    rules_get_tenants_tenantId_programs_programId_rules_metadata: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RulesMetadataResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    streakCampaigns_get_tenants_tenantId_programs_programId_streak_campaigns: {
        parameters: {
            query?: {
                event?: string;
                page?: number;
                pageSize?: number;
                status?: string;
            };
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_StreakCampaignResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    streakCampaigns_post_tenants_tenantId_programs_programId_streak_campaigns: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateStreakCampaignRequest"];
            };
        };
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["StreakCampaignResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    streakCampaigns_get_tenants_tenantId_programs_programId_streak_campaigns_campaignId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaignId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["StreakCampaignResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    streakCampaigns_delete_tenants_tenantId_programs_programId_streak_campaigns_campaignId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaignId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    streakCampaigns_patch_tenants_tenantId_programs_programId_streak_campaigns_campaignId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaignId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UpdateStreakCampaignRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["StreakCampaignResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    streakCampaigns_patch_tenants_tenantId_programs_programId_streak_campaigns_campaignId_status: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaignId: string;
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SetStreakCampaignStatusRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["StreakCampaignResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tiers_get_tenants_tenantId_programs_programId_tiers: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
            };
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResult_TierResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tiers_post_tenants_tenantId_programs_programId_tiers: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateTierRequest"];
            };
        };
        responses: {
            /** @description OK */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TierResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tiers_delete_tenants_tenantId_programs_programId_tiers_tierId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
                tierId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tiers_patch_tenants_tenantId_programs_programId_tiers_tierId: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
                tierId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UpdateTierRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TierResponse"];
                };
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
    tiers_post_tenants_tenantId_programs_programId_tiers_reorder: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                programId: string;
                tenantId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ReorderTiersRequest"];
            };
        };
        responses: {
            /** @description No content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Error (RFC 7807 problem details) */
            default: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["ProblemDetailsBody"];
                };
            };
        };
    };
}
