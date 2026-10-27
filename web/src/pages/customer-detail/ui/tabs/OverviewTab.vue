<script setup lang="ts">
import { AppEmptyState, AppIcon, AppMeter, AppStatusPill } from '@/shared/ui';
import { formatDate } from '@/shared/lib/date';
import { isZeroAmount, walletIcon, type CustomerProfile } from '@/entities/customer';
import ProfilePanel from './ProfilePanel.vue';

/**
 * Overview: the profile panel, and one card per program with
 * its wallets (balance, pending, expiring soon) and progress (tier, streaks). Postings live on the
 * Activity tab. Presentational: the profile is already loaded.
 */
const props = defineProps<{ profile: CustomerProfile }>();
</script>

<template>
  <div class="grid grid-cols-1 gap-6 lg:grid-cols-[17rem_1fr]">
    <ProfilePanel :profile="props.profile" />

    <div class="flex min-w-0 flex-col gap-6">
      <section
        v-for="program in props.profile.programs ?? []"
        :key="program.programId"
        class="rounded-(--app-radius) border bg-white p-5 shadow-(--app-shadow-card)"
      >
        <div class="mb-5 flex flex-wrap items-center justify-between gap-3">
          <div class="flex items-center gap-3">
            <span
              class="flex size-10 items-center justify-center rounded-xl bg-(--app-brand-light) text-primary"
            >
              <AppIcon name="star" class="size-5" />
            </span>
            <h2 class="m-0 text-base font-bold text-(--app-heading)">{{ program.programName }}</h2>
          </div>
          <AppStatusPill v-if="program.tier" tone="info" dot>{{
            program.tier.tierDisplayName ?? $t('customers.overview.noTier')
          }}</AppStatusPill>
        </div>

        <div class="grid grid-cols-1 gap-6 xl:grid-cols-2">
          <div>
            <h3 class="mb-3 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
              {{ $t('customers.overview.wallets') }}
            </h3>
            <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div
                v-for="w in program.wallets"
                :key="w.accountTypeId"
                class="rounded-xl border bg-gray-50 p-4"
              >
                <div class="flex items-center gap-2 text-xs text-muted-foreground">
                  <span
                    class="flex size-7 items-center justify-center rounded-lg bg-white text-primary shadow-sm"
                  >
                    <AppIcon :name="walletIcon(w.type)" />
                  </span>
                  <span class="min-w-0 truncate">{{ w.name }}</span>
                  <span class="ml-auto">{{ w.type }}</span>
                </div>
                <p class="mt-3 mb-0 text-2xl font-semibold">
                  {{ w.balance }}
                  <span class="text-sm font-normal text-muted-foreground">{{ w.currency ?? '' }}</span>
                </p>
                <p v-if="!isZeroAmount(w.pendingAmount)" class="mt-2 mb-0 text-xs text-muted-foreground">
                  {{ $t('customers.overview.pending', { amount: w.pendingAmount }) }}
                </p>
                <p
                  v-if="w.expiringAmount"
                  class="mt-2 mb-0 flex items-center gap-1 text-xs text-(--app-warn-fg)"
                >
                  <AppIcon name="warning" />
                  {{
                    $t('customers.overview.expiring', {
                      amount: w.expiringAmount,
                      date: formatDate(w.expiresOn),
                    })
                  }}
                </p>
              </div>
            </div>
          </div>

          <div>
            <h3 class="mb-3 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase">
              {{ $t('customers.progress.heading') }}
            </h3>
            <div class="flex flex-col gap-4">
              <div v-if="program.tier">
                <p class="mt-0 mb-1 flex items-center gap-1.5 text-sm text-(--app-text-soft)">
                  <AppIcon name="trophy" class="text-primary" />
                  {{ $t('customers.overview.tier') }}
                </p>
                <AppMeter
                  v-if="program.tier.nextTierMinPoints"
                  :value="program.tier.qualifyingPoints"
                  :max="program.tier.nextTierMinPoints"
                  :label="
                    $t('customers.overview.qualifyingOf', {
                      points: program.tier.qualifyingPoints,
                      target: program.tier.nextTierMinPoints,
                      tier: program.tier.nextTierDisplayName,
                    })
                  "
                />
                <p v-else class="m-0 text-xs text-muted-foreground">
                  {{ $t('customers.progress.topTier', { points: program.tier.qualifyingPoints }) }}
                </p>
                <p
                  v-if="program.tier.graceEndsAt || program.tier.lockedUntil"
                  class="mt-1 mb-0 text-xs text-muted-foreground"
                >
                  <template v-if="program.tier.graceEndsAt"
                    >{{ $t('customers.tiers.graceEnds') }}:
                    {{ formatDate(program.tier.graceEndsAt) }}</template
                  >
                  <template v-if="program.tier.lockedUntil">
                    · {{ $t('customers.tiers.lockedUntil') }}:
                    {{ formatDate(program.tier.lockedUntil) }}</template
                  >
                </p>
              </div>
              <div v-for="s in program.streaks" :key="s.campaignId">
                <p class="mt-0 mb-1 flex items-center gap-1.5 text-sm text-(--app-text-soft)">
                  <AppIcon name="flame" class="text-(--app-streak-fg)" />
                  {{ s.campaignName }}
                </p>
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
              </div>
              <p
                v-if="!program.tier && program.streaks.length === 0"
                class="m-0 text-xs text-muted-foreground"
              >
                {{ $t('customers.progress.none') }}
              </p>
            </div>
          </div>
        </div>
      </section>
      <AppEmptyState
        v-if="!(props.profile.programs ?? []).length"
        :heading="$t('customers.overview.noPrograms')"
      />
    </div>
  </div>
</template>
