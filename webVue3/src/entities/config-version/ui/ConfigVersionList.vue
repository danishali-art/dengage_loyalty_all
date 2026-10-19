<script setup lang="ts">
import { AppButton, AppStatusPill } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import { CHANGE_TONE, type ConfigVersionSummary } from '../model/types';

/**
 * A list of recorded versions (publications or per-edit changes). Presentational: the caller
 * supplies the line under the badge through `describe` and handles `view`.
 */
const props = defineProps<{
  versions: readonly ConfigVersionSummary[];
  describe: (version: ConfigVersionSummary) => string;
  busy?: boolean;
}>();
const emit = defineEmits<{ view: [version: ConfigVersionSummary] }>();
</script>

<template>
  <ul class="version-list" :aria-busy="props.busy || undefined">
    <li v-for="version in props.versions" :key="version.id" class="version-list__item">
      <div class="version-list__main">
        <AppStatusPill :tone="CHANGE_TONE[version.changeType]" dot>{{
          `v${version.versionNumber}`
        }}</AppStatusPill>
        <div>
          <div class="version-list__title">{{ props.describe(version) }}</div>
          <div class="version-list__meta">
            {{ formatDateTime(version.changedAt) }}
            <template v-if="version.changeSummary">· {{ version.changeSummary }}</template>
          </div>
        </div>
      </div>
      <AppButton variant="text" size="sm" @click="emit('view', version)">{{ $t('history.view') }}</AppButton>
    </li>
  </ul>
</template>

<style scoped>
.version-list {
  margin: 0;
  padding: 0;
  list-style: none;
}
.version-list__item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.875rem 1rem;
  border-top: 1px solid var(--app-border);
}
.version-list__item:first-child {
  border-top: none;
}
.version-list__main {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.version-list__meta {
  font-size: 0.75rem;
  color: var(--app-muted);
}
</style>
