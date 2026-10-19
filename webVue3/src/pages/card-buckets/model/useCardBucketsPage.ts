import { computed, ref, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import type { TableColumn } from '@/shared/ui';
import { useProgramCrumbs } from '@/entities/program';
import { useCardBucketsPageQuery, type CardBucket } from '@/entities/card-bucket';
import { useCardBucketActions } from '@/features/card-bucket-actions';

const PAGE_SIZE = 20;

/** Card buckets list (port of card-buckets-list.page.ts). */
export function useCardBucketsPage(props: { programId: string }) {
  const { t } = useI18n();
  const router = useRouter();
  const programId = toRef(props, 'programId');
  const { crumbs } = useProgramCrumbs(programId, 'cardBuckets.heading');
  const page = ref(1);
  const list = useCardBucketsPageQuery(programId, page, PAGE_SIZE);
  const actions = useCardBucketActions(programId);

  const columns = computed<TableColumn[]>(() => [
    { key: 'name', header: t('cardBuckets.col.name') },
    { key: 'mcc', header: t('cardBuckets.col.mcc') },
    { key: 'amount', header: t('cardBuckets.col.amount') },
    { key: 'status', header: t('cardBuckets.col.status') },
    { key: 'actions', header: '', align: 'right' },
  ]);

  return {
    crumbs,
    columns,
    page,
    pageSize: PAGE_SIZE,
    rows: computed(() => list.data.value?.data ?? []),
    total: computed(() => list.data.value?.total ?? 0),
    loading: list.isFetching,
    firstLoad: list.isLoading,
    create: () => router.push(`/programs/${programId.value}/card-buckets/new`),
    editPath: (bucket: CardBucket) => `/programs/${programId.value}/card-buckets/${bucket.id}`,
    /** "min–max", open-ended where one side is blank (the Angular list's format). */
    amountRange: (bucket: CardBucket) =>
      !bucket.amountMin && !bucket.amountMax ? '—' : `${bucket.amountMin ?? '0'}–${bucket.amountMax ?? '∞'}`,
    ...actions,
  };
}
