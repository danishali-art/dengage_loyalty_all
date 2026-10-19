<script setup lang="ts">
import type { Crumb } from './types';

/**
 * Page title bar with optional breadcrumbs, a subtitle and an `#actions` slot. The `#below` slot
 * holds what sits under the title inside the bar, such as a page's tabs.
 */
const props = withDefaults(defineProps<{ heading: string; crumbs?: readonly Crumb[]; subtitle?: string }>(), {
  crumbs: () => [],
  subtitle: undefined,
});
</script>

<template>
  <header class="app-page-header" :class="{ 'app-page-header--below': $slots.below }">
    <div class="app-page-header__row">
      <div>
        <nav v-if="props.crumbs.length" :aria-label="$t('ui.breadcrumbs')" class="app-page-header__crumbs">
          <template v-for="(crumb, i) in props.crumbs" :key="i">
            <span v-if="i > 0" aria-hidden="true">›</span>
            <RouterLink v-if="crumb.to" :to="crumb.to">{{ crumb.label }}</RouterLink>
            <span v-else aria-current="page">{{ crumb.label }}</span>
          </template>
        </nav>
        <h1 class="app-page-header__heading">{{ props.heading }}</h1>
        <p v-if="props.subtitle" class="app-page-header__subtitle">{{ props.subtitle }}</p>
      </div>
      <div class="app-page-header__actions">
        <slot name="actions" />
      </div>
    </div>
    <div v-if="$slots.below" class="app-page-header__below">
      <slot name="below" />
    </div>
  </header>
</template>

<style scoped>
.app-page-header {
  padding: 1.25rem 2rem;
  background: white;
  border-bottom: 1px solid var(--app-border);
}
.app-page-header--below {
  padding-bottom: 0;
}
.app-page-header__row {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 1rem;
}
.app-page-header__crumbs {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.25rem;
  font-size: 0.8125rem;
  color: var(--app-muted);
}
.app-page-header__heading {
  margin: 0;
  font-size: 1.125rem;
  font-weight: 700;
}
.app-page-header__subtitle {
  margin: 0.25rem 0 0;
  font-size: 0.8125rem;
  color: var(--app-muted);
}
.app-page-header__actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.app-page-header__below {
  margin-top: 1rem;
}
</style>
