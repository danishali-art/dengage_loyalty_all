<script setup lang="ts" generic="T extends string | number">
import { computed, ref } from 'vue';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from '@/shared/ui/primitives/select';
import {
  Combobox,
  ComboboxAnchor,
  ComboboxEmpty,
  ComboboxGroup,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
  ComboboxTrigger,
  ComboboxViewport,
} from '@/shared/ui/primitives/combobox';
import AppIcon from '../icon/AppIcon.vue';
import type { SelectOption } from './types';

/**
 * Single select. `searchable` adds a filter box; `clearable`
 * lets the admin go back to "nothing chosen" (`null`). Options with a `group` are shown under
 * that heading, grouped by adjacency (sort them by group first).
 */
const model = defineModel<T | null | undefined>();

const props = withDefaults(
  defineProps<{
    id?: string;
    options: readonly SelectOption<T>[];
    placeholder?: string;
    invalid?: boolean;
    disabled?: boolean;
    describedBy?: string;
    labelledBy?: string;
    /** For a select without a visible label (e.g. a condition row's operator). */
    ariaLabel?: string;
    searchable?: boolean;
    clearable?: boolean;
  }>(),
  {
    id: undefined,
    placeholder: undefined,
    invalid: false,
    disabled: false,
    describedBy: undefined,
    labelledBy: undefined,
    ariaLabel: undefined,
    searchable: false,
    clearable: false,
  },
);

// Items are keyed by their value, prefixed: the primitives reject '' as an item value, and keys
// stay strings whatever T is. Not by index — when the options change (a rule's type list follows
// its trigger) the same index would name another option and the trigger would keep the old label.
const keyOf = (value: T): string => `v:${String(value)}`;
const key = computed<string | undefined>({
  get: () => {
    const option = props.options.find((o) => o.value === model.value);
    return option ? keyOf(option.value) : undefined;
  },
  set: (k) => {
    model.value = k === undefined ? null : (props.options.find((o) => keyOf(o.value) === k)?.value ?? null);
  },
});
const selectedLabel = computed(() => props.options.find((o) => o.value === model.value)?.label);

interface Section {
  heading?: string;
  items: { option: SelectOption<T>; key: string }[];
}
const sections = computed<Section[]>(() => {
  const result: Section[] = [];
  props.options.forEach((option) => {
    const item = { option, key: keyOf(option.value) };
    const last = result[result.length - 1];
    if (last && last.heading === option.group) last.items.push(item);
    else result.push({ heading: option.group, items: [item] });
  });
  return result;
});

const searchOpen = ref(false);
const triggerClass =
  'w-full bg-white aria-invalid:border-destructive aria-invalid:ring-destructive/20 data-[placeholder]:text-muted-foreground';
</script>

<template>
  <div class="relative w-full">
    <Combobox
      v-if="props.searchable"
      v-model="key"
      v-model:open="searchOpen"
      :disabled="props.disabled"
      :reset-search-term-on-select="true"
    >
      <ComboboxAnchor as-child>
        <ComboboxTrigger
          :id="props.id"
          :class="[
            triggerClass,
            'flex h-9 items-center justify-between gap-2 rounded-md border border-input px-3 text-left text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50',
            { 'pr-9': props.clearable && key !== undefined },
          ]"
          :aria-label="props.ariaLabel"
          :aria-labelledby="props.labelledBy"
          :aria-describedby="props.describedBy"
          :aria-invalid="props.invalid || undefined"
          :data-placeholder="key === undefined ? '' : undefined"
        >
          <span class="truncate" :class="{ 'text-muted-foreground': key === undefined }">{{
            selectedLabel ?? props.placeholder
          }}</span>
          <AppIcon name="chevron-down" class="opacity-50" />
        </ComboboxTrigger>
      </ComboboxAnchor>
      <ComboboxList class="w-(--reka-combobox-trigger-width) min-w-48" align="start">
        <!-- The search box starts empty: by default it would show the chosen item's key. -->
        <ComboboxInput :aria-label="$t('ui.select.search')" class="h-9" :display-value="() => ''" />
        <ComboboxEmpty class="py-4 text-center text-sm text-muted-foreground">{{
          $t('ui.select.noResults')
        }}</ComboboxEmpty>
        <ComboboxViewport class="max-h-64 p-1">
          <ComboboxGroup v-for="(section, s) in sections" :key="s" :heading="section.heading">
            <ComboboxItem
              v-for="item in section.items"
              :key="item.key"
              :value="item.key"
              :text-value="item.option.label"
            >
              <span class="flex-1 truncate">{{ item.option.label }}</span>
              <AppIcon v-if="key === item.key" name="check" />
            </ComboboxItem>
          </ComboboxGroup>
        </ComboboxViewport>
      </ComboboxList>
    </Combobox>

    <Select v-else v-model="key" :disabled="props.disabled">
      <SelectTrigger
        :id="props.id"
        :class="[triggerClass, { 'pr-9': props.clearable && key !== undefined }]"
        :aria-label="props.ariaLabel"
        :aria-labelledby="props.labelledBy"
        :aria-describedby="props.describedBy"
        :aria-invalid="props.invalid || undefined"
      >
        <SelectValue :placeholder="props.placeholder" />
      </SelectTrigger>
      <SelectContent>
        <SelectGroup v-for="(section, s) in sections" :key="s">
          <SelectLabel v-if="section.heading">{{ section.heading }}</SelectLabel>
          <SelectItem v-for="item in section.items" :key="item.key" :value="item.key">
            {{ item.option.label }}
          </SelectItem>
        </SelectGroup>
      </SelectContent>
    </Select>

    <button
      v-if="props.clearable && key !== undefined && !props.disabled"
      type="button"
      class="absolute top-1/2 right-8 -translate-y-1/2 rounded-sm p-0.5 text-muted-foreground hover:text-foreground"
      :aria-label="$t('ui.select.clear')"
      @click="key = undefined"
    >
      <AppIcon name="close" class="size-3.5" />
    </button>
  </div>
</template>
