<script setup lang="ts">
import { toRef } from 'vue';
import { AppEmptyState, AppMeter, AppPaginator, AppStatusPill } from '@/shared/ui';
import { formatDate, formatDateTime } from '@/shared/lib/date';
import { CUSTOMER_PAGE_SIZES } from '@/entities/customer';
import { useStreaksTab } from '../../model/useCustomerTabs';

/** Streaks (port of customer-streaks.tab.ts, P2): progress per campaign and its completion history. */
const props = defineProps<{ contactKey: string }>();
const { streaks, loading, error, pageSize, pageOf, setPage, historyPage, changePageSize } = useStreaksTab(
  toRef(props, 'contactKey'),
);
</script>

<template>
  <div class="flex flex-col gap-6">
    <p v-if="error" class="rounded-(--app-radius) border bg-white p-5 text-sm text-(--app-danger-fg)">
      {{ $t('customers.loadError') }}
    </p>
    <p v-else-if="loading" class="text-sm text-muted-foreground" aria-busy="true">
      {{ $t('common.loading') }}
    </p>
    <template v-else>
      <section
        v-for="s in streaks"
        :key="s.campaignId"
        class="rounded-(--app-radius) border bg-white p-5 shadow-(--app-shadow-card)"
      >
        <div class="mb-3 flex items-center justify-between">
          <h2 class="m-0 text-[0.9375rem] font-semibold text-(--app-heading)">{{ s.campaignName }}</h2>
          <span class="flex items-center gap-2 text-xs text-muted-foreground">
            {{ s.programName }}
            <AppStatusPill :tone="s.status === 'active' ? 'streak' : 'neutral'" dot>{{
              $t(`customers.streaks.status.${s.status}`)
            }}</AppStatusPill>
          </span>
        </div>
        <AppMeter
          :value="s.streakCount"
          :max="s.targetPeriods"
          :label="
            $t('customers.streaks.progress', {
              count: s.streakCount,
              target: s.targetPeriods,
              completions: s.completions,
            })
          "
        />
        <dl class="mt-3 mb-0 grid grid-cols-[11rem_1fr] gap-y-1 text-xs">
          <dt class="text-muted-foreground">{{ $t('customers.streaks.rule') }}</dt>
          <dd class="m-0">
            {{
              $t('customers.streaks.ruleText', { period: s.period, metric: s.metric, threshold: s.threshold })
            }}
          </dd>
          <dt class="text-muted-foreground">{{ $t('customers.streaks.lastMet') }}</dt>
          <dd class="m-0">{{ formatDate(s.lastMetPeriod) }}</dd>
          <template v-if="s.latestPeriodStart">
            <dt class="text-muted-foreground">{{ $t('customers.streaks.latestPeriod') }}</dt>
            <dd class="m-0">
              {{
                $t('customers.streaks.latestPeriodText', {
                  date: formatDate(s.latestPeriodStart),
                  sum: s.latestAggSum ?? '0',
                  count: s.latestAggCount ?? 0,
                })
              }}
              · {{ $t(s.latestPeriodMet ? 'customers.streaks.met' : 'customers.streaks.notMet') }}
            </dd>
          </template>
        </dl>
        <template v-if="s.history.length > 0">
          <table class="mt-4 w-full text-sm">
            <thead>
              <tr class="text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
                <th class="px-2 py-2 text-left">#</th>
                <th class="px-2 py-2 text-left">{{ $t('customers.streaks.completedPeriod') }}</th>
                <th class="px-2 py-2 text-left">{{ $t('customers.streaks.reward') }}</th>
                <th class="px-2 py-2 text-left">{{ $t('customers.col.date') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="h in historyPage(s)" :key="h.completionNo" class="border-t">
                <td class="px-2 py-2">{{ h.completionNo }}</td>
                <td class="px-2 py-2">{{ formatDate(h.completedPeriod) }}</td>
                <td class="px-2 py-2 text-xs text-(--app-text-soft)">
                  {{ $t(`customers.streaks.kind.${h.rewardKind}`) }}
                </td>
                <td class="px-2 py-2 text-muted-foreground">{{ formatDateTime(h.createdAt) }}</td>
              </tr>
            </tbody>
          </table>
          <AppPaginator
            :page="pageOf(s.campaignId)"
            :page-size="pageSize"
            :total="s.history.length"
            :page-size-options="CUSTOMER_PAGE_SIZES"
            @update:page="setPage(s.campaignId, $event)"
            @update:page-size="changePageSize"
          />
        </template>
      </section>
      <AppEmptyState v-if="streaks.length === 0" :heading="$t('customers.streaks.empty')" />
      <p class="m-0 text-xs text-muted-foreground">{{ $t('customers.streaks.note') }}</p>
    </template>
  </div>
</template>
