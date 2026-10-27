<script setup lang="ts">
const props = withDefaults(defineProps<{ heading: string; description?: string; icon?: string }>(), {
  description: undefined,
  icon: undefined,
});
</script>

<template>
  <div class="app-empty">
    <div v-if="props.icon" class="app-empty__icon" aria-hidden="true">{{ props.icon }}</div>
    <p class="app-empty__heading">{{ props.heading }}</p>
    <p v-if="props.description" class="app-empty__description">{{ props.description }}</p>
  </div>
</template>

<style scoped>
.app-empty {
  padding: 2rem 1rem;
  text-align: center;
}
.app-empty__icon {
  font-size: 1.75rem;
}
.app-empty__heading {
  margin: 0.5rem 0 0;
  font-weight: 500;
}
.app-empty__description {
  margin: 0.25rem 0 0;
  font-size: 0.8125rem;
  color: var(--app-muted);
}
</style>
