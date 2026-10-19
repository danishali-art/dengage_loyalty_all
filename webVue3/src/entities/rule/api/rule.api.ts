import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type { CreateRuleRequest, Rule, RulesMetadata, UpdateRuleRequest } from '../model/types';

/** The API's page-size cap (PageRequest clamps to 100). */
const ALL_PAGE_SIZE = 100;

/** `/tenants/{tenantId}/programs/{programId}/rules` (RulesModule.cs). */
export function listRules(
  tenantId: string,
  programId: string,
  page: number,
  pageSize: number,
): Promise<Page<Rule>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/rules', {
      params: { path: { tenantId, programId }, query: { page, pageSize } },
    }),
  );
}

/**
 * Every (non-deleted) rule of a program — the grouped list needs the whole set, since a
 * trigger/account group split across pages would misrepresent which rules compete. Pages through
 * the 100-row cap rather than assuming a program stays under it.
 */
export async function listAllRules(tenantId: string, programId: string): Promise<Rule[]> {
  const all: Rule[] = [];
  for (let page = 1; ; page++) {
    const result = await listRules(tenantId, programId, page, ALL_PAGE_SIZE);
    all.push(...result.data);
    if (result.data.length === 0 || all.length >= result.total) return all;
  }
}

export function getRule(tenantId: string, programId: string, ruleId: string): Promise<Rule> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/rules/{ruleId}', {
      params: { path: { tenantId, programId, ruleId } },
    }),
  );
}

/**
 * CR-03: the one source of truth for trigger / rule-type compatibility, event fields and (CR
 * 2026-10-06 Phase 2) the fields each type may set — the engine's RuleTypeCatalog. The form
 * falls back to "everything allowed" when it fails, so it doesn't toast.
 */
export function getRulesMetadata(tenantId: string, programId: string): Promise<RulesMetadata> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/rules/metadata', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
    }),
  );
}

export function createRule(tenantId: string, programId: string, body: CreateRuleRequest): Promise<Rule> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/programs/{programId}/rules', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
      body,
    }),
  );
}

/** Editing inserts a new rule version (rule versioning contract); postings keep the old one. */
export function updateRule(
  tenantId: string,
  programId: string,
  ruleId: string,
  body: UpdateRuleRequest,
): Promise<Rule> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/rules/{ruleId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId, ruleId } },
      body,
    }),
  );
}

export function setRuleStatus(
  tenantId: string,
  programId: string,
  ruleId: string,
  status: 'active' | 'disabled',
): Promise<Rule> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/rules/{ruleId}/status', {
      params: { path: { tenantId, programId, ruleId } },
      body: { status },
    }),
  );
}

/** CR-04: a CASH rule needs a second admin — the API refuses the creator (self-approval). */
export function approveRule(tenantId: string, programId: string, ruleId: string): Promise<Rule> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/rules/{ruleId}/approve', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId, ruleId } },
    }),
  );
}

/** A soft delete (status `deleted`). */
export function deleteRule(tenantId: string, programId: string, ruleId: string): Promise<void> {
  return unwrap(
    apiClient().DELETE('/api/v1/tenants/{tenantId}/programs/{programId}/rules/{ruleId}', {
      params: { path: { tenantId, programId, ruleId } },
    }),
  );
}
