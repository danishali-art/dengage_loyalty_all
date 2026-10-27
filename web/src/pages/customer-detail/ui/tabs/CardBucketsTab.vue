<script setup lang="ts">
import { toRef } from 'vue';
import { AppButton, AppEmptyState, AppMeter, AppPaginator } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import { CUSTOMER_PAGE_SIZES, CustomerEventButton, isDebit } from '@/entities/customer';
import { useCardBucketsTab } from '../../model/useCustomerTabs';
import type { OpenEventRequest } from '../../model/useCustomerDetailPage';

/**
 * Card buckets (P3): each bucket in the customer's
 * programs, its per-customer caps against this customer's use (D7), and its postings on demand.
 */
const props = defineProps<{ contactKey: string }>();
const emit = defineEmits<{ openEvent: [request: OpenEventRequest] }>();
const { buckets, loading, error, selected, postings, toggle } = useCardBucketsTab(toRef(props, 'contactKey'));
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
        v-for="b in buckets"
        :key="b.ruleId"
        class="rounded-(--app-radius) border bg-white p-5 shadow-(--app-shadow-card)"
      >
        <div class="mb-2 flex items-center justify-between">
          <h2 class="m-0 text-[0.9375rem] font-semibold text-(--app-heading)">{{ b.name }}</h2>
          <span class="text-xs text-muted-foreground">{{ b.programName }} · {{ b.status }}</span>
        </div>
        <p class="m-0 text-xs text-(--app-text-soft)">
          {{ $t('customers.buckets.summary', { reward: b.rewardAmount ?? '—', postings: b.postings }) }}
          <template v-if="b.lastPostedAt">
            · {{ $t('customers.buckets.last', { date: formatDateTime(b.lastPostedAt) }) }}</template
          >
        </p>
        <div class="mt-3 grid grid-cols-2 gap-4">
          <AppMeter
            v-if="b.perCustomerPerDay"
            :value="b.usedToday"
            :max="b.perCustomerPerDay"
            :label="$t('customers.rules.today', { used: b.usedToday, cap: b.perCustomerPerDay })"
          />
          <AppMeter
            v-if="b.perCustomerTotal"
            :value="b.usedTotal"
            :max="b.perCustomerTotal"
            :label="$t('customers.rules.total', { used: b.usedTotal, cap: b.perCustomerTotal })"
          />
          <p v-if="!b.perCustomerPerDay && !b.perCustomerTotal" class="m-0 text-xs text-muted-foreground">
            {{ $t('customers.buckets.noCap', { used: b.usedTotal }) }}
          </p>
        </div>
        <div v-if="b.postings > 0" class="mt-3">
          <AppButton
            variant="secondary"
            size="sm"
            :pending="selected === b.ruleId && postings.loading.value"
            @click="toggle(b.ruleId)"
          >
            {{
              $t(selected === b.ruleId ? 'customers.buckets.hidePostings' : 'customers.buckets.showPostings')
            }}
          </AppButton>
        </div>
        <template v-if="selected === b.ruleId">
          <ul class="mt-3 mb-0 list-none divide-y p-0 text-sm">
            <li
              v-for="entry in postings.rows.value"
              :key="entry.id"
              class="flex items-center justify-between gap-3 py-2"
            >
              <span class="text-muted-foreground">{{ formatDateTime(entry.createdAt) }}</span>
              <span class="flex-1 text-(--app-text-soft)">{{ $t(`customers.reason.${entry.reason}`) }}</span>
              <span
                class="font-medium"
                :class="isDebit(entry.delta) ? 'text-(--app-danger-fg)' : 'text-(--app-success-fg)'"
                >{{ isDebit(entry.delta) ? '' : '+' }}{{ entry.delta }}</span
              >
              <CustomerEventButton
                @open="emit('openEvent', { eventId: entry.sourceEventId, postingId: entry.id })"
              />
            </li>
          </ul>
          <AppPaginator
            :page="postings.page.value"
            :page-size="postings.pageSize.value"
            :total="postings.total.value"
            :page-size-options="CUSTOMER_PAGE_SIZES"
            @update:page="postings.go"
            @update:page-size="postings.setPageSize"
          />
        </template>
      </section>
      <AppEmptyState v-if="buckets.length === 0" :heading="$t('customers.buckets.empty')" />
    </template>
  </div>
</template>
