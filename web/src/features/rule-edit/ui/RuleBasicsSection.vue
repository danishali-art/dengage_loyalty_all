<script setup lang="ts">
import { AppCard, AppCheckbox, AppFormField, AppInput, AppSelect } from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import type { RuleFormValues } from '@/entities/rule';
import type { RuleFormView } from '../model/useRuleForm';

/** What fires the rule and how it ranks against other rules on the same wallet. Presentational. */
const props = defineProps<{
  values: RuleFormValues;
  errors: Partial<Record<string, string>>;
  view: RuleFormView;
  isEdit: boolean;
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<RuleFormValues>> }>();
const set: FieldSetter<RuleFormValues> = (field, value) => emit('set', field, value);
</script>

<template>
  <AppCard :heading="$t('rules.form.basics')">
    <div class="flex flex-col gap-4">
      <p class="m-0 text-xs text-muted-foreground">{{ $t('rules.form.basicsIntro') }}</p>
      <div class="grid grid-cols-2 gap-3">
        <AppFormField :label="$t('rules.form.name')" for="name" :error="props.errors.name">
          <AppInput
            id="name"
            :model-value="props.values.name"
            :invalid="!!props.errors.name"
            :described-by="props.errors.name ? 'name-error' : undefined"
            @update:model-value="set('name', $event)"
          />
        </AppFormField>
        <AppFormField
          :label="$t('rules.form.priority')"
          for="priority"
          :error="props.errors.priority"
          :hint="$t('rules.priority.hint')"
        >
          <AppInput
            id="priority"
            inputmode="numeric"
            :model-value="props.values.priority"
            :invalid="!!props.errors.priority"
            :described-by="props.errors.priority ? 'priority-error' : 'priority-hint'"
            @update:model-value="set('priority', $event)"
          />
        </AppFormField>
      </div>
      <div class="grid grid-cols-2 gap-3">
        <AppFormField
          :label="$t('rules.form.trigger')"
          label-id="trigger-label"
          :error="props.errors.trigger"
          :hint="$t('rules.form.triggerHint')"
        >
          <AppSelect
            id="trigger"
            searchable
            labelled-by="trigger-label"
            :options="props.view.triggerOptions"
            :placeholder="$t('rules.form.selectTrigger')"
            :model-value="props.values.trigger"
            @update:model-value="$event && set('trigger', $event)"
          />
        </AppFormField>
        <AppFormField
          :label="$t('rules.form.type')"
          label-id="type-label"
          :hint="props.isEdit ? $t('rules.form.typeLocked') : null"
        >
          <AppSelect
            id="type"
            labelled-by="type-label"
            :options="props.view.typeOptions"
            :model-value="props.values.type"
            :disabled="props.isEdit"
            @update:model-value="$event && set('type', $event)"
          />
          <p v-if="props.view.noRuleType" class="m-0 text-xs text-(--app-danger-fg)" role="status">
            {{ $t('rules.type.none') }}
          </p>
          <p v-else-if="props.view.typeNote" class="m-0 text-xs text-muted-foreground">
            {{ props.view.typeNote }}
          </p>
        </AppFormField>
      </div>
      <!-- No rule type → no target to pick; showing the list here is what used to offer
           POINTS/CASH for reward.purchase. -->
      <template v-if="!props.view.noRuleType">
        <AppFormField
          v-if="props.view.needsTargetAccount"
          :label="$t('rules.form.target')"
          label-id="targetAccountTypeId-label"
          :error="props.errors.targetAccountTypeId"
        >
          <AppSelect
            id="targetAccountTypeId"
            searchable
            labelled-by="targetAccountTypeId-label"
            :options="props.view.targetAccountOptions"
            :placeholder="$t('rules.form.selectAccount')"
            :model-value="props.values.targetAccountTypeId"
            :invalid="!!props.errors.targetAccountTypeId"
            @update:model-value="set('targetAccountTypeId', $event ?? null)"
          />
        </AppFormField>
        <p v-else class="m-0 text-xs text-muted-foreground">{{ $t('rules.form.reversalTarget') }}</p>
      </template>
      <div class="grid grid-cols-2 gap-3">
        <AppFormField
          :label="$t('rules.form.activeFrom')"
          for="activeFrom"
          :hint="$t('rules.form.activeFromHint')"
        >
          <AppInput
            id="activeFrom"
            type="datetime-local"
            described-by="activeFrom-hint"
            :model-value="props.values.activeFrom"
            @update:model-value="set('activeFrom', $event)"
          />
        </AppFormField>
        <AppFormField :label="$t('rules.form.activeTo')" for="activeTo" :hint="$t('rules.form.activeToHint')">
          <AppInput
            id="activeTo"
            type="datetime-local"
            described-by="activeTo-hint"
            :model-value="props.values.activeTo"
            @update:model-value="set('activeTo', $event)"
          />
        </AppFormField>
      </div>
      <!-- 1.3.CL item 5: exclusivity groups and stack modes are retired — only "stackable" remains. -->
      <div class="border-t pt-4">
        <AppCheckbox
          id="stackable"
          described-by="stackable-desc"
          :model-value="props.values.stackable"
          @update:model-value="set('stackable', $event)"
        >
          {{ $t('rules.stackable.label') }}
        </AppCheckbox>
        <p id="stackable-desc" class="mt-1 mb-0 text-xs text-muted-foreground">
          {{ $t('rules.stackable.hint') }}
        </p>
      </div>
    </div>
  </AppCard>
</template>
