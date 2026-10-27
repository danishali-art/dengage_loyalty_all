<script setup lang="ts">
import { AppButton } from '@/shared/ui';
import type { CreatedApiKey } from '@/entities/api-key';

/** The new key, shown once. Presentational. */
const props = defineProps<{ apiKey: CreatedApiKey; copied: boolean }>();
const emit = defineEmits<{ copy: [] }>();
</script>

<template>
  <div class="flex flex-col gap-3">
    <p class="m-0 text-sm text-(--app-danger-fg)" role="alert">
      {{ $t('platform.apiKeys.created.warning') }}
    </p>
    <div class="flex items-center gap-2 rounded-lg border bg-gray-50 p-3">
      <code class="flex-1 font-mono text-xs break-all" data-raw-key>{{ props.apiKey.rawKey }}</code>
      <AppButton size="sm" variant="secondary" @click="emit('copy')">
        {{ $t(props.copied ? 'platform.apiKeys.created.copied' : 'platform.apiKeys.created.copy') }}
      </AppButton>
    </div>
    <p class="m-0 text-xs text-muted-foreground">
      {{ $t('platform.apiKeys.created.prefix', { prefix: props.apiKey.prefix }) }}
    </p>
  </div>
</template>
