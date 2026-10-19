<script setup lang="ts">
import { AppButton, AppEmptyState, AppPageHeader, AppSkeletonList } from '@/shared/ui';
import { CardBucketForm } from '@/features/card-bucket-edit';
import { useCardBucketEditPage } from '../model/useCardBucketEditPage';

const props = defineProps<{ programId: string; bucketId?: string }>();
const { crumbs, heading, cancel, form } = useCardBucketEditPage(props);
const { ready, loadFailed, accountOptions, values, errors, set, formErrors, pending, submit } = form;
</script>

<template>
  <AppPageHeader :heading="heading" :crumbs="crumbs">
    <template #actions>
      <AppButton variant="secondary" @click="cancel">{{ $t('action.cancel') }}</AppButton>
      <AppButton :pending="pending" :disabled="!ready" @click="submit">{{ $t('action.save') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="mx-auto max-w-5xl px-8 py-6">
    <AppEmptyState v-if="loadFailed" :heading="$t('cardBuckets.form.notFound')" />
    <AppSkeletonList v-else-if="!ready" :count="4" />
    <CardBucketForm
      v-else
      :values="values"
      :errors="errors"
      :form-errors="formErrors"
      :account-options="accountOptions"
      @set="set"
      @submit="submit"
    />
  </div>
</template>
