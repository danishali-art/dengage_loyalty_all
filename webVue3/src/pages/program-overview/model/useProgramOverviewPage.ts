import { computed, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import type { Crumb } from '@/shared/ui';
import { useProgramQuery } from '@/entities/program';
import { useAccountTypesQuery } from '@/entities/account-type';
import { useProgramInfoForm } from '@/features/program-edit-info';
import { useProgramLifecycle } from '@/features/program-lifecycle';

export interface SummaryCard {
  /** i18n key segment: programs.overview.cards.<key>.label / .description */
  key: string;
  icon: string;
  segment: string;
}

const CARDS: SummaryCard[] = [
  { key: 'accountTypes', icon: '⭐', segment: 'account-types' },
  { key: 'tiers', icon: '🏅', segment: 'tiers' },
  { key: 'rules', icon: '⚙', segment: 'rules' },
  { key: 'rewards', icon: '🎁', segment: 'rewards' },
  { key: 'streakCampaigns', icon: '🔥', segment: 'streak-campaigns' },
  { key: 'cardBuckets', icon: '💳', segment: 'card-buckets' },
  { key: 'history', icon: '🕓', segment: 'history' },
];

/** Program overview page state (port of program-overview.page.ts). */
export function useProgramOverviewPage(props: { programId: string }) {
  const { t } = useI18n();
  const programId = toRef(props, 'programId');
  const query = useProgramQuery(programId);
  const program = computed(() => query.data.value);
  const accountTypes = useAccountTypesQuery(programId);

  const info = useProgramInfoForm(program);
  const lifecycle = useProgramLifecycle(program);

  const crumbs = computed<Crumb[]>(() => [
    { label: t('nav.programs'), to: '/programs' },
    { label: program.value?.name ?? '' },
  ]);

  return {
    program,
    loading: query.isLoading,
    notFound: computed(() => query.isError.value),
    crumbs,
    qualifyingAccountName: computed(
      () => accountTypes.data.value?.find((a) => a.isTierQualifying)?.name ?? null,
    ),
    accountTypesLink: computed(() => `/programs/${programId.value}/account-types`),
    cards: computed(() =>
      CARDS.map((c) => ({ key: c.key, icon: c.icon, to: `/programs/${programId.value}/${c.segment}` })),
    ),
    info,
    lifecycle,
  };
}
