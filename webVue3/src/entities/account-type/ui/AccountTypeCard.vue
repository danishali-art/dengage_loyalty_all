<script setup lang="ts">
import { AppButton, AppStatusPill } from '@/shared/ui';
import { ACCOUNT_TYPE_ICON, type AccountType } from '../model/types';

/** One wallet in the account-types list. Presentational. */
const props = defineProps<{ accountType: AccountType }>();
const emit = defineEmits<{ edit: [accountType: AccountType] }>();
</script>

<template>
  <div class="account-type-card">
    <div class="account-type-card__main">
      <span class="account-type-card__icon" aria-hidden="true">{{
        ACCOUNT_TYPE_ICON[props.accountType.type]
      }}</span>
      <span class="account-type-card__name">{{ props.accountType.name }}</span>
      <AppStatusPill tone="neutral">{{ props.accountType.type }}</AppStatusPill>
      <AppStatusPill v-if="props.accountType.isTierQualifying" tone="info">
        {{ $t('accountTypes.tierQualifying.badge') }}
      </AppStatusPill>
    </div>
    <AppButton variant="text" size="sm" @click="emit('edit', props.accountType)">{{
      $t('action.edit')
    }}</AppButton>
  </div>
</template>

<style scoped>
.account-type-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem;
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  transition: border-color 0.15s;
}
.account-type-card:hover {
  border-color: var(--app-brand);
}
.account-type-card__main {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.account-type-card__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 2.25rem;
  height: 2.25rem;
  border-radius: 0.5rem;
  background: var(--app-brand-light);
  font-size: 1.125rem;
}
.account-type-card__name {
  font-weight: 500;
}
</style>
