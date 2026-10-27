<script setup lang="ts">
import { toRef } from 'vue';
import { AppEmptyState, AppFilterBar, AppInput, AppPaginator, AppSelect, AppStatusPill } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import { CUSTOMER_PAGE_SIZES, outboxStatusTone, type OutboxStatus } from '@/entities/customer';
import { useMessagesTab } from '../../model/useCustomerTabs';

/**
 * Messages sent (P3): outbound events for the customer — type,
 * status, failure reason, times and dedup key. D5: never the payload.
 */
const props = defineProps<{ contactKey: string }>();
const { filter, list, statusOptions, search, reset } = useMessagesTab(toRef(props, 'contactKey'));
const { rows, loading, error, page, pageSize, total, go, setPageSize } = list;
</script>

<template>
  <section class="overflow-hidden rounded-(--app-radius) border bg-white shadow-(--app-shadow-card)">
    <div class="p-5">
      <AppFilterBar
        :label="$t('customers.filter.messagesLabel')"
        :busy="loading"
        @search="search"
        @reset="reset"
      >
        <label class="flex min-w-[9rem] flex-[1.2] flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.eventType') }}
          <AppInput
            v-model="filter.eventType"
            monospace
            :placeholder="$t('customers.messages.typePlaceholder')"
          />
        </label>
        <label class="flex min-w-[6.5rem] flex-1 flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.filter.status') }}
          <AppSelect
            :options="statusOptions"
            :aria-label="$t('customers.filter.status')"
            :model-value="filter.status"
            @update:model-value="filter.status = ($event ?? '') as OutboxStatus | ''"
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
        {{ $t('customers.filter.localTime') }} {{ $t('customers.messages.retention') }}
      </p>
    </div>
    <div class="border-t">
      <p v-if="error" class="p-5 text-sm text-(--app-danger-fg)">{{ $t('customers.loadError') }}</p>
      <div v-else-if="!loading && rows.length === 0" class="p-5">
        <AppEmptyState :heading="$t('customers.messages.empty')" />
      </div>
      <template v-else>
        <div class="overflow-x-auto">
          <table class="w-full text-sm" :aria-busy="loading || undefined">
            <thead class="bg-gray-50">
              <tr class="text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
                <th class="px-4 py-3 text-left">{{ $t('customers.messages.created') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.eventType') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.col.status') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.messages.reason') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.messages.published') }}</th>
                <th class="px-4 py-3 text-left">{{ $t('customers.messages.dedupKey') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="m in rows" :key="m.eventId" class="border-t hover:bg-gray-50">
                <td class="px-4 py-3 whitespace-nowrap text-muted-foreground">
                  {{ formatDateTime(m.createdAt) }}
                </td>
                <td class="px-4 py-3 font-mono text-xs">{{ m.eventType }}</td>
                <td class="px-4 py-3">
                  <AppStatusPill :tone="outboxStatusTone(m.status)" dot>{{
                    $t(`customers.messages.status.${m.status}`)
                  }}</AppStatusPill>
                </td>
                <td class="px-4 py-3 font-mono text-xs">{{ m.reason || '—' }}</td>
                <td class="px-4 py-3 whitespace-nowrap text-muted-foreground">
                  {{ formatDateTime(m.publishedAt) }}
                </td>
                <td
                  class="max-w-xs truncate px-4 py-3 font-mono text-xs text-muted-foreground"
                  :title="m.dedupKey ?? ''"
                >
                  {{ m.dedupKey ?? '—' }}
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
