import { computed, ref, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { useProgramCrumbs } from '@/entities/program';
import {
  PROGRAM_ENTITY_TYPE,
  PUBLICATION_ENTITY_TYPE,
  useConfigVersionQuery,
  useConfigVersionsQuery,
  type ConfigVersionSummary,
} from '@/entities/config-version';

/**
 * Program history page (port of program-history.page.ts): the publish snapshots (1.3.CL item 9)
 * and the per-edit rows kept alongside them; opening one loads its snapshot.
 */
export function useProgramHistoryPage(props: { programId: string }) {
  const { t } = useI18n();
  const programId = toRef(props, 'programId');
  const { crumbs } = useProgramCrumbs(programId, 'programs.overview.cards.history.label');
  const edits = useConfigVersionsQuery(PROGRAM_ENTITY_TYPE, programId);
  const publications = useConfigVersionsQuery(PUBLICATION_ENTITY_TYPE, programId);

  const openId = ref<string | null>(null);
  const detail = useConfigVersionQuery(openId);
  const viewerOpen = computed({
    get: () => openId.value !== null,
    set: (open) => {
      if (!open) openId.value = null;
    },
  });

  return {
    crumbs,
    edits: computed(() => edits.data.value ?? []),
    publications: computed(() => publications.data.value ?? []),
    loading: computed(() => edits.isLoading.value || publications.isLoading.value),
    describePublication: (v: ConfigVersionSummary) => t('history.published.by', { by: v.changedBy }),
    describeEdit: (v: ConfigVersionSummary) =>
      t('history.changedByShort', { change: t(`history.change.${v.changeType}`), by: v.changedBy }),
    view: (v: ConfigVersionSummary) => (openId.value = v.id),
    viewerOpen,
    viewed: computed(() => detail.data.value),
    viewedLoading: detail.isLoading,
  };
}
