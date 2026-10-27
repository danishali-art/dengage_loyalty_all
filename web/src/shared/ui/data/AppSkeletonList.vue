<script setup lang="ts">
import { Skeleton } from '@/shared/ui/primitives/skeleton';

const props = withDefaults(defineProps<{ count?: number }>(), { count: 3 });
</script>

<template>
  <div class="flex flex-col gap-2" aria-busy="true">
    <Skeleton v-for="i in props.count" :key="i" class="h-12 rounded-xl bg-gray-200/70" />
  </div>
</template>
