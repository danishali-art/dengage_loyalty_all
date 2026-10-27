<script setup lang="ts">
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/shared/ui/primitives/dialog';
import AppIcon from '../icon/AppIcon.vue';

/** Modal dialog. Open/close with `v-model:visible`; actions go in the `footer` slot. */
const visible = defineModel<boolean>('visible', { default: false });
const props = withDefaults(defineProps<{ header: string; width?: string }>(), { width: '32rem' });
</script>

<template>
  <Dialog v-model:open="visible">
    <!-- No description element: the title names the dialog, the body is a form. -->
    <DialogContent
      :show-close-button="false"
      :aria-describedby="undefined"
      class="max-h-[90vh] grid-rows-[auto_minmax(0,1fr)_auto] sm:max-w-none"
      :style="{ width: `min(${props.width}, calc(100vw - 2rem))` }"
    >
      <DialogHeader class="flex-row items-center justify-between gap-4">
        <DialogTitle>{{ props.header }}</DialogTitle>
        <DialogClose
          class="rounded-sm p-1 text-muted-foreground hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
          :aria-label="$t('action.close')"
        >
          <AppIcon name="close" />
        </DialogClose>
      </DialogHeader>
      <div class="-mx-6 overflow-y-auto px-6">
        <slot />
      </div>
      <DialogFooter v-if="$slots.footer">
        <slot name="footer" />
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
