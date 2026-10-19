<script setup lang="ts">
import { AppIcon, AppStatusPill } from '@/shared/ui';
import { formatDate, formatDateTime } from '@/shared/lib/date';
import type { CustomerProfile } from '@/entities/customer';

/**
 * Overview's left column (port of customer-profile-panel.ts): who the customer is in loyalty
 * terms — contact key, the tier held in each program, and engagement facts. Presentational.
 */
const props = defineProps<{ profile: CustomerProfile }>();
</script>

<template>
  <aside class="flex flex-col gap-6 rounded-(--app-radius) border bg-white p-5 shadow-(--app-shadow-card)">
    <div class="flex flex-col items-center text-center">
      <div class="flex size-20 items-center justify-center rounded-2xl bg-(--app-brand-light) text-primary">
        <AppIcon name="user" class="size-8" />
      </div>
      <p class="mt-3 mb-0 font-mono text-sm font-medium break-all">{{ props.profile.contactKey }}</p>
      <p v-if="props.profile.summary" class="mt-1 mb-0 text-xs text-muted-foreground">
        {{ $t('customers.panel.memberSince') }} {{ formatDate(props.profile.summary.firstSeenAt) }}
      </p>
    </div>

    <section>
      <h3 class="mb-2 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
        {{ $t('customers.panel.programs') }}
      </h3>
      <ul class="m-0 flex list-none flex-col gap-2 p-0">
        <li
          v-for="p in props.profile.programs ?? []"
          :key="p.programId"
          class="flex items-center justify-between gap-2"
        >
          <span class="text-sm text-(--app-text-soft)">{{ p.programName }}</span>
          <AppStatusPill v-if="p.tier" tone="info" dot>{{
            p.tier.tierDisplayName ?? $t('customers.overview.noTier')
          }}</AppStatusPill>
          <span v-else class="text-xs text-muted-foreground">{{ $t('customers.panel.noTiers') }}</span>
        </li>
        <li v-if="!(props.profile.programs ?? []).length" class="text-xs text-muted-foreground">
          {{ $t('customers.overview.noPrograms') }}
        </li>
      </ul>
    </section>

    <section v-if="props.profile.summary">
      <h3 class="mb-2 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
        {{ $t('customers.panel.engagement') }}
      </h3>
      <dl class="m-0 flex flex-col gap-3 text-sm">
        <div>
          <dt class="text-xs text-muted-foreground">{{ $t('customers.detail.lastActivity') }}</dt>
          <dd class="m-0">{{ formatDateTime(props.profile.summary.lastActivityAt) }}</dd>
        </div>
        <div class="flex items-center justify-between">
          <dt class="text-xs text-muted-foreground">{{ $t('customers.panel.streaksInProgress') }}</dt>
          <dd class="m-0">
            <AppStatusPill :tone="props.profile.summary.activeStreakCount > 0 ? 'streak' : 'neutral'">{{
              props.profile.summary.activeStreakCount
            }}</AppStatusPill>
          </dd>
        </div>
        <div class="flex items-center justify-between">
          <dt class="text-xs text-muted-foreground">{{ $t('customers.panel.failedEvents') }}</dt>
          <dd class="m-0">
            <AppStatusPill
              :tone="props.profile.summary.failedEventsLast7Days > 0 ? 'danger' : 'neutral'"
              :dot="props.profile.summary.failedEventsLast7Days > 0"
              >{{ props.profile.summary.failedEventsLast7Days }}</AppStatusPill
            >
          </dd>
        </div>
        <div class="flex items-center justify-between">
          <dt class="text-xs text-muted-foreground">{{ $t('customers.panel.wallets') }}</dt>
          <dd class="m-0">
            <AppStatusPill tone="neutral">{{ props.profile.balances.length }}</AppStatusPill>
          </dd>
        </div>
      </dl>
    </section>
  </aside>
</template>
