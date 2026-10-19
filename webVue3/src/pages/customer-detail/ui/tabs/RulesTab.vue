<script setup lang="ts">
import { toRef } from 'vue';
import { AppEmptyState, AppFilterBar, AppInput, AppPaginator, AppStatusPill } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import { CUSTOMER_PAGE_SIZES, CustomerEventButton, type CapTile } from '@/entities/customer';
import { useRulesTab } from '../../model/useCustomerTabs';
import type { OpenEventRequest } from '../../model/useCustomerDetailPage';

/**
 * Rules & caps (port of customer-rules.tab.ts, P2): one tile per capped rule, its ring showing the
 * tightest cap; then every rule decision for the customer — clicking a rule narrows to it.
 */
const props = defineProps<{ contactKey: string }>();
const emit = defineEmits<{ openEvent: [request: OpenEventRequest] }>();
const {
  tiles,
  reachedCount,
  capsLoading,
  capsError,
  filter,
  ruleFilter,
  fires,
  search,
  reset,
  filterRule,
  clearRule,
} = useRulesTab(toRef(props, 'contactKey'));
const { rows, loading, error, page, pageSize, total, go, setPageSize } = fires;

const ring = (tile: CapTile): string =>
  `conic-gradient(${tile.reached ? 'var(--app-warn-fg)' : 'var(--app-brand)'} ${tile.percent}%, #f3f4f6 0)`;
</script>

