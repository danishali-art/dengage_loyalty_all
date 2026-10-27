<script setup lang="ts">
import AppIcon from '../icon/AppIcon.vue';

/**
 * An "i" button that carries an explanation as its accessible name and tooltip, so it reaches
 * keyboard and screen-reader users too (account-type form, 1.3.CL item 2).
 */
const props = defineProps<{ text: string }>();
</script>

<template>
  <button
    type="button"
    class="inline-flex cursor-help rounded-sm text-muted-foreground hover:text-primary focus-visible:text-primary"
    :aria-label="props.text"
    :title="props.text"
  >
    <AppIcon name="info" class="size-3.5" />
  </button>
</template>
