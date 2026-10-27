<script setup lang="ts">
import { computed, toRef } from 'vue';
import { AppDrawer, AppStatusPill } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import { inboxStatusTone, isDebit } from '@/entities/customer';
import { useEventDrawer } from '../model/useEventDrawer';
import type { OpenEventRequest } from '../model/useCustomerDetailPage';

/** What one event did for this customer. `v-model:request` — null closes it. */
const request = defineModel<OpenEventRequest | null>('request', { required: true });
const props = defineProps<{ contactKey: string }>();
const { detail, loading, notFound, failed, payloadText, copied, hasEffects, snapshots, copyPayload } =
  useEventDrawer(toRef(props, 'contactKey'), request);

const open = computed({
  get: () => request.value !== null,
  set: (value) => {
    if (!value) request.value = null;
  },
});
const signed = (delta: string): string => (isDebit(delta) ? delta : `+${delta}`);
</script>

<template>
  <AppDrawer
    v-model:open="open"
    :heading="$t('customers.drawer.heading')"
    :subheading="request?.eventId"
    :busy="loading"
  >
    <p v-if="loading" class="text-sm text-muted-foreground">{{ $t('common.loading') }}</p>
    <p v-else-if="notFound" class="text-sm text-muted-foreground">{{ $t('customers.drawer.notFound') }}</p>
    <p v-else-if="failed" class="text-sm text-(--app-danger-fg)">{{ $t('customers.loadError') }}</p>
    <div v-else-if="detail" class="flex flex-col gap-6 text-sm">
      <!-- 1. Wallets involved -->
      <section>
        <h3 class="mb-2 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
          {{ $t('customers.drawer.wallets') }}
        </h3>
        <p
          v-if="detail.postings.length === 0 && detail.heldPostings.length === 0"
          class="text-muted-foreground"
        >
          {{ $t('customers.drawer.noPostings') }}
        </p>
        <ul class="m-0 flex list-none flex-col gap-2 p-0">
          <li
            v-for="p in detail.postings"
            :key="p.id"
            class="rounded-lg border px-3 py-2"
            :class="p.id === request?.postingId ? 'border-primary bg-(--app-brand-light)' : ''"
            :data-highlighted="p.id === request?.postingId || undefined"
          >
            <div class="flex items-center justify-between gap-2">
              <span class="font-medium">
                {{ p.accountTypeName }}
                <span class="text-xs text-muted-foreground"
                  >· {{ p.accountTypeType }} · {{ p.programName }}</span
                >
              </span>
              <span
                class="font-medium"
                :class="isDebit(p.delta) ? 'text-(--app-danger-fg)' : 'text-(--app-success-fg)'"
                >{{ signed(p.delta) }}</span
              >
            </div>
            <div class="mt-1 text-xs text-muted-foreground">
              {{ $t(`customers.reason.${p.reason}`) }}
              <template v-if="p.ruleName">
                · {{ $t('customers.source.rule', { name: p.ruleName, version: p.ruleVersion ?? '—' }) }}
              </template>
              <template v-if="p.campaignName">
                · {{ $t('customers.source.streak', { name: p.campaignName }) }}</template
              >
              <template v-if="p.contactKey !== props.contactKey">
                · {{ $t('customers.drawer.counterparty', { contactKey: p.contactKey }) }}
              </template>
            </div>
          </li>
          <li v-for="h in detail.heldPostings" :key="h.id" class="rounded-lg border border-dashed px-3 py-2">
            <div class="flex items-center justify-between gap-2">
              <span class="font-medium">{{ h.accountTypeName }}</span>
              <span class="text-(--app-text-soft)">{{ h.delta }}</span>
            </div>
            <div class="mt-1 text-xs text-muted-foreground">
              <template v-if="h.cancelledAt">{{
                $t('customers.drawer.heldCancelled', { date: formatDateTime(h.cancelledAt) })
              }}</template>
              <template v-else-if="h.postedAt">{{
                $t('customers.drawer.heldPosted', { date: formatDateTime(h.postedAt) })
              }}</template>
              <template v-else>{{
                $t('customers.drawer.heldUntil', { date: formatDateTime(h.holdUntil) })
              }}</template>
              <template v-if="h.refundedDelta && !h.cancelledAt">
                · {{ $t('customers.drawer.heldRefunded', { amount: h.refundedDelta }) }}
              </template>
              <template v-if="h.ruleName"> · {{ h.ruleName }}</template>
            </div>
          </li>
        </ul>
      </section>

      <!-- 2. Event -->
      <section>
        <h3 class="mb-2 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
          {{ $t('customers.drawer.event') }}
        </h3>
        <template v-if="detail.event">
          <dl class="m-0 grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 text-xs">
            <dt class="text-muted-foreground">{{ $t('customers.col.eventType') }}</dt>
            <dd class="m-0 font-mono">{{ detail.event.eventType }}</dd>
            <dt class="text-muted-foreground">{{ $t('customers.col.status') }}</dt>
            <dd class="m-0">
              <AppStatusPill :tone="inboxStatusTone(detail.event.status)" dot>{{
                $t(`customers.status.${detail.event.status}`)
              }}</AppStatusPill>
            </dd>
            <dt class="text-muted-foreground">{{ $t('customers.col.occurred') }}</dt>
            <dd class="m-0">{{ formatDateTime(detail.event.occurredAt) }}</dd>
            <dt class="text-muted-foreground">{{ $t('customers.col.received') }}</dt>
            <dd class="m-0">{{ formatDateTime(detail.event.receivedAt) }}</dd>
            <dt class="text-muted-foreground">{{ $t('customers.col.processed') }}</dt>
            <dd class="m-0">{{ formatDateTime(detail.event.processedAt) }}</dd>
            <template v-if="detail.event.error">
              <dt class="text-muted-foreground">{{ $t('customers.col.error') }}</dt>
              <dd class="m-0 font-mono text-(--app-danger-fg)">{{ detail.event.error }}</dd>
            </template>
          </dl>
          <div class="mt-3">
            <div class="mb-1 flex items-center justify-between">
              <span class="text-xs text-muted-foreground">{{ $t('customers.drawer.payload') }}</span>
              <button
                type="button"
                class="rounded px-2 py-0.5 text-xs text-muted-foreground hover:bg-gray-100"
                @click="copyPayload"
              >
                {{ $t(copied ? 'customers.drawer.copied' : 'customers.drawer.copy') }}
              </button>
            </div>
            <pre
              class="m-0 max-h-72 overflow-auto rounded-lg bg-gray-50 p-3 font-mono text-xs"
              data-payload
              >{{ payloadText }}</pre>
            <p class="mt-1 mb-0 text-xs text-muted-foreground">{{ $t('customers.drawer.masked') }}</p>
          </div>
        </template>
        <p v-else class="text-muted-foreground">{{ $t('customers.drawer.noInbound') }}</p>
      </section>

      <!-- 3. Why -->
      <section>
        <h3 class="mb-2 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
          {{ $t('customers.drawer.why') }}
        </h3>
        <p v-if="detail.ruleFires.length === 0" class="text-muted-foreground">
          {{ $t('customers.drawer.noRuleFires') }}
        </p>
        <p
          v-for="s in detail.onceOnlySkips ?? []"
          :key="s.ruleId"
          class="mb-2 rounded-lg border border-dashed px-3 py-2 text-(--app-text-soft)"
        >
          {{
            $t('customers.drawer.onceOnlySkip', {
              name: s.ruleName ?? s.ruleId,
              date: formatDateTime(s.earlierAt),
              eventId: s.earlierEventId,
            })
          }}
        </p>
        <details v-for="f in detail.ruleFires" :key="f.id" class="mb-2 rounded-lg border px-3 py-2">
          <summary class="cursor-pointer">
            {{ $t('customers.source.rule', { name: f.ruleName ?? f.ruleId, version: f.ruleVersion }) }}
            · {{ f.resultingDelta }}
          </summary>
          <template v-for="snap in snapshots(f)" :key="snap.label">
            <p class="mt-2 mb-0 text-xs text-muted-foreground">{{ $t(snap.label) }}</p>
            <pre class="m-0 overflow-auto rounded bg-gray-50 p-2 font-mono text-xs">{{ snap.json }}</pre>
          </template>
        </details>
      </section>

      <!-- 4. Everything else this event did -->
      <section>
        <h3 class="mb-2 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
          {{ $t('customers.drawer.effects') }}
        </h3>
        <p v-if="!hasEffects" class="text-muted-foreground">{{ $t('customers.drawer.noEffects') }}</p>
        <ul class="m-0 flex list-none flex-col gap-1 p-0 text-xs">
          <li v-for="s in detail.streaksApplied" :key="s.campaignId">
            {{ $t('customers.drawer.streakApplied', { name: s.campaignName ?? s.campaignId }) }}
          </li>
          <li v-for="c in detail.streakCompletions" :key="c.campaignId + c.completionNo">
            {{
              $t('customers.drawer.streakCompleted', {
                name: c.campaignName ?? c.campaignId,
                n: c.completionNo,
                reward: c.rewardKind,
              })
            }}
          </li>
          <li v-for="r in detail.rewards" :key="r.id">
            {{ $t('customers.drawer.reward', { name: r.rewardName, status: r.status }) }}
          </li>
          <li v-for="t in detail.tierChanges" :key="t.id">
            {{ $t('customers.drawer.tierChange', { from: t.fromTierName ?? '—', to: t.toTierName }) }}
          </li>
        </ul>
      </section>

      <!-- 5. Messages sent (D5: no payload) -->
      <section>
        <h3 class="mb-2 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
          {{ $t('customers.drawer.messages') }}
        </h3>
        <p v-if="detail.messages.length === 0" class="text-muted-foreground">
          {{ $t('customers.drawer.noMessages') }}
        </p>
        <ul class="m-0 flex list-none flex-col gap-1 p-0 text-xs">
          <li v-for="m in detail.messages" :key="m.eventId" class="flex items-center justify-between gap-2">
            <span class="font-mono">{{ m.eventType }}</span>
            <span class="text-muted-foreground">{{ m.status }} · {{ formatDateTime(m.createdAt) }}</span>
          </li>
        </ul>
      </section>
    </div>
  </AppDrawer>
</template>
