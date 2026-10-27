<script setup lang="ts">
import { AppButton, AppEmptyState, AppPageHeader, AppSkeletonList } from '@/shared/ui';
import { StreakCampaignForm } from '@/features/streak-campaign-edit';
import { useStreakCampaignEditPage } from '../model/useStreakCampaignEditPage';

const props = defineProps<{ programId: string; campaignId?: string }>();
const { crumbs, heading, cancel, form } = useStreakCampaignEditPage(props);
const { ready, loadFailed, options, timezones, values, errors, set, formErrors, pending, submit } = form;
</script>

<template>
  <AppPageHeader :heading="heading" :crumbs="crumbs">
    <template #actions>
      <AppButton variant="secondary" @click="cancel">{{ $t('action.cancel') }}</AppButton>
      <AppButton :pending="pending" :disabled="!ready" @click="submit">{{ $t('action.save') }}</AppButton>
    </template>
  </AppPageHeader>

  <div class="mx-auto max-w-5xl px-8 py-6">
    <AppEmptyState v-if="loadFailed" :heading="$t('streaks.form.notFound')" />
    <AppSkeletonList v-else-if="!ready" :count="4" />
    <StreakCampaignForm
      v-else
      :values="values"
      :errors="errors"
      :form-errors="formErrors"
      :options="options"
      :timezones="timezones"
      @set="set"
      @submit="submit"
    />
  </div>
</template>
