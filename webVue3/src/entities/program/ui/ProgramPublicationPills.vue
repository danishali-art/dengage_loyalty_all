<script setup lang="ts">
import { AppStatusPill } from '@/shared/ui';
import type { Program } from '../model/types';

/** Draft / Published v{n} / Unpublished changes (1.3.CL item 8). Presentational. */
const props = withDefaults(defineProps<{ program: Program; short?: boolean }>(), { short: false });
</script>

<template>
  <span class="program-pills">
    <template v-if="props.short">
      <AppStatusPill v-if="props.program.publicationStatus === 'draft'" tone="warning">
        {{ $t('programs.publication.draftShort') }}
      </AppStatusPill>
      <AppStatusPill v-else-if="props.program.hasUnpublishedChanges" tone="info">
        {{ $t('programs.publication.unpublishedChanges') }}
      </AppStatusPill>
    </template>
    <template v-else>
      <AppStatusPill :tone="props.program.publicationStatus === 'published' ? 'success' : 'warning'">
        {{
          props.program.publicationStatus === 'published'
            ? $t('programs.publication.published', { version: props.program.publishedVersion })
            : $t('programs.publication.draft')
        }}
      </AppStatusPill>
      <AppStatusPill v-if="props.program.hasUnpublishedChanges" tone="info">
        {{ $t('programs.publication.unpublishedChanges') }}
      </AppStatusPill>
    </template>
  </span>
</template>

<style scoped>
.program-pills {
  display: inline-flex;
  gap: 0.375rem;
}
</style>
