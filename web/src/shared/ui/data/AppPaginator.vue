<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import AppButton from '../button/AppButton.vue';
import AppIcon from '../icon/AppIcon.vue';
import AppSelect from '../form/AppSelect.vue';

/**
 * Offset paginator: "1–25 of 120", Prev / Page x of y / Next. With
 * `pageSizeOptions`, a "page view" choice too, reported through `update:pageSize`.
 */
const props = withDefaults(
  defineProps<{ page: number; pageSize: number; total: number; pageSizeOptions?: readonly number[] }>(),
  { pageSizeOptions: () => [] },
);
const emit = defineEmits<{ 'update:page': [page: number]; 'update:pageSize': [size: number] }>();
const { t } = useI18n();

const lastPage = computed(() => Math.max(1, Math.ceil(props.total / Math.max(1, props.pageSize))));
const rangeLabel = computed(() => {
  if (props.total === 0) return t('ui.paginator.none');
  const from = (props.page - 1) * props.pageSize + 1;
  const to = Math.min(props.total, props.page * props.pageSize);
  return t('ui.paginator.range', { from, to, total: props.total });
});
const pageLabel = computed(() => t('ui.paginator.page', { page: props.page, pages: lastPage.value }));
const sizeOptions = computed(() => props.pageSizeOptions.map((s) => ({ value: s, label: String(s) })));

function changeSize(size: number | null | undefined): void {
  if (size && size !== props.pageSize) emit('update:pageSize', size);
}
</script>

<template>
  <div class="flex items-center justify-between gap-4 border-t px-4 py-2 text-muted-foreground">
    <div class="flex items-center gap-4">
      <span aria-live="polite">{{ rangeLabel }}</span>
      <div v-if="sizeOptions.length" class="flex items-center gap-2 text-xs">
        <span>{{ $t('ui.paginator.pageView') }}</span>
        <div class="w-20">
          <AppSelect
            :model-value="props.pageSize"
            :options="sizeOptions"
            :aria-label="$t('ui.paginator.pageView')"
            @update:model-value="changeSize"
          />
        </div>
      </div>
    </div>
    <div class="flex items-center gap-2">
      <AppButton
        variant="text"
        size="sm"
        :aria-label="$t('ui.paginator.previous')"
        :disabled="props.page <= 1"
        @click="emit('update:page', props.page - 1)"
      >
        <AppIcon name="chevron-left" />
      </AppButton>
      <span>{{ pageLabel }}</span>
      <AppButton
        variant="text"
        size="sm"
        :aria-label="$t('ui.paginator.next')"
        :disabled="props.page >= lastPage"
        @click="emit('update:page', props.page + 1)"
      >
        <AppIcon name="chevron-right" />
      </AppButton>
    </div>
  </div>
</template>
