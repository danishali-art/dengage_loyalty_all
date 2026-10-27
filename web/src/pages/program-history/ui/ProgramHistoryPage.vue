<script setup lang="ts">
import { AppButton, AppCard, AppDialog, AppEmptyState, AppPageHeader, AppSkeletonList } from '@/shared/ui';
import { ConfigVersionDetailView, ConfigVersionList } from '@/entities/config-version';
import { useProgramHistoryPage } from '../model/useProgramHistoryPage';

const props = defineProps<{ programId: string }>();
const {
  crumbs,
  edits,
  publications,
  loading,
  describePublication,
  describeEdit,
  view,
  viewerOpen,
  viewed,
  viewedLoading,
} = useProgramHistoryPage(props);
</script>

<template>
  <AppPageHeader :heading="$t('history.heading')" :crumbs="crumbs" />

  <div class="history-page">
    <p class="history-page__intro">{{ $t('history.intro') }}</p>

    <!-- 1.3.CL item 9: one entry per Publish, each a snapshot of the whole program. -->
    <AppCard :heading="$t('history.published.heading')" flush>
      <AppSkeletonList v-if="loading" class="history-page__pad" :count="2" />
      <p v-else-if="publications.length === 0" class="history-page__pad history-page__muted">
        {{ $t('history.published.empty') }}
      </p>
      <ConfigVersionList v-else :versions="publications" :describe="describePublication" @view="view" />
    </AppCard>

    <AppCard :heading="$t('history.changes.heading')" flush>
      <AppSkeletonList v-if="loading" class="history-page__pad" :count="4" />
      <div v-else-if="edits.length === 0" class="history-page__pad">
        <AppEmptyState :heading="$t('history.empty')" :description="$t('history.emptyHint')" icon="🕓" />
      </div>
      <ConfigVersionList v-else :versions="edits" :describe="describeEdit" @view="view" />
    </AppCard>
  </div>

  <AppDialog
    v-model:visible="viewerOpen"
    :header="viewed ? $t('history.versionHeading', { version: viewed.versionNumber }) : $t('common.loading')"
    width="44rem"
  >
    <AppSkeletonList v-if="viewedLoading" :count="3" />
    <ConfigVersionDetailView v-else-if="viewed" :version="viewed" />
    <template #footer>
      <AppButton variant="secondary" @click="viewerOpen = false">{{ $t('action.close') }}</AppButton>
    </template>
  </AppDialog>
</template>

<style scoped>
.history-page {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  max-width: 48rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
.history-page__intro {
  margin: 0;
  color: var(--app-muted);
}
.history-page__pad {
  padding: 1rem;
}
.history-page__muted {
  margin: 0;
  color: var(--app-muted);
}
</style>
