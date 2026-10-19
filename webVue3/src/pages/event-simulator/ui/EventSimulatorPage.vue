<script setup lang="ts">
import {
  AppButton,
  AppCard,
  AppFormErrors,
  AppFormField,
  AppPageHeader,
  AppSelect,
  AppStatusPill,
  AppTextarea,
} from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/date';
import { useEventSimulatorPage } from '../model/useEventSimulatorPage';

const {
  typesLoaded,
  typesFailed,
  eventTypeOptions,
  typesHint,
  eventType,
  selectEventType,
  dataJson,
  sampleJson,
  useSample,
  help,
  sending,
  polling,
  formErrors,
  eventId,
  status,
  statusTone,
  send,
  pollStatus,
  reset,
} = useEventSimulatorPage();
</script>

<template>
  <AppPageHeader :heading="$t('events.simulator.heading')" :subtitle="$t('events.simulator.subtitle')" />

  <div class="mx-auto flex max-w-5xl flex-col gap-6 px-8 py-6">
    <AppFormErrors :messages="formErrors" />
    <AppFormErrors v-if="typesFailed" :messages="[$t('events.simulator.typesFailed')]" />

    <AppCard :heading="$t('events.simulator.event')">
      <div class="flex flex-col gap-4">
        <AppFormField
          :label="$t('events.simulator.eventType')"
          label-id="eventType-label"
          :hint="typesLoaded ? typesHint : $t('events.simulator.loadingTypes')"
        >
          <AppSelect
            id="eventType"
            searchable
            labelled-by="eventType-label"
            :options="eventTypeOptions"
            :placeholder="$t('events.simulator.selectType')"
            :model-value="eventType"
            @update:model-value="selectEventType($event ?? null)"
          />
        </AppFormField>

        <div class="flex flex-col gap-1.5">
          <div class="flex items-center justify-between">
            <label for="eventData" class="text-[0.8125rem] font-medium text-(--app-text-soft)">{{
              $t('events.simulator.data')
            }}</label>
            <AppButton v-if="dataJson !== sampleJson" variant="text" size="sm" @click="useSample">
              {{ $t('events.simulator.useSample') }}
            </AppButton>
          </div>
          <AppTextarea
            id="eventData"
            v-model="dataJson"
            :rows="8"
            class="font-mono text-xs"
            spellcheck="false"
          />
          <p class="m-0 text-xs text-muted-foreground">{{ $t('events.simulator.dataHint') }}</p>
          <p v-if="dataJson.includes('REPLACE_WITH_')" class="m-0 text-xs text-(--app-warn-fg)">
            {{ $t('events.simulator.placeholderHint') }}
          </p>
          <p v-if="help" class="m-0 text-xs text-(--app-text-soft)">
            {{ $t('events.simulator.requires') }}
            <code class="font-mono">{{ help.required.join(', ') }}</code
            >.
            <template v-if="help.optional">
              {{ $t('events.simulator.optional') }}
              <code class="font-mono">{{ help.optional.join(', ') }}</code
              >.
            </template>
            {{ $t(help.note) }}
          </p>
        </div>

        <div class="flex items-center gap-2">
          <AppButton :pending="sending" :disabled="!eventType" @click="send">{{
            $t('events.simulator.send')
          }}</AppButton>
          <AppButton v-if="eventId" variant="text" size="sm" @click="reset">{{
            $t('events.simulator.sendAnother')
          }}</AppButton>
        </div>
      </div>
    </AppCard>

    <AppCard v-if="eventId" :heading="$t('events.simulator.result')">
      <div class="flex flex-col gap-3">
        <div class="flex items-center justify-between">
          <div>
            <div class="font-mono text-xs text-muted-foreground">{{ $t('events.simulator.eventId') }}</div>
            <div class="font-mono text-sm">{{ eventId }}</div>
          </div>
          <AppStatusPill v-if="status" :tone="statusTone(status.status)" dot>{{
            status.status
          }}</AppStatusPill>
          <span v-else class="inline-flex items-center gap-2 text-xs text-muted-foreground">
            {{ $t('events.simulator.checking') }}
          </span>
        </div>
        <dl v-if="status" class="m-0 grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
          <dt class="text-muted-foreground">{{ $t('events.simulator.eventType') }}</dt>
          <dd class="m-0 font-mono">{{ status.eventType }}</dd>
          <dt class="text-muted-foreground">{{ $t('events.simulator.received') }}</dt>
          <dd class="m-0">{{ formatDateTime(status.receivedAt) }}</dd>
          <dt class="text-muted-foreground">{{ $t('events.simulator.processed') }}</dt>
          <dd class="m-0">{{ formatDateTime(status.processedAt) }}</dd>
          <template v-if="status.error">
            <dt class="text-muted-foreground">{{ $t('events.simulator.error') }}</dt>
            <dd class="m-0 text-(--app-danger-fg)">{{ status.error }}</dd>
          </template>
        </dl>
        <div v-if="status?.status === 'pending'">
          <AppButton variant="secondary" size="sm" :pending="polling" @click="pollStatus">{{
            $t('events.simulator.refresh')
          }}</AppButton>
        </div>
      </div>
    </AppCard>
  </div>
</template>
