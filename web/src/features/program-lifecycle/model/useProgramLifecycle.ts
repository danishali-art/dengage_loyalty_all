import { computed, type Ref } from 'vue';
import { useRouter } from 'vue-router';
import { useI18n } from 'vue-i18n';
import { ApiError } from '@/shared/api';
import { notify, useAppConfirm } from '@/shared/ui';
import {
  canPublish,
  isRunning,
  useDeleteProgramMutation,
  usePublishProgramMutation,
  useUpdateProgramMutation,
  type Program,
} from '@/entities/program';

/**
 * Publish, Active/Inactive and Delete for one program (1.3.CL items 7–9). Each asks for
 * confirmation first.
 */
export function useProgramLifecycle(program: Ref<Program | undefined>) {
  const { t } = useI18n();
  const router = useRouter();
  const confirm = useAppConfirm();
  const id = (): string => program.value?.id ?? '';
  const publishMutation = usePublishProgramMutation(id);
  const statusMutation = useUpdateProgramMutation(id);
  const deleteMutation = useDeleteProgramMutation(id);

  function fail(error: unknown): void {
    notify.error(error instanceof ApiError ? error.message : t('common.genericError'));
  }

  async function publish(): Promise<void> {
    const p = program.value;
    if (!p || !canPublish(p) || publishMutation.isPending.value) return;
    const confirmed = await confirm.ask({
      title: t('programs.publication.confirmTitle'),
      message: t('programs.publication.confirmMessage', { name: p.name }),
      confirmLabel: t('programs.publication.publish'),
    });
    if (!confirmed) return;
    try {
      const updated = await publishMutation.mutateAsync();
      notify.success(t('programs.publication.done', { version: updated.publishedVersion }));
    } catch (error) {
      fail(error);
    }
  }

  async function toggleActive(): Promise<void> {
    const p = program.value;
    if (!p || p.publicationStatus === 'draft' || statusMutation.isPending.value) return;
    try {
      const updated = await statusMutation.mutateAsync({ status: isRunning(p) ? 'inactive' : 'active' });
      notify.success(
        t(updated.status === 'active' ? 'programs.status.activated' : 'programs.status.deactivated'),
      );
    } catch (error) {
      fail(error);
    }
  }

  async function remove(): Promise<void> {
    const p = program.value;
    if (!p || isRunning(p) || deleteMutation.isPending.value) return;
    const confirmed = await confirm.ask({
      title: t('programs.delete.confirmTitle'),
      message: t('programs.delete.confirmMessage', { name: p.name }),
      confirmLabel: t('action.delete'),
      destructive: true,
    });
    if (!confirmed) return;
    try {
      await deleteMutation.mutateAsync();
      notify.success(t('programs.delete.done', { name: p.name }));
      await router.push('/programs');
    } catch (error) {
      fail(error);
    }
  }

  return {
    canPublish: computed(() => !!program.value && canPublish(program.value)),
    running: computed(() => !!program.value && isRunning(program.value)),
    publishing: publishMutation.isPending,
    togglingStatus: statusMutation.isPending,
    deleting: deleteMutation.isPending,
    publish,
    toggleActive,
    remove,
  };
}
