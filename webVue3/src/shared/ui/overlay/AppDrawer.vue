<script setup lang="ts">
import { Dialog, DialogClose, DialogContent, DialogTitle } from '@/shared/ui/primitives/dialog';
import AppIcon from '../icon/AppIcon.vue';

/**
 * A panel docked to the right edge (port of the Angular `app-side-panel`): titled header with a
 * close button and a scrollable body. Open/close with `v-model:open`. Built on the dialog
 * primitive, so focus trap, Escape and `role="dialog"` come with it.
 */
const open = defineModel<boolean>('open', { default: false });
const props = withDefaults(defineProps<{ heading: string; subheading?: string; busy?: boolean }>(), {
  subheading: '',
  busy: false,
});
</script>

<template>
  <Dialog v-model:open="open">
    <!-- No description element: the title names the panel. -->
    <DialogContent
      :show-close-button="false"
      :aria-describedby="undefined"
      class="top-0 right-0 left-auto flex h-screen w-[40rem] max-w-[100vw] translate-x-0 translate-y-0 flex-col gap-0 overflow-hidden rounded-none border-0 p-0 sm:max-w-[100vw] data-[state=closed]:zoom-out-100 data-[state=open]:zoom-in-100"
    >
      <header class="flex items-start justify-between gap-3 border-b bg-(--app-brand-light) px-5 py-4">
        <div class="min-w-0">
          <DialogTitle class="text-sm font-bold text-(--app-heading)">{{ props.heading }}</DialogTitle>
          <p v-if="props.subheading" class="mt-0.5 mb-0 truncate font-mono text-xs text-muted-foreground">
            {{ props.subheading }}
          </p>
        </div>
        <DialogClose
          class="rounded-sm p-1 text-muted-foreground hover:bg-white/60 focus-visible:ring-2 focus-visible:ring-ring"
          :aria-label="$t('action.close')"
        >
          <AppIcon name="close" />
        </DialogClose>
      </header>
      <div class="flex-1 overflow-y-auto px-5 py-4" :aria-busy="props.busy || undefined">
        <slot />
      </div>
    </DialogContent>
  </Dialog>
</template>
