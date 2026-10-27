import { computed, ref } from 'vue';

/** Ref-counted in-flight request tracker. Drives the shell's progress bar. */
const inFlight = ref(0);

export const isLoading = computed(() => inFlight.value > 0);

export function beginLoading(): void {
  inFlight.value++;
}

export function endLoading(): void {
  inFlight.value = Math.max(0, inFlight.value - 1);
}
