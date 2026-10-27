<script setup lang="ts">
import { AppButton, AppIcon } from '@/shared/ui';

/** The eye button that opens the event drawer (CR 2026-10-02, §3.4). Presentational. */
const emit = defineEmits<{ open: [] }>();
</script>

<template>
  <AppButton variant="text" size="sm" :aria-label="$t('customers.activity.openEvent')" @click="emit('open')">
    <AppIcon name="eye" />
  </AppButton>
</template>
