<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import type { RouteLocationRaw } from 'vue-router';
import { AppCard, AppFormField, AppInput, AppSelect, AppSwitch, AppTextarea } from '@/shared/ui';
import { ROUNDING_DIRECTIONS, type RoundingDirection } from '@/entities/program';

/**
 * Presentational "Program information" card. The Active switch is emitted, not bound: it saves
 * on its own (1.3.CL item 7), separately from the form's Save.
 */
const name = defineModel<string | undefined>('name', { required: true });
const slug = defineModel<string | undefined>('slug', { required: true });
const description = defineModel<string | undefined>('description', { required: true });
const defaultRounding = defineModel<RoundingDirection | undefined>('defaultRounding', { required: true });
const props = defineProps<{
  errors: Partial<Record<'name' | 'slug' | 'description' | 'defaultRounding', string>>;
  isDraft: boolean;
  running: boolean;
  togglingStatus: boolean;
  qualifyingAccountName: string | null;
  accountTypesLink: RouteLocationRaw;
}>();
const emit = defineEmits<{ toggleStatus: [] }>();
const { t } = useI18n();

const roundingOptions = computed(() =>
  ROUNDING_DIRECTIONS.map((d) => ({ value: d, label: t(`rules.config.rounding.${d}`) })),
);
const running = computed({
  get: () => props.running,
  set: () => emit('toggleStatus'),
});
</script>

<template>
  <AppCard :heading="$t('programs.overview.infoHeading')">
    <form class="program-info" novalidate @submit.prevent>
      <AppFormField :label="$t('programs.form.name')" for="name" :error="props.errors.name">
        <AppInput id="name" v-model="name" :invalid="!!props.errors.name" />
      </AppFormField>
      <!-- CR 2026-09-30 (A5): prefixes reward names; fixed once published. -->
      <AppFormField
        :label="$t('programs.slug.label')"
        for="slug"
        :hint="props.isDraft ? $t('programs.slug.draftHint') : $t('programs.slug.locked')"
        :error="props.errors.slug"
      >
        <AppInput
          id="slug"
          v-model="slug"
          monospace
          :readonly="!props.isDraft"
          :invalid="!!props.errors.slug"
          :described-by="props.errors.slug ? 'slug-error' : 'slug-hint'"
        />
      </AppFormField>
      <!-- 1.3.CL item 7: an Active/Inactive switch, saved immediately; only after publish. -->
      <AppFormField
        :label="$t('programs.status.label')"
        label-id="status-label"
        :hint="props.isDraft ? $t('programs.status.draftHint') : $t('programs.status.hint')"
      >
        <div class="program-info__switch">
          <AppSwitch
            v-model="running"
            :disabled="props.isDraft || props.togglingStatus"
            labelled-by="status-label"
          />
          <span>{{ props.running ? $t('common.active') : $t('common.inactive') }}</span>
        </div>
      </AppFormField>
      <!-- CR 2026-10-06 Phase 4: the direction rules inherit; precision is the wallet's. -->
      <AppFormField
        :label="$t('programs.defaultRounding.label')"
        for="defaultRounding"
        :hint="$t('programs.defaultRounding.hint')"
      >
        <AppSelect id="defaultRounding" v-model="defaultRounding" :options="roundingOptions" />
      </AppFormField>
      <AppFormField :label="$t('programs.form.description')" for="description">
        <AppTextarea id="description" v-model="description" />
      </AppFormField>
      <!-- 1.3.CL item 1: tier qualifying account and expiry warning moved to Account Types. -->
      <AppFormField
        :label="$t('accountTypes.tierQualifying.label')"
        :hint="$t('programs.tierQualifying.movedHint')"
      >
        <p class="program-info__value">
          {{ props.qualifyingAccountName ?? $t('programs.tierQualifying.none') }}
        </p>
        <RouterLink :to="props.accountTypesLink" class="program-info__link">
          {{ $t('programs.tierQualifying.manage') }}
        </RouterLink>
      </AppFormField>
    </form>
  </AppCard>
</template>

<style scoped>
.program-info {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.program-info__switch {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.program-info__value {
  margin: 0;
}
.program-info__link {
  font-size: 0.75rem;
  font-weight: 500;
}
</style>
