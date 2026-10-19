<script setup lang="ts">
import { TenantPickerList, useTenantPicker } from '@/features/tenant-switch';

/** Interstitial for a platform_admin who hasn't picked a tenant to administer yet. */
const { query, page, pageSize, tenants, total, loading, activeTenantId, select } = useTenantPicker();
</script>

<template>
  <main class="select-tenant">
    <h1 class="select-tenant__title">{{ $t('tenant.select.title') }}</h1>
    <p class="select-tenant__body">{{ $t('tenant.select.body') }}</p>
    <TenantPickerList
      v-model:query="query"
      v-model:page="page"
      :tenants="tenants"
      :loading="loading"
      :active-tenant-id="activeTenantId"
      :page-size="pageSize"
      :total="total"
      @select="select"
    />
  </main>
</template>

<style scoped>
.select-tenant {
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 0.75rem;
  max-width: 32rem;
  min-height: 100vh;
  margin: 0 auto;
  padding: 3rem 1.5rem;
}
.select-tenant__title {
  margin: 0;
  font-size: 1.125rem;
  font-weight: 700;
}
.select-tenant__body {
  margin: 0;
  color: var(--app-muted);
}
</style>
