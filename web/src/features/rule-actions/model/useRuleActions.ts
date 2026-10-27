import { ref, type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';
import { ApiError } from '@/shared/api';
import { notify, useAppConfirm } from '@/shared/ui';
import {
  useApproveRuleMutation,
  useDeleteRuleMutation,
  useSetRuleStatusMutation,
  type Rule,
} from '@/entities/rule';

/** Activate / disable, approve and delete a rule. */
export function useRuleActions(programId: MaybeRefOrGetter<string>) {
  const { t } = useI18n();
  const confirm = useAppConfirm();
  const setStatus = useSetRuleStatusMutation(programId);
  const approveMutation = useApproveRuleMutation(programId);
  const remove = useDeleteRuleMutation(programId);
  /** The rule an action is running on, for its row's pending state. */
  const busyId = ref<string | null>(null);

  async function run(id: string, action: () => Promise<unknown>): Promise<void> {
    busyId.value = id;
    try {
      await action();
    } finally {
      busyId.value = null;
    }
  }

  /** Status and delete failures are toasted by the API client. */
  function toggleStatus(rule: Rule): Promise<void> {
    const next = rule.status === 'active' ? 'disabled' : 'active';
    return run(rule.id, async () => {
      try {
        await setStatus.mutateAsync({ id: rule.id, status: next });
        notify.success(t(next === 'active' ? 'rules.list.activated' : 'rules.list.disabled'));
      } catch {
        // Already toasted.
      }
    });
  }

  // CR-04: the server enforces creator ≠ approver — a self-approval attempt comes back as an
  // error, toasted here with the server's message (the list doesn't know who created a rule).
  function approve(rule: Rule): Promise<void> {
    return run(rule.id, async () => {
      try {
        await approveMutation.mutateAsync(rule.id);
        notify.success(t('rules.list.approved'));
      } catch (error) {
        notify.error(error instanceof ApiError ? error.message : t('rules.list.approveFailed'));
      }
    });
  }

  async function deleteRule(rule: Rule): Promise<void> {
    const confirmed = await confirm.ask({
      title: t('rules.list.delete.title'),
      message: t('rules.list.delete.message', { name: rule.name }),
      confirmLabel: t('action.delete'),
      destructive: true,
    });
    if (!confirmed) return;
    await run(rule.id, async () => {
      try {
        await remove.mutateAsync(rule.id);
        notify.success(t('rules.list.delete.done'));
      } catch {
        // Already toasted.
      }
    });
  }

  return { busyId, toggleStatus, approve, deleteRule };
}
