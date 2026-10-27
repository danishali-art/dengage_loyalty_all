<script setup lang="ts">
import { Alert, AlertDescription } from '@/shared/ui/primitives/alert';

/** Form-level errors (server messages that matched no field). Renders nothing when empty. */
const props = defineProps<{ messages: readonly string[] }>();
</script>

<template>
  <Alert
    v-if="props.messages.length"
    variant="destructive"
    class="border-(--app-danger-border) bg-(--app-danger-bg)"
  >
    <AlertDescription>
      <ul class="m-0 list-disc pl-4">
        <li v-for="message in props.messages" :key="message">{{ message }}</li>
      </ul>
    </AlertDescription>
  </Alert>
</template>
