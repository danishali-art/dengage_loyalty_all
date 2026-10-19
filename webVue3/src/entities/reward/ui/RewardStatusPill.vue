<script setup lang="ts">
import { AppStatusPill } from '@/shared/ui';
import type { Reward } from '../model/types';

/** CR 2026-09-30 (A4): an unapproved cashback can't be bought or earned yet. Presentational. */
const props = defineProps<{ reward: Reward }>();
</script>

<template>
  <AppStatusPill v-if="props.reward.status === 'pending_approval'" tone="neutral" dot>
    {{ $t('rewards.list.pendingApproval') }}
  </AppStatusPill>
  <AppStatusPill v-else :tone="props.reward.isActive ? 'success' : 'neutral'" dot>
    {{ props.reward.isActive ? $t('common.active') : $t('common.inactive') }}
  </AppStatusPill>
</template>
