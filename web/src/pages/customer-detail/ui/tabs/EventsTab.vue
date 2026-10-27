<script setup lang="ts">
import { toRef } from 'vue';
import { AppEmptyState, AppFilterBar, AppInput, AppPaginator, AppSelect, AppStatusPill } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import {
  CUSTOMER_PAGE_SIZES,
  CustomerEventButton,
  inboxStatusTone,
  isDebit,
  type InboxStatus,
} from '@/entities/customer';
import { useEventsTab } from '../../model/useCustomerTabs';
import type { OpenEventRequest } from '../../model/useCustomerDetailPage';

/** Events: what the customer sent, received after CR 2026-10-02 (P1). */
const props = defineProps<{ contactKey: string }>();
const emit = defineEmits<{ openEvent: [request: OpenEventRequest] }>();
const { filter, list, typeOptions, statusOptions, search, reset } = useEventsTab(toRef(props, 'contactKey'));
const { rows, loading, error, page, pageSize, total, go, setPageSize } = list;
</script>

<template>
  <section class="overflow-hidden rounded-(--app-radius) border bg-white shadow-(--app-shadow-card)">
    <div class="p-5">
      <AppFilterBar
        :label="$t('customers.filter.eventsLabel')"
        :busy="loading"
        @search="search"
        @reset="reset"
      >
        <label class="flex min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.eventType') }}
          <AppSelect
            searchable
            :options="typeOptions"
            :aria-label="$t('customers.filter.eventType')"
            :model-value="filter.eventType"
            @update:model-value="filter.eventType = $event ?? ''"
          />
        </label>
        <label class="flex min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.status') }}
          <AppSelect
            :options="statusOptions"
            :aria-label="$t('customers.filter.status')"
            :model-value="filter.status"
            @update:model-value="filter.status = ($event ?? '') as InboxStatus | ''"
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
      </AppFilterBar>
      <p class="mt-2 mb-0 text-xs text-muted-foreground">
        {{ $t('customers.filter.localTime') }} {{ $t('customers.events.sinceDeploy') }}
      </p>
    </div>
    <div class="border-t">
      <p v-if="error" class="p-5 text-sm text-(--app-danger-fg)">{{ $t('customers.loadError') }}</p>
      <div v-else-if="!loading && rows.length === 0" class="p-5">
        <AppEmptyState
          :heading="$t('customers.events.empty')"
          :description="$t('customers.events.emptyBody')"
        />
      </div>
      <template v-else>
        <div class="overflow-x-auto">
          <table class="w-full text-sm" :aria-busy="loading || undefined">
            <thead class="bg-gray-50">
              <tr class="text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
                <th class="px-4 py-3 text-left">{{ $t('customers.col.received') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.eventType') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.status') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.outcome') }}</th>
                <th class="px-4 py-3">
                  <span class="sr-only">{{ $t('customers.activity.openEvent') }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="event in rows" :key="event.eventId" class="border-t hover:bg-gray-50">
                <td class="px-4 py-3 whitespace-nowrap text-muted-foreground">
                  {{ formatDateTime(event.receivedAt) }}
                </td>
                <td class="px-4 py-3 font-mono text-xs">{{ event.eventType }}</td>
                <td class="px-4 py-3">
                  <AppStatusPill :tone="inboxStatusTone(event.status)" dot>{{
                    $t(`customers.status.${event.status}`)
                  }}</AppStatusPill>
                  <div
                    v-if="event.error"
                    class="mt-1 max-w-xs truncate font-mono text-xs text-(--app-danger-fg)"
                    :title="event.error"
                  >
                    {{ event.error }}
                  </div>
                </td>
                <td class="px-4 py-3 text-xs text-(--app-text-soft)">
                  <div v-for="o in event.outcome" :key="o.accountTypeId">
                    {{ o.accountTypeName }}:
                    <span :class="isDebit(o.netDelta) ? 'text-(--app-danger-fg)' : 'text-(--app-success-fg)'"
                      >{{ isDebit(o.netDelta) ? '' : '+' }}{{ o.netDelta }}</span
                    >
                  </div>
                  <span v-if="event.outcome.length === 0" class="text-muted-foreground">{{
                    $t('customers.events.noPostings')
                  }}</span>
                </td>
                <td class="px-4 py-3 text-right">
                  <CustomerEventButton @open="emit('openEvent', { eventId: event.eventId })" />
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
