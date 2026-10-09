import { EventMetadata, RuleType } from './rule.model';

/**
 * CR 2026-10-06 Phase 2: which Configuration and Limits fields the rule form shows. The list
 * comes from the API (`GET rules/metadata` → `applicableFields`, built from RuleFieldCatalog.cs),
 * so it is never duplicated here.
 *
 * `null` means "show every field": an existing rule (D5 — saved values stay visible and editable),
 * or a tenant-defined trigger the catalog doesn't know (D7).
 */
export interface FieldVisibility {
  configuration: ReadonlySet<string>;
  limits: ReadonlySet<string>;
}

export function fieldVisibility(
  event: EventMetadata | null,
  type: RuleType,
  isEdit: boolean,
): FieldVisibility | null {
  if (isEdit || !event) return null;
  const entry = event.applicableFields?.find((f) => f.ruleType === type);
  if (!entry) return null;
  return { configuration: new Set(entry.configuration), limits: new Set(entry.limits) };
}

export function isConfigVisible(visibility: FieldVisibility | null, key: string): boolean {
  return visibility === null || visibility.configuration.has(key);
}

export function isLimitVisible(visibility: FieldVisibility | null, key: string): boolean {
  return visibility === null || visibility.limits.has(key);
}
