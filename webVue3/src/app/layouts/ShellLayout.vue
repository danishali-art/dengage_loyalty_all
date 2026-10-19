<script setup lang="ts">
import { isLoading } from '@/shared/lib/loading/loading';
import { AppProgressBar } from '@/shared/ui';
import { AppSidebar } from '@/widgets/app-sidebar';
import { AppTopbar } from '@/widgets/app-topbar';
</script>

<template>
  <AppProgressBar :active="isLoading" />
  <a href="#main-content" class="shell__skip">{{ $t('ui.skipToContent') }}</a>
  <div class="shell">
    <AppSidebar />
    <div class="shell__column">
      <AppTopbar />
      <main id="main-content" class="shell__main" tabindex="-1">
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
.shell {
  display: flex;
  height: 100vh;
  overflow: hidden;
}
.shell__column {
  display: flex;
  flex: 1;
  flex-direction: column;
  overflow: hidden;
}
.shell__main {
  flex: 1;
  overflow-y: auto;
}
.shell__skip {
  position: absolute;
  left: -999px;
}
.shell__skip:focus {
  left: 0.5rem;
  top: 0.5rem;
  z-index: 70;
  padding: 0.5rem 0.75rem;
  border-radius: 0.5rem;
  background: white;
  box-shadow: var(--app-shadow-card);
}
</style>
