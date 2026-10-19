<script setup lang="ts">
import { AppButton, AppEmptyState, AppPageHeader, AppSkeletonList } from '@/shared/ui';
import { ProgramPublicationPills } from '@/entities/program';
import { ProgramInfoForm } from '@/features/program-edit-info';
import { useProgramOverviewPage } from '../model/useProgramOverviewPage';
import ProgramSummaryCards from './ProgramSummaryCards.vue';

const props = defineProps<{ programId: string }>();
const {
  program,
  loading,
  notFound,
  crumbs,
  qualifyingAccountName,
  accountTypesLink,
  cards,
  info,
  lifecycle,
} = useProgramOverviewPage(props);
const { name, slug, description, defaultRounding, errors, saving, save } = info;
const { canPublish, running, publishing, togglingStatus, deleting, publish, toggleActive, remove } =
  lifecycle;
</script>

<template>
  <AppPageHeader :heading="program?.name ?? $t('programs.overview.fallbackHeading')" :crumbs="crumbs">
    <template v-if="program" #actions>
      <!-- 1.3.CL item 8: Draft/Published lifecycle and the Publish action. -->
      <ProgramPublicationPills :program="program" />
      <AppButton
        :disabled="!canPublish"
        :pending="publishing"
        :title="canPublish ? undefined : $t('programs.publication.upToDate')"
        @click="publish"
      >
        {{
          program.publicationStatus === 'draft'
            ? $t('programs.publication.publish')
            : $t('programs.publication.publishChanges')
        }}
      </AppButton>
      <AppButton
        variant="danger"
        :disabled="running"
        :pending="deleting"
        :title="running ? $t('programs.delete.mustBeInactive') : undefined"
        @click="remove"
      >
        {{ $t('action.delete') }}
      </AppButton>
      <AppButton variant="secondary" :pending="saving" @click="save">{{ $t('action.save') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="overview">
    <AppSkeletonList v-if="loading" :count="4" />
    <AppEmptyState
      v-else-if="notFound || !program"
      :heading="$t('programs.overview.notFound')"
      :description="$t('programs.overview.notFoundBody')"
      icon="▦"
    />
    <div v-else class="overview__grid">
      <ProgramInfoForm
        v-model:name="name"
        v-model:slug="slug"
        v-model:description="description"
        v-model:default-rounding="defaultRounding"
        :errors="errors"
        :is-draft="program.publicationStatus === 'draft'"
        :running="running"
        :toggling-status="togglingStatus"
        :qualifying-account-name="qualifyingAccountName"
        :account-types-link="accountTypesLink"
        @toggle-status="toggleActive"
      />
      <ProgramSummaryCards :cards="cards" />
    </div>
  </div>
</template>

<style scoped>
.overview {
  max-width: 64rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
.overview__grid {
  display: grid;
  grid-template-columns: 2fr 3fr;
  gap: 1.5rem;
  align-items: start;
}
</style>
