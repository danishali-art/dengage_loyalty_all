<script setup lang="ts">
/** 403 / 404. */
const props = defineProps<{ kind: 'forbidden' | 'notFound' }>();
const CODE = { forbidden: '403', notFound: '404' } as const;
</script>

<template>
  <main class="status-page">
    <div class="status-page__code" aria-hidden="true">{{ CODE[props.kind] }}</div>
    <h1 class="status-page__title">{{ $t(`${props.kind}.title`) }}</h1>
    <p class="status-page__body">{{ $t(`${props.kind}.body`) }}</p>
    <RouterLink to="/programs" class="status-page__link">{{ $t('nav.programs') }}</RouterLink>
  </main>
</template>

<style scoped>
.status-page {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 60vh;
  padding: 2rem 1.5rem;
  text-align: center;
}
.status-page__code {
  font-size: 3rem;
  font-weight: 700;
  color: var(--app-muted);
}
.status-page__title {
  margin: 0.75rem 0 0;
  font-size: 1.125rem;
  font-weight: 700;
}
.status-page__body {
  max-width: 24rem;
  margin: 0.25rem 0 0;
  color: var(--app-muted);
}
.status-page__link {
  margin-top: 1.25rem;
  padding: 0.5rem 1rem;
  border-radius: 0.5rem;
  background: var(--app-brand);
  color: white;
  font-weight: 500;
}
.status-page__link:hover {
  background: var(--app-brand-dark);
  text-decoration: none;
}
</style>
