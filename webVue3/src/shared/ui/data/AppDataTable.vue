<script setup lang="ts" generic="T extends object">
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/shared/ui/primitives/table';
import type { TableColumn } from './types';

/**
 * Table with a caption, a loading state and an empty message. Custom cells go in a
 * `#cell-<key>="{ row }"` slot; without one the cell shows `row[key]`.
 */
const props = withDefaults(
  defineProps<{
    rows: readonly T[];
    columns: readonly TableColumn[];
    rowKey: string;
    caption: string;
    loading?: boolean;
    emptyText: string;
    /** Rows open something on click (pointer cursor + rowClick). */
    clickableRows?: boolean;
  }>(),
  { loading: false, clickableRows: false },
);

const emit = defineEmits<{ rowClick: [row: T] }>();

defineSlots<Record<`cell-${string}`, (props: { row: T }) => unknown>>();

function cell(row: T, key: string): unknown {
  return (row as Record<string, unknown>)[key];
}

function onRowClick(row: T): void {
  if (props.clickableRows) emit('rowClick', row);
}
</script>

<template>
  <Table :aria-label="props.caption" :aria-busy="props.loading || undefined">
    <TableHeader class="bg-gray-50">
      <TableRow>
        <TableHead
          v-for="column in props.columns"
          :key="column.key"
          class="px-4 text-xs font-semibold tracking-wide text-(--app-text-soft) uppercase"
          :class="{ 'text-right': column.align === 'right' }"
        >
          {{ column.header }}
        </TableHead>
      </TableRow>
    </TableHeader>
    <TableBody :class="{ 'opacity-50 transition-opacity': props.loading }">
      <TableRow
        v-for="row in props.rows"
        :key="String(cell(row, props.rowKey))"
        :class="{ 'cursor-pointer': props.clickableRows }"
        @click="onRowClick(row)"
      >
        <TableCell
          v-for="column in props.columns"
          :key="column.key"
          class="px-4 py-3 whitespace-normal"
          :class="{ 'text-right': column.align === 'right' }"
        >
          <slot :name="`cell-${column.key}`" :row="row">{{ cell(row, column.key) }}</slot>
        </TableCell>
      </TableRow>
      <TableRow v-if="!props.rows.length" class="hover:bg-transparent">
        <TableCell :colspan="props.columns.length" class="py-6 text-center text-muted-foreground">
          {{ props.emptyText }}
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
