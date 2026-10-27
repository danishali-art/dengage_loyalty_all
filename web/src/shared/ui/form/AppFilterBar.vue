<script setup lang="ts">
import AppButton from '../button/AppButton.vue';

/**
 * One row of list filters with Search / Reset. The page
 * owns the filter values and puts its fields in the slot; this lays them out and reports the
 * buttons. Enter in a field counts as Search.
 */
const props = withDefaults(defineProps<{ label: string; busy?: boolean }>(), { busy: false });
const emit = defineEmits<{ search: []; reset: [] }>();
</script>

<template>
  <form
    class="flex flex-wrap items-end gap-3 2xl:flex-nowrap"
    role="search"
    :aria-label="props.label"
    @submit.prevent="emit('search')"
  >
    <slot />
    <div class="flex shrink-0 gap-2">
      <AppButton type="submit" :pending="props.busy">{{ $t('ui.filter.search') }}</AppButton>
      <AppButton variant="secondary" @click="emit('reset')">{{ $t('ui.filter.reset') }}</AppButton>
    </div>
  </form>
</template>
