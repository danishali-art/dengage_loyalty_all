<script setup lang="ts">
import {
  AppButton,
  AppEmptyState,
  AppInput,
  AppPaginator,
  AppSkeletonList,
  AppStatusPill,
} from '@/shared/ui';
import type { Tenant } from '@/entities/tenant';

/** Presentational tenant picker: search box, list, paginator. */
const query = defineModel<string>('query', { required: true });
const props = defineProps<{
  tenants: readonly Tenant[];
  loading: boolean;
  activeTenantId: string | null;
  page: number;
  pageSize: number;
  total: number;
}>();
const emit = defineEmits<{ select: [tenant: Tenant]; 'update:page': [page: number] }>();
</script>

<template>
  <div class="tenant-picker">
    <AppInput
      v-model="query"
      type="search"
      :placeholder="$t('tenant.picker.searchPlaceholder')"
      :aria-label="$t('tenant.picker.searchPlaceholder')"
    />
    <AppSkeletonList v-if="props.loading && props.tenants.length === 0" :count="3" />
    <AppEmptyState
      v-else-if="props.tenants.length === 0"
      :heading="$t('tenant.picker.empty')"
      :description="$t('tenant.picker.emptyHint')"
      icon="🏢"
    />
    <ul v-else class="tenant-picker__list" :aria-label="$t('tenant.picker.listLabel')">
      <li v-for="tenant in props.tenants" :key="tenant.id" class="tenant-picker__item">
        <div class="tenant-picker__text">
          <span class="tenant-picker__name">{{ tenant.name }}</span>
          <span class="tenant-picker__id">{{ tenant.id }}</span>
        </div>
        <AppStatusPill v-if="tenant.id === props.activeTenantId" tone="info">
          {{ $t('tenant.picker.current') }}
        </AppStatusPill>
        <AppButton v-else size="sm" variant="secondary" @click="emit('select', tenant)">
          {{ $t('tenant.picker.select') }}
        </AppButton>
      </li>
    </ul>
    <AppPaginator
      :page="props.page"
      :page-size="props.pageSize"
      :total="props.total"
      @update:page="emit('update:page', $event)"
    />
  </div>
</template>

<style scoped>
.tenant-picker {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
.tenant-picker__list {
  margin: 0;
  padding: 0;
  list-style: none;
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  max-height: 24rem;
  overflow-y: auto;
}
.tenant-picker__item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--app-border);
  background: white;
}
.tenant-picker__item:last-child {
  border-bottom: none;
}
.tenant-picker__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.tenant-picker__name {
  font-weight: 500;
}
.tenant-picker__id {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.75rem;
  color: var(--app-muted);
}
</style>
