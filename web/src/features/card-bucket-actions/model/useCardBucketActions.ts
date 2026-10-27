import { ref, type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';
import { notify, useAppConfirm } from '@/shared/ui';
import {
  useDeleteCardBucketMutation,
  useSetCardBucketStatusMutation,
  type CardBucket,
} from '@/entities/card-bucket';

/**
 * Activate / disable and delete a card bucket. Failures are
 * toasted by the API client.
 */
export function useCardBucketActions(programId: MaybeRefOrGetter<string>) {
  const { t } = useI18n();
  const confirm = useAppConfirm();
  const setStatus = useSetCardBucketStatusMutation(programId);
  const remove = useDeleteCardBucketMutation(programId);
  /** The bucket an action is running on, for its row's pending state. */
  const busyId = ref<string | null>(null);

  async function run(id: string, action: () => Promise<unknown>): Promise<void> {
    busyId.value = id;
    try {
      await action();
    } catch {
      // Already toasted by the API client.
    } finally {
      busyId.value = null;
    }
  }

  function toggleStatus(bucket: CardBucket): Promise<void> {
    const next = bucket.status === 'active' ? 'disabled' : 'active';
    return run(bucket.id, async () => {
      await setStatus.mutateAsync({ id: bucket.id, status: next });
      notify.success(t(next === 'active' ? 'cardBuckets.activated' : 'cardBuckets.disabled'));
    });
  }

  async function deleteBucket(bucket: CardBucket): Promise<void> {
    const confirmed = await confirm.ask({
      title: t('cardBuckets.delete.title'),
      message: t('cardBuckets.delete.message', { name: bucket.name }),
      confirmLabel: t('action.delete'),
      destructive: true,
    });
    if (!confirmed) return;
    await run(bucket.id, async () => {
      await remove.mutateAsync(bucket.id);
      notify.success(t('cardBuckets.delete.done'));
    });
  }

  return { busyId, toggleStatus, deleteBucket };
}
