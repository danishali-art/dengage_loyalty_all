<script setup lang="ts">
/** White card. With `heading`, a tinted header band with the accent bar. */
const props = withDefaults(defineProps<{ heading?: string; flush?: boolean }>(), {
  heading: undefined,
  flush: false,
});
</script>

<template>
  <section class="app-card" :class="{ 'app-card--flush': props.flush }">
    <div v-if="props.heading" class="app-card__header">
      <h2 class="app-card__heading">{{ props.heading }}</h2>
    </div>
    <slot />
  </section>
</template>

<style scoped>
.app-card {
  background: white;
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  box-shadow: var(--app-shadow-card);
  padding: 1.25rem;
}
.app-card--flush {
  padding: 0;
  overflow: hidden;
}
.app-card__header {
  position: relative;
  margin: -1.25rem -1.25rem 1rem;
  padding: 1rem 1.25rem 1rem 2rem;
  background: var(--app-brand-light);
  border-radius: var(--app-radius) var(--app-radius) 0 0;
}
.app-card__header::before {
  content: '';
  position: absolute;
  left: 1.25rem;
  top: 1rem;
  bottom: 1rem;
  width: 3px;
  border-radius: 2px;
  background: var(--app-brand);
}
.app-card__heading {
  margin: 0;
  font-size: 0.9375rem;
  font-weight: 600;
  color: var(--app-heading);
}
</style>
