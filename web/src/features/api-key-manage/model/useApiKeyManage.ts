import { ref, type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';
import { notify, useAppConfirm } from '@/shared/ui';
import {
  useCreateApiKeyMutation,
  useRevokeApiKeyMutation,
  type ApiKey,
  type CreatedApiKey,
} from '@/entities/api-key';

/**
 * Create and revoke a tenant's API keys. A new key's raw value is
 * held here only until its dialog is closed — it is never retrievable again. Failures are toasted
 * by the API client.
 */
export function useApiKeyManage(tenantId: MaybeRefOrGetter<string>) {
  const { t } = useI18n();
  const confirm = useAppConfirm();
  const create = useCreateApiKeyMutation(tenantId);
  const revoke = useRevokeApiKeyMutation(tenantId);
  const created = ref<CreatedApiKey | null>(null);
  const copied = ref(false);

  return {
    created,
    copied,
    creating: create.isPending,
    async createKey(): Promise<void> {
      try {
        created.value = await create.mutateAsync();
        copied.value = false;
      } catch {
        // Already toasted.
      }
    },
    /** Closing the dialog forgets the raw key. */
    dismiss(): void {
      created.value = null;
    },
    async copy(): Promise<void> {
      try {
        await navigator.clipboard.writeText(created.value?.rawKey ?? '');
        copied.value = true;
      } catch {
        // The clipboard can be unavailable; the key stays selectable by hand.
      }
    },
    async revokeKey(key: ApiKey): Promise<void> {
      const confirmed = await confirm.ask({
        title: t('platform.apiKeys.revoke.title'),
        message: t('platform.apiKeys.revoke.message', { prefix: key.prefix }),
        confirmLabel: t('platform.apiKeys.revoke.confirm'),
        destructive: true,
      });
      if (!confirmed) return;
      try {
        await revoke.mutateAsync(key.id);
        notify.success(t('platform.apiKeys.revoke.done'));
      } catch {
        // Already toasted.
      }
    },
  };
}
