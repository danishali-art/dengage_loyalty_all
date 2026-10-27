<script setup lang="ts">
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/shared/ui/primitives/dropdown-menu';
import type { PopupMenuItem } from './types';

/** A menu opened by the button in the `#trigger` slot, anchored below it. */
const props = defineProps<{ items: readonly PopupMenuItem[] }>();
defineSlots<{ trigger(): unknown }>();
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <slot name="trigger" />
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="min-w-48">
      <template v-for="(item, i) in props.items" :key="i">
        <DropdownMenuLabel v-if="item.info" class="font-normal text-muted-foreground">
          {{ item.label }}
        </DropdownMenuLabel>
        <DropdownMenuItem v-else @select="item.action?.()">{{ item.label }}</DropdownMenuItem>
      </template>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
