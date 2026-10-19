<script setup lang="ts">
import { computed } from 'vue';
import {
  AppButton,
  AppCard,
  AppDataTable,
  AppDialog,
  AppPageHeader,
  AppPaginator,
  AppStatusPill,
} from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import type { ApiKey } from '@/entities/api-key';
import { ApiKeyCreated } from '@/features/api-key-manage';
import { useApiKeysPage } from '../model/useApiKeysPage';

const props = defineProps<{ tenantId: string }>();
const { crumbs, page, pageSize, rows, total, loading, columns, manage } = useApiKeysPage(props);
const { created, copied, creating, createKey, dismiss, copy, revokeKey } = manage;

// The dialog's v-model: closing it (Done, Esc, outside click) forgets the raw key.
const createdOpen = computed({
  get: () => created.value !== null,
  set: (open: boolean) => {
    if (!open) dismiss();
  },
});
</script>

<template>
  <AppPageHeader :heading="$t('platform.apiKeys.heading')" :crumbs="crumbs">
    <template #actions>
      <AppButton :pending="creating" @click="createKey">{{ $t('platform.apiKeys.new') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="api-keys-page">
    <AppCard flush>
      <AppDataTable
        :rows="rows"
        :columns="columns"
        row-key="id"
        :caption="$t('platform.apiKeys.caption')"
        :loading="loading"
        :empty-text="$t('platform.apiKeys.empty')"
      >
        <template #cell-prefix="{ row }">
          <code class="font-mono text-xs">
            {{ $t('platform.apiKeys.prefixValue', { prefix: (row as ApiKey).prefix }) }}
          </code>
        </template>
        <template #cell-createdAt="{ row }">{{ formatDateTime((row as ApiKey).createdAt) }}</template>
        <template #cell-lastUsedAt="{ row }">
          {{
            (row as ApiKey).lastUsedAt
              ? formatDateTime((row as ApiKey).lastUsedAt)
              : $t('platform.apiKeys.neverUsed')
          }}
        </template>
        <template #cell-actions="{ row }">
          <AppStatusPill v-if="(row as ApiKey).revokedAt" tone="neutral">
            {{ $t('platform.apiKeys.revoked') }}
          </AppStatusPill>
          <AppButton v-else size="sm" variant="danger" @click="revokeKey(row as ApiKey)">
            {{ $t('platform.apiKeys.revokeKey') }}
          </AppButton>
        </template>
      </AppDataTable>
      <AppPaginator v-model:page="page" :page-size="pageSize" :total="total" />
    </AppCard>
  </div>

  <AppDialog v-model:visible="createdOpen" :header="$t('platform.apiKeys.created.heading')">
    <ApiKeyCreated v-if="created" :api-key="created" :copied="copied" @copy="copy" />
    <template #footer>
      <AppButton @click="createdOpen = false">{{ $t('platform.apiKeys.created.done') }}</AppButton>
    </template>
  </AppDialog>
</template>

<style scoped>
.api-keys-page {
  max-width: 64rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
</style>
