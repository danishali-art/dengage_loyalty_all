import { computed, ref, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { notify } from '@/shared/ui';
import { useProgramCrumbs } from '@/entities/program';
import { useAccountTypesPageQuery, useAccountTypesQuery, type AccountType } from '@/entities/account-type';
import { useAccountTypeForm } from '@/features/account-type-edit';

const PAGE_SIZE = 12;

/** Account types page. */
export function useAccountTypesPage(props: { programId: string }) {
  const { t } = useI18n();
  const programId = toRef(props, 'programId');
  const { crumbs } = useProgramCrumbs(programId, 'programs.overview.cards.accountTypes.label');
  const page = ref(1);
  const list = useAccountTypesPageQuery(programId, page, PAGE_SIZE);
  // The form's "redeem into" choice needs every wallet, not just this page's.
  const all = useAccountTypesQuery(programId);

  const form = useAccountTypeForm(programId, (saved, created) => {
    notify.success(created ? t('accountTypes.created', { name: saved.name }) : t('accountTypes.saved'));
    if (created) page.value = 1;
  });

  return {
    crumbs,
    page,
    pageSize: PAGE_SIZE,
    accountTypes: computed(() => list.data.value?.data ?? []),
    total: computed(() => list.data.value?.total ?? 0),
    loading: list.isFetching,
    firstLoad: computed(() => list.isLoading.value),
    create: () => form.open(null, all.data.value ?? []),
    edit: (accountType: AccountType) => form.open(accountType, all.data.value ?? []),
    form,
  };
}
