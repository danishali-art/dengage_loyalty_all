<script setup lang="ts">
import { toRef } from 'vue';
import { AppEmptyState, AppPaginator, AppStatusPill } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import { CUSTOMER_PAGE_SIZES, CustomerEventButton, rewardOutcomeTone } from '@/entities/customer';
import { useRewardsTab } from '../../model/useCustomerTabs';
import type { OpenEventRequest } from '../../model/useCustomerDetailPage';

/** Rewards (port of customer-rewards.tab.ts, P2): what each reward cost and what it paid. */
const props = defineProps<{ contactKey: string }>();
const emit = defineEmits<{ openEvent: [request: OpenEventRequest] }>();
const { loading, error, paged } = useRewardsTab(toRef(props, 'contactKey'));
const { page, pageSize, rows, total, changePageSize } = paged;
</script>

<template>
  <section class="overflow-hidden rounded-(--app-radius) border bg-white shadow-(--app-shadow-card)">
    <p v-if="error" class="p-5 text-sm text-(--app-danger-fg)">{{ $t('customers.loadError') }}</p>
    <div v-else-if="!loading && total === 0" class="p-5">
      <AppEmptyState :heading="$t('customers.rewards.empty')" />
    </div>
    <template v-else>
      <div class="overflow-x-auto">
        <table class="w-full text-sm" :aria-busy="loading || undefined">
          <thead class="bg-gray-50">
            <tr class="text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
              <th class="px-4 py-3 text-left">{{ $t('customers.col.date') }}</th>
              <th class="px-4 py-3 text-left">{{ $t('customers.rewards.reward') }}</th>
              <th class="px-4 py-3 text-left">{{ $t('customers.rewards.how') }}</th>
              <th class="px-4 py-3 text-right">{{ $t('customers.rewards.cost') }}</th>
              <th class="px-4 py-3 text-left">{{ $t('customers.rewards.payout') }}</th>
              <th class="px-4 py-3">
                <span class="sr-only">{{ $t('customers.activity.openEvent') }}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="r in rows"
              :key="r.sourceEventId + r.source + r.createdAt"
              class="border-t hover:bg-gray-50"
            >
              <td class="px-4 py-3 whitespace-nowrap text-muted-foreground">
                {{ formatDateTime(r.createdAt) }}
              </td>
              <td class="px-4 py-3">
                {{ r.rewardName }}
                <div v-if="r.rewardType" class="text-xs text-muted-foreground">
                  {{ $t(`rewards.type.${r.rewardType}`) }}
                </div>
              </td>
              <td class="px-4 py-3 text-xs text-(--app-text-soft)">
                {{ $t(`rewards.acquisition.${r.source}`) }}
              </td>
              <td class="px-4 py-3 text-right whitespace-nowrap">
                <template v-if="r.cost">
                  {{ r.cost }} <span class="text-xs text-muted-foreground">{{ r.costWalletName }}</span>
                </template>
                <template v-else>—</template>
              </td>
              <td class="px-4 py-3">
                <AppStatusPill :tone="rewardOutcomeTone(r.outcome)" dot>{{
                  $t(`customers.rewards.outcome.${r.outcome}`)
                }}</AppStatusPill>
                <div v-if="r.cashAmount" class="mt-1 text-xs text-(--app-text-soft)">
                  +{{ r.cashAmount }} {{ r.cashWalletName }}
                </div>
                <div v-if="r.tierName" class="mt-1 text-xs text-(--app-text-soft)">{{ r.tierName }}</div>
              </td>
              <td class="px-4 py-3 text-right">
                <CustomerEventButton @open="emit('openEvent', { eventId: r.sourceEventId })" />
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
</template>
