<script setup lang="ts">
import { AppCard, AppFormField, AppInput, AppPageHeader, AppSelect } from '@/shared/ui';
import { useDashboardPage } from '../model/useDashboardPage';

const { filter, programOptions, filtered, clear, loading, tiles } = useDashboardPage();
</script>

<template>
  <AppPageHeader :heading="$t('nav.dashboard')" :subtitle="$t('dashboard.subtitle')" />

  <div class="mx-auto max-w-6xl px-8 py-6">
    <AppCard class="mb-6">
      <div class="flex flex-wrap items-end gap-4">
        <div class="w-56">
          <AppFormField :label="$t('dashboard.program')" label-id="filter-program-label">
            <AppSelect
              id="filter-program"
              searchable
              labelled-by="filter-program-label"
              :options="programOptions"
              :model-value="filter.programId ?? ''"
              @update:model-value="filter.programId = $event || null"
            />
          </AppFormField>
        </div>
        <AppFormField :label="$t('dashboard.from')" for="filter-from">
          <AppInput
            id="filter-from"
            type="date"
            :model-value="filter.fromDate ?? ''"
            @update:model-value="filter.fromDate = $event || null"
          />
        </AppFormField>
        <AppFormField :label="$t('dashboard.to')" for="filter-to">
          <AppInput
            id="filter-to"
            type="date"
            :model-value="filter.toDate ?? ''"
            @update:model-value="filter.toDate = $event || null"
          />
        </AppFormField>
        <button
          v-if="filtered"
          type="button"
          class="pb-2 text-xs font-medium text-muted-foreground hover:text-primary"
          @click="clear"
        >
          {{ $t('dashboard.clear') }}
        </button>
      </div>
    </AppCard>

    <div v-if="loading" class="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4" aria-busy="true">
      <AppCard v-for="i in 8" :key="i" aria-hidden="true">
        <div class="h-3 w-20 animate-pulse rounded bg-gray-200" />
        <div class="mt-3 h-7 w-14 animate-pulse rounded bg-gray-200" />
      </AppCard>
    </div>
    <div v-else class="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
      <component
        :is="tile.to ? 'RouterLink' : 'div'"
        v-for="tile in tiles"
        :key="tile.labelKey"
        :to="tile.to"
        class="block rounded-(--app-radius) border bg-white p-5 text-inherit no-underline shadow-(--app-shadow-card) hover:no-underline"
        :class="{ 'transition-colors hover:border-primary': tile.to }"
      >
        <div class="flex items-center gap-2 text-xs font-medium text-muted-foreground">
          <span aria-hidden="true">{{ tile.icon }}</span
          >{{ $t(tile.labelKey) }}
        </div>
        <div class="mt-2 text-2xl font-bold text-(--app-text)">{{ tile.value }}</div>
      </component>
    </div>
  </div>
</template>
