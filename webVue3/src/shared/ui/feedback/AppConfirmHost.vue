<script setup lang="ts">
import { computed } from 'vue';
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/shared/ui/primitives/alert-dialog';
import { Button } from '@/shared/ui/primitives/button';
import { pendingConfirm, settleConfirm } from './confirm';

/** Mounted once in App.vue; shows what `useAppConfirm().ask()` requested. Escape cancels. */
const options = computed(() => pendingConfirm.value?.options);
const open = computed({
  get: () => pendingConfirm.value !== null,
  set: (value) => {
    if (!value) settleConfirm(false);
  },
});
</script>

<template>
  <AlertDialog v-model:open="open">
    <AlertDialogContent v-if="options" class="sm:max-w-md">
      <AlertDialogHeader>
        <AlertDialogTitle>{{ options.title }}</AlertDialogTitle>
        <AlertDialogDescription>{{ options.message }}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel @click="settleConfirm(false)">{{ $t('action.cancel') }}</AlertDialogCancel>
        <!-- A plain button, not AlertDialogAction: that one closes the dialog first, and the close
             would settle the promise as cancelled before this click is seen. -->
        <Button :variant="options.destructive ? 'destructive' : 'default'" @click="settleConfirm(true)">
          {{ options.confirmLabel ?? $t('ui.confirm.ok') }}
        </Button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
