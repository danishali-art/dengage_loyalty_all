<script setup lang="ts">
import { AppButton, AppEmptyState, AppInput, AppPageHeader, AppPaginator, AppStatusPill } from '@/shared/ui';
import { formatDate, formatDateTime } from '@/shared/lib/date';
import { useCustomersPage } from '../model/useCustomersPage';

const {
  search,
  rows,
  details,
  page,
  pageSize,
  pageSizes,
  total,
  loading,
  searched,
  submit,
  reset,
  changePageSize,
  open,
} = useCustomersPage();
</script>

<template>
  <AppPageHeader :heading="$t('nav.customers')" :subtitle="$t('customers.list.subtitle')" />

  <div class="mx-auto max-w-7xl px-8 py-6">
    <section class="overflow-hidden rounded-(--app-radius) border bg-white shadow-(--app-shadow-card)">
      <form
        class="flex flex-wrap items-end gap-3 p-5"
        role="search"
        :aria-label="$t('customers.list.searchLabel')"
        @submit.prevent="submit"
      >
        <label class="flex min-w-[16rem] flex-1 flex-col gap-1 text-xs text-muted-foreground">
          {{ $t('customers.list.searchLabel') }}
          <AppInput v-model="search" monospace :placeholder="$t('customers.list.searchPlaceholder')" />
        </label>
        <div class="flex shrink-0 gap-2">
          <AppButton type="submit" :pending="loading">{{ $t('customers.filter.search') }}</AppButton>
          <AppButton variant="secondary" @click="reset">{{ $t('customers.filter.reset') }}</AppButton>
        </div>
        <p class="m-0 basis-full text-xs text-muted-foreground">{{ $t('customers.list.searchHint') }}</p>
      </form>

      <div class="border-t">
        <div v-if="!loading && rows.length === 0" class="p-5">
          <AppEmptyState
            :heading="$t(searched ? 'customers.list.noMatch' : 'customers.list.empty')"
            :description="$t(searched ? 'customers.list.noMatchBody' : 'customers.list.emptyBody')"
            icon="👥"
          />
        </div>
        <template v-else>
          <div class="overflow-x-auto">
            <table class="w-full text-sm" :aria-busy="loading || undefined">
              <thead class="bg-gray-50">
                <tr class="text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
                  <th class="px-4 py-3 text-left">{{ $t('customers.list.col.contactKey') }}</th>
                  <th class="px-4 py-3 text-right">{{ $t('customers.list.col.programs') }}</th>
                  <th class="px-4 py-3 text-right">{{ $t('customers.list.col.accounts') }}</th>
                  <th class="px-4 py-3 text-left">{{ $t('customers.list.col.firstSeen') }}</th>
                  <th class="px-4 py-3 text-left">{{ $t('customers.list.col.lastActivity') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="c in rows"
                  :key="c.contactKey"
                  class="cursor-pointer border-t hover:bg-gray-50"
                  tabindex="0"
                  @click="open(c.contactKey)"
                  @keydown.enter="open(c.contactKey)"
                >
                  <td class="px-4 py-3 font-mono text-sm font-medium">{{ c.contactKey }}</td>
                  <td class="px-4 py-3 text-right">
                    <span v-if="details[c.contactKey]" :title="details[c.contactKey]!.programs.join('\n')">
                      <AppStatusPill tone="neutral">{{
                        details[c.contactKey]!.programs.length
                      }}</AppStatusPill>
                    </span>
                    <div
                      v-else
                      class="ml-auto h-4 w-8 animate-pulse rounded bg-gray-200"
                      aria-hidden="true"
                    />
                  </td>
                  <td class="px-4 py-3 text-right">
                    <AppStatusPill tone="neutral">{{ c.accountCount }}</AppStatusPill>
                  </td>
                  <td class="px-4 py-3 whitespace-nowrap text-muted-foreground">
                    <template v-if="details[c.contactKey]">{{
                      formatDate(details[c.contactKey]!.firstSeenAt)
                    }}</template>
                    <div v-else class="h-4 w-20 animate-pulse rounded bg-gray-200" aria-hidden="true" />
                  </td>
                  <td class="px-4 py-3 whitespace-nowrap text-muted-foreground">
                    {{ formatDateTime(c.lastActivityAt) }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <AppPaginator
            v-model:page="page"
            :page-size="pageSize"
            :total="total"
            :page-size-options="pageSizes"
            @update:page-size="changePageSize"
          />
        </template>
      </div>
    </section>
  </div>
</template>
