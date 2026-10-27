import { computed, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import { notify } from '@/shared/ui';
import { useProgramCrumbs } from '@/entities/program';
import { useCardBucketForm } from '@/features/card-bucket-edit';

/** New / edit card bucket page. */
export function useCardBucketEditPage(props: { programId: string; bucketId?: string }) {
  const { t } = useI18n();
  const router = useRouter();
  const programId = toRef(props, 'programId');
  const bucketId = computed(() => props.bucketId ?? null);
  const listPath = computed(() => `/programs/${programId.value}/card-buckets`);

  const form = useCardBucketForm(programId, bucketId, (_saved, created) => {
    notify.success(t(created ? 'cardBuckets.created' : 'cardBuckets.saved'));
    void router.push(listPath.value);
  });
  const { crumbs } = useProgramCrumbs(programId, 'cardBuckets.heading', () => ({
    section: 'card-buckets',
    label: t(form.isEdit.value ? 'cardBuckets.form.editCrumb' : 'cardBuckets.form.newCrumb'),
  }));

  return {
    crumbs,
    heading: computed(() =>
      t(form.isEdit.value ? 'cardBuckets.form.editHeading' : 'cardBuckets.form.newHeading'),
    ),
    cancel: () => router.push(listPath.value),
    form,
  };
}