<template>
  <div class="flex flex-col gap-6">
    <section class="rounded-(--app-radius) border bg-white p-5 shadow-(--app-shadow-card)">
      <div class="mb-3 flex items-center justify-between gap-3">
        <h2 class="m-0 text-[0.9375rem] font-semibold text-(--app-heading)">
          {{ $t('customers.rules.caps') }}
        </h2>
        <span v-if="tiles.length" class="text-xs text-muted-foreground">{{
          $t('customers.rules.summary', { count: tiles.length, reached: reachedCount })
        }}</span>
      </div>
      <p v-if="capsError" class="text-sm text-(--app-danger-fg)">{{ $t('customers.loadError') }}</p>
      <p v-else-if="!capsLoading && tiles.length === 0" class="text-sm text-muted-foreground">
        {{ $t('customers.rules.noCaps') }}
      </p>
      <template v-else>
        <div
          class="-mx-1 flex snap-x snap-mandatory gap-4 overflow-x-auto px-1 pt-1 pb-3"
          role="list"
          tabindex="0"
          :aria-label="$t('customers.rules.caps')"
          :aria-busy="capsLoading || undefined"
        >
          <article
            v-for="tile in tiles"
            :key="tile.cap.ruleId"
            role="listitem"
            class="w-80 shrink-0 snap-start rounded-xl border border-t-[3px] bg-white p-4 shadow-sm"
            :class="tile.reached ? 'border-t-(--app-warn-fg)' : 'border-t-primary'"
          >
            <header class="flex items-start justify-between gap-2">
              <div class="min-w-0">
                <h3 class="m-0 truncate text-sm font-semibold" :title="tile.cap.ruleName">
                  {{ tile.cap.ruleName }}
                </h3>
                <p class="m-0 truncate text-xs text-muted-foreground">{{ tile.cap.programName }}</p>
              </div>
              <AppStatusPill :tone="tile.reached ? 'warning' : 'success'" dot>{{
                $t(tile.reached ? 'customers.rules.reached' : 'customers.rules.earning')
              }}</AppStatusPill>
            </header>
            <div class="mt-4 flex items-center gap-4">
              <div
                class="flex size-16 shrink-0 items-center justify-center rounded-full"
                role="img"
                :aria-label="$t('customers.rules.tightest', { percent: tile.percent })"
                :style="{ background: ring(tile) }"
              >
                <span
                  class="flex size-12 items-center justify-center rounded-full bg-white text-sm font-semibold"
                  >{{ tile.percent }}%</span
                >
              </div>
              <dl class="m-0 flex min-w-0 flex-1 flex-col gap-1.5 text-xs">
                <div v-for="w in tile.windows" :key="w.key" class="flex justify-between gap-2">
                  <dt class="text-muted-foreground">
                    {{ $t(w.labelKey, { period: tile.cap.period ?? '' }) }}
                  </dt>
                  <dd v-if="w.cap" class="m-0 font-medium tabular-nums">{{ w.used }} / {{ w.cap }}</dd>
                  <dd v-else class="m-0 text-muted-foreground">{{ $t('customers.rules.noCap') }}</dd>
                </div>
              </dl>
            </div>
          </article>
        </div>
        <p class="mt-1 mb-0 text-xs text-muted-foreground">
          {{ $t('customers.rules.capsNote') }} {{ $t('customers.rules.bucketsElsewhere') }}
        </p>
      </template>
    </section>

    <section class="overflow-hidden rounded-(--app-radius) border bg-white shadow-(--app-shadow-card)">
      <div class="p-5">
        <AppFilterBar
          :label="$t('customers.filter.rulesLabel')"
          :busy="loading"
          @search="search"
          @reset="reset"
        >
          <label class="flex min-w-[12.5rem] flex-[1.5] flex-col gap-1 text-xs text-muted-foreground">
            {{ $t('customers.filter.from') }}
            <AppInput v-model="filter.from" type="datetime-local" />
          </label>
          <label class="flex min-w-[12.5rem] flex-[1.5] flex-col gap-1 text-xs text-muted-foreground">
            {{ $t('customers.filter.to') }}
            <AppInput v-model="filter.to" type="datetime-local" />
          </label>
        </AppFilterBar>
        <p class="mt-2 mb-0 text-xs text-muted-foreground">{{ $t('customers.filter.localTime') }}</p>
        <p v-if="ruleFilter" class="mt-2 mb-0 text-xs text-(--app-text-soft)">
          {{ $t('customers.rules.onlyRule', { name: ruleFilter.name }) }}
          <button type="button" class="ml-2 text-primary underline" @click="clearRule">
            {{ $t('customers.rules.allRules') }}
          </button>
        </p>
      </div>
      <div class="border-t">
        <p v-if="error" class="p-5 text-sm text-(--app-danger-fg)">{{ $t('customers.loadError') }}</p>
        <div v-else-if="!loading && rows.length === 0" class="p-5">
          <AppEmptyState :heading="$t('customers.rules.noFires')" />
        </div>
        <template v-else>
          <div class="overflow-x-auto">
            <table class="w-full text-sm" :aria-busy="loading || undefined">
              <thead class="bg-gray-50">
                <tr class="text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
                  <th class="px-4 py-3 text-left">{{ $t('customers.col.date') }}</th>
                  <th class="px-4 py-3 text-left">{{ $t('customers.col.rule') }}</th>
                  <th class="px-4 py-3 text-right">{{ $t('customers.col.amount') }}</th>
                  <th class="px-4 py-3 text-left">{{ $t('customers.col.eventType') }}</th>
                  <th class="px-4 py-3 text-left">{{ $t('customers.col.details') }}</th>
                  <th class="px-4 py-3">
                    <span class="sr-only">{{ $t('customers.activity.openEvent') }}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="fire in rows" :key="fire.id" class="border-t align-top hover:bg-gray-50">
                  <td class="px-4 py-3 whitespace-nowrap text-muted-foreground">
                    {{ formatDateTime(fire.createdAt) }}
                  </td>
                  <td class="px-4 py-3">
                    <button
                      type="button"
                      class="text-left text-primary hover:underline"
                      @click="filterRule(fire.ruleId, fire.ruleName ?? fire.ruleId)"
                    >
                      {{
                        $t('customers.source.rule', {
                          name: fire.ruleName ?? fire.ruleId,
                          version: fire.ruleVersion,
                        })
                      }}
                    </button>
                  </td>
                  <td class="px-4 py-3 text-right font-medium">{{ fire.resultingDelta }}</td>
                  <td class="px-4 py-3 font-mono text-xs text-muted-foreground">
                    {{ fire.eventType ?? $t('customers.activity.scheduledJob') }}
                  </td>
                  <td class="px-4 py-3 text-xs">
                    <details>
                      <summary class="cursor-pointer text-muted-foreground">
                        {{ $t('customers.rules.snapshots') }}
                      </summary>
                      <template v-if="fire.conditionsSnapshot">
                        <p class="mt-2 mb-0 text-muted-foreground">{{ $t('customers.drawer.conditions') }}</p>
                        <pre class="m-0 overflow-auto rounded bg-gray-50 p-2 font-mono">{{
                          fire.conditionsSnapshot
                        }}</pre>
                      </template>
                      <p class="mt-2 mb-0 text-muted-foreground">{{ $t('customers.drawer.calculation') }}</p>
                      <pre class="m-0 overflow-auto rounded bg-gray-50 p-2 font-mono">{{
                        fire.calculationSnapshot
                      }}</pre>
                      <template v-if="fire.resolutionSnapshot">
                        <p class="mt-2 mb-0 text-muted-foreground">{{ $t('customers.drawer.resolution') }}</p>
                        <pre class="m-0 overflow-auto rounded bg-gray-50 p-2 font-mono">{{
                          fire.resolutionSnapshot
                        }}</pre>
                      </template>
                    </details>
                  </td>
                  <td class="px-4 py-3 text-right">
                    <CustomerEventButton
                      @open="
                        emit('openEvent', {
                          eventId: fire.sourceEventId,
                          postingId: fire.ledgerEntryId ?? undefined,
                        })
                      "
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
  </div>
</template>
