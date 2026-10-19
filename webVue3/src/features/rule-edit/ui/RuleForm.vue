<script setup lang="ts">
import { AppFormErrors } from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import { ConditionTreeEditor } from '@/entities/condition';
import type { RuleFormValues } from '@/entities/rule';
import type { RuleFormView } from '../model/useRuleForm';
import RuleBasicsSection from './RuleBasicsSection.vue';
import RuleCalculationSection from './RuleCalculationSection.vue';
import RuleLimitsSection from './RuleLimitsSection.vue';
import RuleConfigurationSection from './RuleConfigurationSection.vue';

/** The rule form (port of rule-form.page.ts's template). Presentational: emits `set(field, value)`. */
const props = defineProps<{
  values: RuleFormValues;
  errors: Partial<Record<string, string>>;
  formErrors: readonly string[];
  view: RuleFormView;
  isEdit: boolean;
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<RuleFormValues>>; submit: [] }>();
const set: FieldSetter<RuleFormValues> = (field, value) => emit('set', field, value);
</script>

<template>
  <form class="flex flex-col gap-6" novalidate @submit.prevent="emit('submit')">
    <AppFormErrors :messages="props.formErrors" />
    <RuleBasicsSection
      :values="props.values"
      :errors="props.errors"
      :view="props.view"
      :is-edit="props.isEdit"
      @set="set"
    />
    <RuleCalculationSection
      :values="props.values"
      :errors="props.errors"
      :view="props.view"
      :is-edit="props.isEdit"
      @set="set"
    />
    <RuleLimitsSection :values="props.values" :errors="props.errors" :view="props.view" @set="set" />
    <RuleConfigurationSection :values="props.values" :errors="props.errors" :view="props.view" @set="set" />
    <ConditionTreeEditor
      :model-value="props.values.conditions"
      :known-fields="props.view.knownFields"
      @update:model-value="set('conditions', $event)"
    />
  </form>
</template>
