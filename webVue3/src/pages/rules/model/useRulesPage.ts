import { computed, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import type { TableColumn } from '@/shared/ui';
import { useProgramCrumbs } from '@/entities/program';
import { useAccountTypesQuery } from '@/entities/account-type';
import { groupRulesByApplication, rankExclusiveRules, useAllRulesQuery, type Rule } from '@/entities/rule';
import { useRuleActions } from '@/features/rule-actions';

/**
 * Rules grouped the way the engine applies them (port of rules-list.page.ts, 1.3.CL item 6):
 * per trigger event and target account, exclusive rules (highest priority wins) apart from
 * stackable ones (added on top); Redemption / Transfer / Reversal, which skip winner selection,
 * on their own. Loads every rule of the program: a group split across pages would misrepresent
 * what competes.
 */
export function useRulesPage(props: { programId: string }) {
  const { t } = useI18n();
  const router = useRouter();
  const programId = toRef(props, 'programId');
  const { crumbs } = useProgramCrumbs(programId, 'rules.heading');
  const rules = useAllRulesQuery(programId);
  const accountTypes = useAccountTypesQuery(programId);
  const actions = useRuleActions(programId);

  const accountsById = computed(() => new Map((accountTypes.data.value ?? []).map((a) => [a.id, a])));
  // CR 2026-10-05 (D8): the account-type list hides retired STAMP wallets, so a disabled rule
  // that targeted one has no entry here — label it instead of showing a bare id.
  function accountLabel(accountTypeId: string): string {
    const account = accountsById.value.get(accountTypeId);
    return account ? `${account.name} (${account.type})` : t('rules.groups.retiredAccount');
  }

  const grouped = computed(() =>
    groupRulesByApplication(rules.data.value ?? [], (id) => accountsById.value.get(id)?.name ?? id),
  );
  /** Winner / tie / fallback for every exclusive rule, per trigger/account group. */
  const ranks = computed(
    () => new Map(grouped.value.groups.flatMap((g) => [...rankExclusiveRules(g.exclusive)])),
  );

  const columns = computed<TableColumn[]>(() => [
    { key: 'name', header: t('rules.list.col.name') },
    { key: 'type', header: t('rules.list.col.type') },
    { key: 'priority', header: t('rules.list.col.priority'), align: 'right' },
    { key: 'status', header: t('rules.list.col.status') },
    { key: 'actions', header: '', align: 'right' },
  ]);

  return {
    crumbs,
    columns,
    outsideColumns: computed<TableColumn[]>(() => [
      { key: 'trigger', header: t('rules.groups.trigger') },
      ...columns.value,
    ]),
    loading: rules.isFetching,
    firstLoad: computed(() => rules.isLoading.value || accountTypes.isLoading.value),
    empty: computed(() => (rules.data.value ?? []).length === 0),
    grouped,
    ranks,
    accountLabel,
    /** Screen-reader caption for one sub-table, e.g. "order.created → Beans (POINTS): Exclusive". */
    groupCaption: (trigger: string, accountTypeId: string, kindKey: string) =>
      `${trigger} → ${accountLabel(accountTypeId)}: ${t(kindKey)}`,
    create: () => router.push(`/programs/${programId.value}/rules/new`),
    editPath: (rule: Rule) => `/programs/${programId.value}/rules/${rule.id}`,
    ...actions,
  };
}
