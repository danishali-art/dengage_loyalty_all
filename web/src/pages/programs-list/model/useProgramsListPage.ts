import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useI18n } from 'vue-i18n';
import { notify, type TableColumn } from '@/shared/ui';
import { useProgramsQuery, type Program } from '@/entities/program';
import { useCreateProgramForm } from '@/features/program-create';

const PAGE_SIZE = 25;

/** Programs list page state: paging, opening, creating. */
export function useProgramsListPage() {
  const { t } = useI18n();
  const router = useRouter();
  const page = ref(1);
  const programs = useProgramsQuery(page, PAGE_SIZE);
  const createOpen = ref(false);

  const create = useCreateProgramForm((program: Program) => {
    createOpen.value = false;
    notify.success(t('programs.create.done', { name: program.name }));
    void router.push(`/programs/${program.id}`);
  });

  function openCreate(): void {
    create.reset();
    createOpen.value = true;
  }

  const columns = computed<TableColumn[]>(() => [
    { key: 'name', header: t('programs.list.col.name') },
    { key: 'status', header: t('programs.list.col.status') },
    { key: 'accountTypeCount', header: t('programs.list.col.accountTypes') },
    { key: 'ruleCount', header: t('programs.list.col.rules') },
  ]);

  return {
    page,
    pageSize: PAGE_SIZE,
    rows: computed(() => programs.data.value?.data ?? []),
    total: computed(() => programs.data.value?.total ?? 0),
    loading: programs.isFetching,
    columns,
    open: (program: Program) => router.push(`/programs/${program.id}`),
    createOpen,
    openCreate,
    create,
  };
}
