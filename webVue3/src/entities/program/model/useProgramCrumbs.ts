import { computed, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';
import type { Crumb } from '@/shared/ui';
import { useProgramQuery } from '../api/program.queries';
import type { Program } from './types';

/** A page below a section, e.g. a rule's form under Rules: the section links back to its list. */
export interface ChildCrumb {
  /** The section's path segment, e.g. `rules`. */
  section: string;
  label: string;
}

/**
 * "Programs › {program} › {section}" for a program's sub-pages — or, with `child`,
 * "… › {section} › {child}" — with the program read from the query cache (no context store).
 * Also hands back the program for pages that need more of it.
 */
export function useProgramCrumbs(
  programId: MaybeRefOrGetter<string>,
  sectionKey: string,
  child?: MaybeRefOrGetter<ChildCrumb | undefined>,
): { crumbs: ComputedRef<Crumb[]>; program: ComputedRef<Program | undefined> } {
  const { t } = useI18n();
  const query = useProgramQuery(programId);
  const program = computed(() => query.data.value);
  const crumbs = computed<Crumb[]>(() => {
    const below = toValue(child);
    return [
      { label: t('nav.programs'), to: '/programs' },
      {
        label: program.value?.name ?? t('programs.overview.fallbackHeading'),
        to: `/programs/${toValue(programId)}`,
      },
      below
        ? { label: t(sectionKey), to: `/programs/${toValue(programId)}/${below.section}` }
        : { label: t(sectionKey) },
      ...(below ? [{ label: below.label }] : []),
    ];
  });
  return { crumbs, program };
}
