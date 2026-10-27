<script setup lang="ts">
import { useSidebarNav } from '../model/useSidebarNav';

const { items, programSubNav, tenantSubNav } = useSidebarNav();
</script>

<template>
  <aside class="sidebar">
    <RouterLink to="/programs" class="sidebar__brand" :aria-label="$t('app.home')">
      <span class="sidebar__logo" aria-hidden="true">L·</span>
      <span class="sidebar__brand-name">{{ $t('app.title') }}</span>
    </RouterLink>
    <nav class="sidebar__nav" :aria-label="$t('nav.primary')">
      <template v-for="item in items" :key="item.to">
        <RouterLink :to="item.to" class="sidebar__link" active-class="sidebar__link--active">
          <span class="sidebar__icon" aria-hidden="true">{{ item.icon }}</span>
          {{ $t(item.labelKey) }}
        </RouterLink>
        <!-- Contextual sub-nav: which program's account types / tiers / rules, once inside one. -->
        <div
          v-if="item.to === '/programs' && programSubNav"
          class="sidebar__sub"
          :title="programSubNav.programName"
        >
          <RouterLink
            v-for="sub in programSubNav.items"
            :key="sub.to"
            :to="sub.to"
            class="sidebar__sublink"
            exact-active-class="sidebar__sublink--active"
          >
            {{ $t(sub.labelKey) }}
          </RouterLink>
        </div>
        <div
          v-if="item.to === '/platform' && tenantSubNav"
          class="sidebar__sub"
          :title="tenantSubNav.tenantName"
        >
          <RouterLink
            v-for="sub in tenantSubNav.items"
            :key="sub.to"
            :to="sub.to"
            class="sidebar__sublink"
            exact-active-class="sidebar__sublink--active"
          >
            {{ $t(sub.labelKey) }}
          </RouterLink>
        </div>
      </template>
    </nav>
  </aside>
</template>

<style scoped>
.sidebar {
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  width: 15rem;
  height: 100vh;
  background: white;
  border-right: 1px solid var(--app-border);
}
.sidebar__brand {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 1rem 1.25rem;
  text-decoration: none;
}
.sidebar__logo {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  border-radius: 0.5rem;
  background: var(--app-brand);
  color: white;
  font-weight: 700;
}
.sidebar__brand-name {
  font-weight: 700;
  color: var(--app-text);
}
.sidebar__nav {
  flex: 1;
  overflow-y: auto;
  padding: 0 0.75rem 1rem;
}
.sidebar__link {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.5rem 0.75rem;
  border-radius: 0.5rem;
  font-weight: 500;
  color: var(--app-text-soft);
  text-decoration: none;
}
.sidebar__link:hover {
  background: #f3f4f6;
  text-decoration: none;
}
.sidebar__link--active {
  background: var(--app-brand-light);
  color: var(--app-brand);
}
.sidebar__icon {
  width: 1.25rem;
  text-align: center;
}
.sidebar__sub {
  display: flex;
  flex-direction: column;
  margin: 0.125rem 0 0 1rem;
  padding-left: 0.75rem;
  border-left: 1px solid var(--app-border);
}
.sidebar__sublink {
  padding: 0.375rem 0.5rem;
  border-radius: 0.375rem;
  font-size: 0.75rem;
  color: var(--app-muted);
  text-decoration: none;
}
.sidebar__sublink:hover {
  background: #f3f4f6;
  text-decoration: none;
}
.sidebar__sublink--active {
  color: var(--app-brand);
  font-weight: 500;
}
</style>
