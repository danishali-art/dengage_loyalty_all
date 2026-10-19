<script setup lang="ts">
import { AppConfirmHost, AppToastHost } from '@/shared/ui';
</script>

<template>
  <RouterView />
  <AppToastHost />
  <AppConfirmHost />
</template>
