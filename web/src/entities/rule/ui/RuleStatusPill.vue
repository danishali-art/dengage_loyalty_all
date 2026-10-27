<script setup lang="ts">
import { AppStatusPill } from '@/shared/ui';
import type { RuleStatus } from '../model/types';

/** A rule's status in words; pending approval (CR-04, CASH rules) stands out. Presentational. */
const props = defineProps<{ status: RuleStatus }>();
</script>

<template>
  <AppStatusPill
    :tone="
      props.status === 'active' ? 'success' : props.status === 'pending_approval' ? 'warning' : 'neutral'
    "
    dot
  >
    {{ $t(`common.status.${props.status}`) }}
  </AppStatusPill>
</template>
