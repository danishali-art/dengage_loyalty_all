<script setup lang="ts">
import { AppButton, AppEmptyState, AppPageHeader, AppSkeletonList, AppStatusPill } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import { useTenantOverviewPage } from '../model/useTenantOverviewPage';

const props = defineProps<{ tenantId: string }>();
const { tenant, loading, notFound, crumbs, cards, toggling, toggleStatus } = useTenantOverviewPage(props);
</script>

<template>
  <AppPageHeader :heading="tenant?.name ?? props.tenantId" :crumbs="crumbs">
    <template v-if="tenant" #actions>
      <AppButton
        :variant="tenant.status === 'active' ? 'danger' : 'primary'"
        :pending="toggling"
        @click="toggleStatus"
      >
        {{ $t(tenant.status === 'active' ? 'platform.overview.suspend' : 'platform.overview.reactivate') }}
      </AppButton>
    </template>
  </AppPageHeader>

  <div class="tenant-overview">
    <AppSkeletonList v-if="loading" :count="3" />
    <AppEmptyState
      v-else-if="notFound || !tenant"
      :heading="$t('platform.overview.notFound')"
      :description="$t('platform.overview.notFoundBody')"
      icon="⚙"
    />
    <template v-else>
      <dl class="tenant-overview__strip">
        <div>
          <dt>{{ $t('platform.overview.status') }}</dt>
          <dd>
            <AppStatusPill :tone="tenant.status === 'active' ? 'success' : 'neutral'" dot>
              {{ $t(`platform.status.${tenant.status}`) }}
            </AppStatusPill>
          </dd>
        </div>
        <div>
          <dt>{{ $t('platform.overview.tenantId') }}</dt>
          <dd>
            <code class="font-mono text-xs">{{ tenant.id }}</code>
          </dd>
        </div>
        <div>
          <dt>{{ $t('platform.overview.created') }}</dt>
          <dd>{{ formatDateTime(tenant.createdAt) }}</dd>
        </div>
      </dl>

      <div class="tenant-overview__cards">
        <RouterLink v-for="card in cards" :key="card.key" :to="card.to" class="tenant-card">
          <div class="tenant-card__head">
            <span class="tenant-card__icon" aria-hidden="true">{{ card.icon }}</span>
            <div>
              <div class="tenant-card__label">{{ $t(`platform.overview.cards.${card.key}.label`) }}</div>
              <div class="tenant-card__description">
                {{ $t(`platform.overview.cards.${card.key}.description`) }}
              </div>
            </div>
          </div>
          <div class="tenant-card__manage">{{ $t('platform.overview.manage') }} ›</div>
        </RouterLink>
      </div>
    </template>
  </div>
</template>

<style scoped>
.tenant-overview {
  max-width: 64rem;
  margin: 0 auto;
  padding: 1.5rem 2rem;
}
.tenant-overview__strip {
  display: flex;
  flex-wrap: wrap;
  gap: 2.5rem;
  margin: 0 0 1.5rem;
  padding: 1rem 1.25rem;
  background: white;
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
}
.tenant-overview__strip dt {
  font-size: 0.75rem;
  color: var(--app-muted);
}
.tenant-overview__strip dd {
  margin: 0.25rem 0 0;
}
.tenant-overview__cards {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
}
.tenant-card {
  display: block;
  padding: 1.25rem;
  background: white;
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  box-shadow: var(--app-shadow-card);
  color: var(--app-text);
  text-decoration: none;
  transition: border-color 0.15s;
}
.tenant-card:hover {
  border-color: var(--app-brand);
  text-decoration: none;
}
.tenant-card__head {
  display: flex;
  gap: 0.75rem;
}
.tenant-card__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 2.25rem;
  height: 2.25rem;
  border-radius: 0.5rem;
  background: var(--app-brand-light);
  font-size: 1.125rem;
}
.tenant-card__label {
  font-weight: 500;
}
.tenant-card__description {
  margin-top: 0.125rem;
  font-size: 0.75rem;
  color: var(--app-muted);
}
.tenant-card__manage {
  margin-top: 0.75rem;
  text-align: right;
  font-weight: 500;
  color: var(--app-brand);
}
</style>
