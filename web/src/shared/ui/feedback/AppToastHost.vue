<script setup lang="ts">
import { onBeforeUnmount } from 'vue';
import { toast } from 'vue-sonner';
// vue-sonner 2 ships its toast layout as a separate stylesheet; without it toasts are bare text.
import 'vue-sonner/style.css';
import { Toaster } from '@/shared/ui/primitives/sonner';
import { onToast, type ToastKind } from './toast';

/** Mounted once in App.vue. Errors stay until dismissed. */
const TTL: Record<ToastKind, number> = {
  success: 4000,
  info: 5000,
  warning: 7000,
  error: Infinity,
};

const stop = onToast((t) =>
  toast[t.kind](t.title, {
    description: [t.detail, t.traceId].filter(Boolean).join(' · ') || undefined,
    duration: TTL[t.kind],
    closeButton: t.kind === 'error',
  }),
);
onBeforeUnmount(stop);
</script>

<template>
  <!-- Below the 3.5rem topbar, so a toast never covers the tenant switcher or the user menu. -->
  <Toaster
    position="top-right"
    rich-colors
    :offset="{ top: '4.25rem', right: '1.25rem' }"
    :close-button-aria-label="$t('ui.toast.dismiss')"
  />
</template>
