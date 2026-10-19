<script setup lang="ts">
import { AppButton, AppEmptyState, AppPageHeader, AppSkeletonList } from '@/shared/ui';
import { RuleForm } from '@/features/rule-edit';
import { useRuleEditPage } from '../model/useRuleEditPage';

const props = defineProps<{ programId: string; ruleId?: string }>();
const { crumbs, heading, cancel, form } = useRuleEditPage(props);
const { isEdit, ready, loadFailed, view, values, errors, set, formErrors, pending, submit } = form;
</script>

<template>
  <AppPageHeader :heading="heading" :crumbs="crumbs">
    <template #actions>
      <AppButton variant="secondary" @click="cancel">{{ $t('action.cancel') }}</AppButton>
      <AppButton :pending="pending" :disabled="!ready || view.noRuleType" @click="submit">
        {{ $t('action.save') }}
      </AppButton>
    </template>
  </AppPageHeader>

  <div class="mx-auto max-w-5xl px-8 py-6">
    <AppEmptyState v-if="loadFailed" :heading="$t('rules.form.notFound')" />
    <AppSkeletonList v-else-if="!ready" :count="5" />
    <RuleForm
      v-else
      :values="values"
      :errors="errors"
      :form-errors="formErrors"
      :view="view"
      :is-edit="isEdit"
      @set="set"
      @submit="submit"
    />
  </div>
</template>
