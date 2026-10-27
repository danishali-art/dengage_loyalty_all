<script setup lang="ts">
import { computed } from 'vue';
import { AppButton, AppCard } from '@/shared/ui';
import { emptyLeaf, type ConditionLeaf } from '@/shared/lib/dsl';
import { fieldKinds, type KnownField } from '../model/fields';
import ConditionLeafRow from './ConditionLeafRow.vue';

/**
 * Flat-list sibling of `ConditionTreeEditor`: a bare
 * `ConditionLeaf[]`, implicitly AND-joined. Card buckets' `additionalConditions` is the one
 * caller — the backend (`CardBucketConditionMapper.ToConditions`) folds it into a single AND
 * group with its own structured leaves, so offering OR-grouping here would promise something the
 * API can't represent.
 */
const leaves = defineModel<ConditionLeaf[]>({ required: true });
const props = withDefaults(defineProps<{ knownFields?: readonly KnownField[] }>(), {
  knownFields: () => [],
});
const kinds = computed(() => fieldKinds(props.knownFields));

const setLeaf = (li: number, leaf: ConditionLeaf): void => {
  leaves.value = leaves.value.map((c, i) => (i === li ? leaf : c));
};
</script>

<template>
  <AppCard :heading="$t('conditions.leaves.heading')">
    <div class="flex flex-col gap-3">
      <p class="m-0 text-xs text-muted-foreground">{{ $t('conditions.leaves.intro') }}</p>
      <ConditionLeafRow
        v-for="(leaf, li) in leaves"
        :key="li"
        :leaf="leaf"
        :kinds="kinds"
        :id-prefix="`extra-condition-${li}`"
        @change="setLeaf(li, $event)"
        @remove="leaves = leaves.filter((_, i) => i !== li)"
      />
      <div>
        <AppButton size="sm" variant="secondary" @click="leaves = [...leaves, emptyLeaf()]">
          {{ $t('conditions.add') }}
        </AppButton>
      </div>
    </div>
  </AppCard>
</template>
