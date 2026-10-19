<script setup lang="ts">
import { toRef } from 'vue';
import { AppEmptyState, AppPaginator, AppStatusPill } from '@/shared/ui';
import { formatDate, formatDateTime } from '@/shared/lib/date';
import { CUSTOMER_PAGE_SIZES, CustomerEventButton, type CustomerProfile } from '@/entities/customer';
import { useTiersTab } from '../../model/useCustomerTabs';
import type { OpenEventRequest } from '../../model/useCustomerDetailPage';

/**
 * Tiers (port of customer-tiers.tab.ts, P2): one status tile per program that has tiers, then
 * every tier change with its program and cause (points, a reward, or the nightly downgrade —
 * which has no event of its own).
 */
const props = defineProps<{ contactKey: string; profile: CustomerProfile }>();
const emit = defineEmits<{ openEvent: [request: OpenEventRequest] }>();
const { tiles, loading, error, paged } = useTiersTab(toRef(props, 'contactKey'), toRef(props, 'profile'));
const { page, pageSize, rows, total, changePageSize } = paged;
const ring = (percent: number): string => `conic-gradient(var(--app-brand) ${percent}%, #f3f4f6 0)`;
</script>

<template>
  <div class="flex flex-col gap-6">
    <section class="rounded-(--app-radius) border bg-white p-5 shadow-(--app-shadow-card)">
      <div class="mb-3 flex items-center justify-between gap-3">
        <h2 class="m-0 text-[0.9375rem] font-semibold text-(--app-heading)">
          {{ $t('customers.tiers.statusHeading') }}
        </h2>
        <span v-if="tiles.length" class="text-xs text-muted-foreground">{{
          $t('customers.tiers.summary', { count: tiles.length })
        }}</span>
      </div>
      <p v-if="tiles.length === 0" class="text-sm text-muted-foreground">
        {{ $t('customers.tiers.noneTiered') }}
      </p>
      <div
        v-else
        class="-mx-1 flex snap-x snap-mandatory gap-4 overflow-x-auto px-1 pt-1 pb-3"
        role="list"
        tabindex="0"
        :aria-label="$t('customers.tiers.statusHeading')"
      >
        <article
          v-for="tile in tiles"
          :key="tile.programId"
          role="listitem"
          class="w-[23rem] shrink-0 snap-start rounded-xl border border-t-[3px] border-t-primary bg-white p-4 shadow-sm"
        >
          <header class="flex items-start justify-between gap-2">
            <h3 class="m-0 min-w-0 truncate text-sm font-semibold" :title="tile.programName">
              {{ tile.programName }}
            </h3>
            <AppStatusPill tone="info" dot>{{
              tile.tier.tierDisplayName ?? $t('customers.overview.noTier')
            }}</AppStatusPill>
          </header>
          <div class="mt-4 flex items-center gap-4">
            <div
              class="flex size-16 shrink-0 items-center justify-center rounded-full"
              role="img"
              :aria-label="
                tile.tier.nextTierMinPoints
                  ? $t('customers.tiers.toNext', {
                      percent: tile.percent,
                      tier: tile.tier.nextTierDisplayName,
                    })
                  : $t('customers.tiers.topTierShort')
              "
              :style="{ background: ring(tile.percent) }"
            >
              <span
                class="flex size-12 items-center justify-center rounded-full bg-white text-sm font-semibold"
                >{{ tile.percent }}%</span
              >
            </div>
            <dl class="m-0 flex min-w-0 flex-1 flex-col gap-1.5 text-xs">
              <div class="flex justify-between gap-2">
                <dt class="text-muted-foreground">{{ $t('customers.tiers.points') }}</dt>
                <dd class="m-0 font-medium tabular-nums">
                  {{ tile.tier.qualifyingPoints
                  }}<template v-if="tile.tier.nextTierMinPoints">
                    / {{ tile.tier.nextTierMinPoints }}</template
                  >
                </dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-muted-foreground">{{ $t('customers.tiers.nextTier') }}</dt>
                <dd class="m-0 truncate font-medium">
                  {{ tile.tier.nextTierDisplayName ?? $t('customers.tiers.topTierShort') }}
                </dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-muted-foreground">{{ $t('customers.tiers.periodStart') }}</dt>
                <dd class="m-0">{{ formatDate(tile.tier.periodStart) }}</dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-muted-foreground">{{ $t('customers.tiers.review') }}</dt>
                <dd class="m-0">{{ formatDate(tile.tier.graceEndsAt) }}</dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-muted-foreground">{{ $t('customers.tiers.lockedUntil') }}</dt>
                <dd class="m-0">{{ formatDate(tile.tier.lockedUntil) }}</dd>
              </div>
            </dl>
          </div>
        </article>
      </div>
    </section>

    <section class="overflow-hidden rounded-(--app-radius) border bg-white shadow-(--app-shadow-card)">
      <h2 class="m-0 px-5 pt-5 text-[0.9375rem] font-semibold text-(--app-heading)">
        {{ $t('customers.overview.tierHistory') }}
      </h2>
      <p v-if="error" class="p-5 text-sm text-(--app-danger-fg)">{{ $t('customers.loadError') }}</p>
      <div v-else-if="!loading && total === 0" class="p-5">
        <AppEmptyState :heading="$t('customers.overview.noTierHistory')" />
      </div>
      <template v-else>
        <div class="mt-3 overflow-x-auto">
          <table class="w-full text-sm" :aria-busy="loading || undefined">
            <thead class="bg-gray-50">
              <tr class="text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
                <th class="px-4 py-3 text-left">{{ $t('customers.col.date') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.filter.program') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.from') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.to') }}</th>
                <th class="px-4 py-3 text-right">{{ $t('customers.col.qualifyingPoints') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.tiers.cause') }}</th>
                <th class="px-4 py-3">
                  <span class="sr-only">{{ $t('customers.activity.openEvent') }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="entry in rows" :key="entry.id" class="border-t">
                <td class="px-4 py-3 whitespace-nowrap text-muted-foreground">
                  {{ formatDateTime(entry.createdAt) }}
                </td>
                <td class="px-4 py-3 text-(--app-text-soft)">{{ entry.programName ?? '—' }}</td>
                <td class="px-4 py-3 text-muted-foreground">{{ entry.fromTierName ?? '—' }}</td>
                <td class="px-4 py-3 font-medium">{{ entry.toTierName }}</td>
                <td class="px-4 py-3 text-right">{{ entry.qualifyingPoints }}</td>
                <td class="px-4 py-3 text-xs text-(--app-text-soft)">
                  {{ $t(`customers.tiers.causes.${entry.cause ?? 'points'}`) }}
                </td>
                <td class="px-4 py-3 text-right">
                  <!-- Only a points-driven change has an event of its own to open. -->
                  <CustomerEventButton
                    v-if="entry.cause === 'points' && entry.sourceEventId"
                    @open="emit('openEvent', { eventId: entry.sourceEventId! })"
                  />
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <AppPaginator
          v-model:page="page"
          :page-size="pageSize"
          :total="total"
          :page-size-options="CUSTOMER_PAGE_SIZES"
          @update:page-size="changePageSize"
        />
      </template>
    </section>
  </div>
</template>
