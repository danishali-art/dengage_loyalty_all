<script setup lang="ts">
import { toRef } from 'vue';
import { AppEmptyState, AppFilterBar, AppInput, AppPaginator, AppSelect } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import {
  CUSTOMER_PAGE_SIZES,
  CustomerEventButton,
  isDebit,
  type CustomerProfile,
  type ReasonGroup,
} from '@/entities/customer';
import { useActivityTab } from '../../model/useCustomerTabs';
import type { OpenEventRequest } from '../../model/useCustomerDetailPage';

/** Activity: every posting, with where it came from (CR 2026-10-02 P1). */
const props = defineProps<{ contactKey: string; profile: CustomerProfile }>();
const emit = defineEmits<{ openEvent: [request: OpenEventRequest] }>();
const { filter, list, programOptions, walletOptions, reasonOptions, search, reset } = useActivityTab(
  toRef(props, 'contactKey'),
  toRef(props, 'profile'),
);
const { rows, loading, error, page, pageSize, total, go, setPageSize } = list;
</script>

<template>
  <section class="overflow-hidden rounded-(--app-radius) border bg-white shadow-(--app-shadow-card)">
    <div class="p-5">
      <AppFilterBar
        :label="$t('customers.filter.activityLabel')"
        :busy="loading"
        @search="search"
        @reset="reset"
      >
        <label class="flex min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.program') }}
          <AppSelect
            :options="programOptions"
            :aria-label="$t('customers.filter.program')"
            :model-value="filter.programId"
            @update:model-value="filter.programId = $event ?? ''"
          />
        </label>
        <label class="flex min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.wallet') }}
          <AppSelect
            :options="walletOptions"
            :aria-label="$t('customers.filter.wallet')"
            :model-value="filter.accountTypeId"
            @update:model-value="filter.accountTypeId = $event ?? ''"
          />
        </label>
        <label class="flex min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.reasonGroup') }}
          <AppSelect
            :options="reasonOptions"
            :aria-label="$t('customers.filter.reasonGroup')"
            :model-value="filter.reasonGroup"
            @update:model-value="filter.reasonGroup = ($event ?? '') as ReasonGroup | ''"
          />
        </label>
        <label class="flex min-w-[12.5rem] flex-[1.5] flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.from') }}
          <AppInput v-model="filter.from" type="datetime-local" />
        </label>
        <label class="flex min-w-[12.5rem] flex-[1.5] flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.to') }}
          <AppInput v-model="filter.to" type="datetime-local" />
        </label>
        <label class="flex min-w-[9rem] flex-[1.2] flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.eventId') }}
          <AppInput v-model="filter.eventId" monospace />
        </label>
      </AppFilterBar>
      <p class="mt-2 mb-0 text-xs text-muted-foreground">{{ $t('customers.filter.localTime') }}</p>
    </div>
    <div class="border-t">
      <p v-if="error" class="p-5 text-sm text-(--app-danger-fg)">{{ $t('customers.loadError') }}</p>
      <div v-else-if="!loading && rows.length === 0" class="p-5">
        <AppEmptyState
          :heading="$t('customers.activity.empty')"
          :description="$t('customers.activity.emptyBody')"
        />
      </div>
      <template v-else>
        <div class="overflow-x-auto">
          <table class="w-full text-sm" :aria-busy="loading || undefined">
            <thead class="bg-gray-50">
              <tr class="text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
                <th class="px-4 py-3 text-left">{{ $t('customers.col.date') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.wallet') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.reason') }}</th>
                <th class="px-4 py-3 text-right">{{ $t('customers.col.amount') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.source') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.eventType') }}</th>
                <th class="px-4 py-3">
                  <span class="sr-only">{{ $t('customers.activity.openEvent') }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="entry in rows" :key="entry.id" class="border-t hover:bg-gray-50">
                <td class="px-4 py-3 whitespace-nowrap text-muted-foreground">
                  {{ formatDateTime(entry.createdAt) }}
                </td>
                <td class="px-4 py-3">
                  {{ entry.accountTypeName }}
                  <div class="text-xs text-muted-foreground">
                    {{ entry.accountTypeType }} · {{ entry.programName }}
                  </div>
                </td>
                <td class="px-4 py-3 text-(--app-text-soft)">{{ $t(`customers.reason.${entry.reason}`) }}</td>
                <td
                  class="px-4 py-3 text-right font-medium whitespace-nowrap"
                  :class="isDebit(entry.delta) ? 'text-(--app-danger-fg)' : 'text-(--app-success-fg)'"
                >
                  {{ isDebit(entry.delta) ? '' : '+' }}{{ entry.delta }}
                </td>
                <td class="px-4 py-3 text-xs text-(--app-text-soft)">
                  <template v-if="entry.ruleName">{{
                    $t('customers.source.rule', { name: entry.ruleName, version: entry.ruleVersion ?? '—' })
                  }}</template>
                  <template v-else-if="entry.campaignName">{{
                    $t('customers.source.streak', { name: entry.campaignName })
                  }}</template>
                  <template v-else>—</template>
                </td>
                <td class="px-4 py-3 font-mono text-xs text-muted-foreground">
                  {{ entry.eventType ?? $t('customers.activity.scheduledJob') }}
                </td>
                <td class="px-4 py-3 text-right">
                  <CustomerEventButton
                    @open="emit('openEvent', { eventId: entry.sourceEventId, postingId: entry.id })"
                  />
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <AppPaginator
          :page="page"
          :page-size="pageSize"
          :total="total"
          :page-size-options="CUSTOMER_PAGE_SIZES"
          @update:page="go"
          @update:page-size="setPageSize"
        />
      </template>
    </div>
  </section>
</template>
