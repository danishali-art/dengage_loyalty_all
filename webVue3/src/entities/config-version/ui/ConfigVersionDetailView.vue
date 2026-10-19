<script setup lang="ts">
import { computed } from 'vue';
import { formatDateTime } from '@/shared/lib/date';
import { prettySnapshot, snapshotSections, type ConfigVersionDetail } from '../model/types';

/**
 * One version's snapshot (port of view-version.dialog.ts): a publish snapshot shows one
 * collapsible section per part of the program (1.3.CL item 9); a per-edit row shows the raw JSON.
 */
const props = defineProps<{ version: ConfigVersionDetail }>();
const sections = computed(() => snapshotSections(props.version));
const pretty = computed(() => prettySnapshot(props.version.snapshot));
</script>

<template>
  <div class="version-detail">
    <p class="version-detail__meta">
      {{
        $t('history.changedBy', {
          change: $t(`history.change.${props.version.changeType}`),
          by: props.version.changedBy,
          at: formatDateTime(props.version.changedAt),
        })
      }}
    </p>
    <template v-if="sections">
      <p v-if="props.version.changeSummary" class="version-detail__summary">
        {{ props.version.changeSummary }}
      </p>
      <div class="version-detail__sections">
        <details v-for="section in sections" :key="section.key" class="version-detail__section">
          <summary>
            {{ $t(section.labelKey) }}
            <span v-if="section.count !== null" class="version-detail__count">({{ section.count }})</span>
          </summary>
          <pre>{{ section.json }}</pre>
        </details>
      </div>
    </template>
    <pre v-else class="version-detail__raw">{{ pretty }}</pre>
  </div>
</template>

<style scoped>
.version-detail__meta {
  margin: 0 0 0.75rem;
  font-size: 0.75rem;
  color: var(--app-muted);
}
.version-detail__summary {
  margin: 0 0 0.75rem;
}
.version-detail__sections {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  max-height: 24rem;
  overflow: auto;
}
.version-detail__section {
  border: 1px solid var(--app-border);
  border-radius: 0.5rem;
}
.version-detail__section summary {
  padding: 0.5rem 0.75rem;
  font-weight: 500;
  cursor: pointer;
}
.version-detail__count {
  color: var(--app-muted);
}
pre {
  margin: 0;
  padding: 0.75rem;
  overflow: auto;
  background: #f9fafb;
  font-size: 0.75rem;
}
.version-detail__raw {
  max-height: 24rem;
  border-radius: 0.5rem;
}
</style>
