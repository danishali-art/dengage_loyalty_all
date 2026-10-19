<script setup lang="ts">
import {
  AppButton,
  AppCard,
  AppDataTable,
  AppDialog,
  AppPageHeader,
  AppPaginator,
  AppStatusPill,
} from '@/shared/ui';
import { ProgramPublicationPills, type Program } from '@/entities/program';
import { CreateProgramForm } from '@/features/program-create';
import { useProgramsListPage } from '../model/useProgramsListPage';

const { page, pageSize, rows, total, loading, columns, open, createOpen, openCreate, create } =
  useProgramsListPage();
const { name, slug, description, errors, formErrors, pending, submit } = create;
</script>

<template>
  <AppPageHeader :heading="$t('programs.list.heading')">
    <template #actions>
      <AppButton @click="openCreate">{{ $t('programs.list.new') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="programs-page">
    <AppCard flush>
      <AppDataTable
        :rows="rows"
        :columns="columns"
        row-key="id"
        :caption="$t('programs.list.caption')"
        :loading="loading"
        :empty-text="$t('programs.list.empty')"
        clickable-rows
        @row-click="open"
      >
        <template #cell-name="{ row }">
          <RouterLink :to="`/programs/${(row as Program).id}`" class="programs-page__name">
            {{ (row as Program).name }}
          </RouterLink>
        </template>
        <template #cell-status="{ row }">
          <span class="programs-page__status">
            <AppStatusPill :tone="(row as Program).status === 'active' ? 'success' : 'neutral'" dot>
              {{ (row as Program).status === 'active' ? $t('common.active') : $t('common.inactive') }}
            </AppStatusPill>
            <!-- 1.3.CL item 8: a draft never runs, whatever its status says. -->
            <ProgramPublicationPills :program="row as Program" short />
          </span>
        </template>
      </AppDataTable>
      <AppPaginator v-model:page="page" :page-size="pageSize" :total="total" />
    </AppCard>
  </div>

  <AppDialog v-model:visible="createOpen" :header="$t('programs.create.heading')">
    <CreateProgramForm
      v-model:name="name"
      v-model:slug="slug"
      v-model:description="description"
      :errors="errors"
      :form-errors="formErrors"
      @submit="submit"
    />
    <template #footer>
      <AppButton variant="secondary" @click="createOpen = false">{{ $t('action.cancel') }}</AppButton>
      <AppButton :pending="pending" @click="submit">{{ $t('programs.create.submit') }}</AppButton>
    </template>
  </AppDialog>
</template>

<style scoped>
.programs-page {
  max-width: 80rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
.programs-page__name {
  font-weight: 500;
  color: var(--app-text);
}
.programs-page__status {
  display: inline-flex;
  gap: 0.375rem;
}
</style>
