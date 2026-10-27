import type { SelectOption } from '@/shared/ui';
import { DEFAULT_TRIGGER, type EventTypesCatalog } from '@/entities/event-type';
import type { RulesMetadata } from '@/entities/rule';

// Category display order for the grouped trigger dropdown; matches EventCategory's declaration
// order in EventTypes.cs. Event types the metadata catalog doesn't know about (tenant-approved
// generic types — see EventTypesCatalog) have no category and sort last, under "Other".
const CATEGORY_ORDER = ['Earn', 'Burn', 'Reverse', 'Adjust'] as const;
const OTHER_GROUP = 'Other';

export function buildTriggerOptions(
  catalog: EventTypesCatalog | null,
  metadata: RulesMetadata | null,
  keep: string | null,
): (SelectOption<string> & { group: string })[] {
  if (!catalog) return [{ value: DEFAULT_TRIGGER, label: DEFAULT_TRIGGER, group: 'Earn' }];

  const categoryByType = new Map<string, string>(
    metadata?.events.map((e) => [e.eventType, e.category]) ?? [],
  );
  // CR 2026-09-30 item 8: an event no rule type can use (reward.purchase — configured through
  // its reward definition) isn't offered as a trigger. `keep` is an existing rule's own trigger,
  // so a rule saved before that change still opens with its trigger shown.
  const ruleless = new Set(
    metadata?.events.filter((e) => e.compatibleRuleTypes.length === 0).map((e) => e.eventType) ?? [],
  );
  const groupRank = (group: string): number => {
    const i = CATEGORY_ORDER.indexOf(group as (typeof CATEGORY_ORDER)[number]);
    return i === -1 ? CATEGORY_ORDER.length : i;
  };

  // `generic` is a deployment-configured allow-list (RabbitMq:GenericEventTypes) and isn't
  // guaranteed disjoint from the built-in set — de-dupe so a misconfigured overlap (e.g. a
  // built-in accidentally also listed as generic) doesn't show the same trigger twice.
  const seen = new Set<string>();
  return [...catalog.builtIn, ...catalog.generic]
    .filter((type) => (seen.has(type) ? false : (seen.add(type), true)))
    .filter((type) => !ruleless.has(type) || type === keep)
    .map((type) => ({ value: type, label: type, group: categoryByType.get(type) ?? OTHER_GROUP }))
    .sort((a, b) => groupRank(a.group) - groupRank(b.group) || a.label.localeCompare(b.label));
}
