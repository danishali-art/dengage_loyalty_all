<script setup lang="ts">
/** Presentational: links to the program's sub-resources. */
const props = defineProps<{ cards: readonly { key: string; icon: string; to: string }[] }>();
</script>

<template>
  <div class="summary-cards">
    <RouterLink v-for="card in props.cards" :key="card.key" :to="card.to" class="summary-card">
      <div class="summary-card__head">
        <span class="summary-card__icon" aria-hidden="true">{{ card.icon }}</span>
        <div>
          <div class="summary-card__label">{{ $t(`programs.overview.cards.${card.key}.label`) }}</div>
          <div class="summary-card__description">
            {{ $t(`programs.overview.cards.${card.key}.description`) }}
          </div>
        </div>
      </div>
      <div class="summary-card__manage">{{ $t('programs.overview.manage') }}</div>
    </RouterLink>
  </div>
</template>

<style scoped>
.summary-cards {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
  align-content: start;
}
.summary-card {
  display: block;
  padding: 1.25rem;
  background: white;
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  box-shadow: var(--app-shadow-card);
  color: var(--app-text);
  text-decoration: none;
  transition: border-color 0.15s;
}
.summary-card:hover {
  border-color: var(--app-brand);
  text-decoration: none;
}
.summary-card__head {
  display: flex;
  gap: 0.75rem;
}
.summary-card__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 2.25rem;
  height: 2.25rem;
  border-radius: 0.5rem;
  background: var(--app-brand-light);
  font-size: 1.125rem;
}
.summary-card__label {
  font-weight: 500;
}
.summary-card__description {
  margin-top: 0.125rem;
  font-size: 0.75rem;
  color: var(--app-muted);
}
.summary-card__manage {
  margin-top: 0.75rem;
  text-align: right;
  font-weight: 500;
  color: var(--app-brand);
}
</style>
