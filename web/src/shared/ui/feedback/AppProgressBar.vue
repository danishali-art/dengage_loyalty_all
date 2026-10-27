<script setup lang="ts">
/** Thin top-of-page bar while any API request is in flight (driven by shared/lib/loading). */
const props = defineProps<{ active: boolean }>();
</script>

<template>
  <div class="fixed inset-x-0 top-0 z-60 h-[3px] overflow-hidden" :aria-hidden="!props.active">
    <div v-if="props.active" class="app-progress__bar h-full w-1/3 bg-primary" />
  </div>
</template>

<style scoped>
.app-progress__bar {
  animation: app-progress 1.2s ease-in-out infinite;
}
@keyframes app-progress {
  from {
    transform: translateX(-100%);
  }
  to {
    transform: translateX(300%);
  }
}
</style>
