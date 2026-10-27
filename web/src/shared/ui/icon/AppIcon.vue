<script setup lang="ts">
import {
  ArrowDown,
  Banknote,
  CircleCheck,
  Copy,
  Eye,
  Flame,
  ArrowUp,
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Info,
  LoaderCircle,
  Pause,
  Play,
  Plus,
  Trash2,
  TriangleAlert,
  Trophy,
  Star,
  User,
  X,
} from '@lucide/vue';
import { useAttrs } from 'vue';
import { cn } from '@/shared/ui/lib/utils';
import type { IconName } from './types';

/** An icon by name. The icon set stays inside shared/ui, so pages never import it. Decorative only. */
const props = withDefaults(defineProps<{ name: IconName; spin?: boolean }>(), { spin: false });
// A caller's class (e.g. `size-8`) is merged, not appended: two size utilities would leave the
// winner to stylesheet order.
defineOptions({ inheritAttrs: false });
const attrs = useAttrs();

const ICONS = {
  'arrow-up': ArrowUp,
  'arrow-down': ArrowDown,
  check: Check,
  'chevron-down': ChevronDown,
  'chevron-left': ChevronLeft,
  'chevron-right': ChevronRight,
  info: Info,
  loader: LoaderCircle,
  close: X,
  pause: Pause,
  play: Play,
  plus: Plus,
  trash: Trash2,
  user: User,
  flame: Flame,
  trophy: Trophy,
  star: Star,
  cash: Banknote,
  stamp: CircleCheck,
  warning: TriangleAlert,
  eye: Eye,
  copy: Copy,
} as const satisfies Record<IconName, unknown>;
</script>

<template>
  <component
    :is="ICONS[props.name]"
    :class="cn('size-4 shrink-0', props.spin && 'animate-spin', attrs.class as string)"
    aria-hidden="true"
  />
</template>
