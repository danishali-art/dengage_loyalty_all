<script setup lang="ts" generic="T extends string">
import { Tabs, TabsList, TabsTrigger } from '@/shared/ui/primitives/tabs';

/**
 * A row of tabs under a page header (the Customer 360 tabs). Only the tab list: the page renders
 * the selected tab's content itself, below the header. `v-model` the selected tab.
 */
const model = defineModel<T>({ required: true });
const props = defineProps<{ tabs: readonly { value: T; label: string }[]; label: string }>();
</script>

<template>
  <Tabs :model-value="model" @update:model-value="model = $event as T">
    <TabsList
      :aria-label="props.label"
      class="-mb-px h-auto w-full justify-start gap-1 overflow-x-auto rounded-none bg-transparent p-0"
    >
      <TabsTrigger
        v-for="tab in props.tabs"
        :key="tab.value"
        :value="tab.value"
        class="h-auto flex-none rounded-none border-0 border-b-2 border-transparent px-4 pt-1 pb-3 font-normal text-muted-foreground shadow-none hover:text-foreground data-[state=active]:border-primary data-[state=active]:font-medium data-[state=active]:text-primary data-[state=active]:shadow-none"
      >
        {{ tab.label }}
      </TabsTrigger>
    </TabsList>
  </Tabs>
</template>
