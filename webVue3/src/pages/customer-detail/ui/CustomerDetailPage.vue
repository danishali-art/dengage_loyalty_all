<script setup lang="ts">
import { AppButton, AppEmptyState, AppIcon, AppPageHeader, AppTabs } from '@/shared/ui';
import { useCustomerDetailPage } from '../model/useCustomerDetailPage';
import EventDrawer from './EventDrawer.vue';
import OverviewTab from './tabs/OverviewTab.vue';
import ActivityTab from './tabs/ActivityTab.vue';
import EventsTab from './tabs/EventsTab.vue';
import RulesTab from './tabs/RulesTab.vue';
import StreaksTab from './tabs/StreaksTab.vue';
import RewardsTab from './tabs/RewardsTab.vue';
import TiersTab from './tabs/TiersTab.vue';
import CardBucketsTab from './tabs/CardBucketsTab.vue';
import MessagesTab from './tabs/MessagesTab.vue';

const props = defineProps<{ contactKey: string }>();
const { contactKey, crumbs, tabs, tab, loading, profile, drawer, openEvent, copyContactKey } =
  useCustomerDetailPage(props);
</script>

<template>
  <AppPageHeader :heading="contactKey" :crumbs="crumbs">
    <template #actions>
      <AppButton
        variant="text"
        size="sm"
        :aria-label="$t('customers.detail.copyKey')"
        @click="copyContactKey"
      >
        <AppIcon name="copy" />
      </AppButton>
    </template>
    <!-- The tabs sit in the header, under the customer (Option C, 2026-10-03). -->
    <template v-if="!loading && profile" #below>
      <AppTabs v-model="tab" :tabs="tabs" :label="$t('nav.customers')" />
    </template>
  </AppPageHeader>

  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-8 py-6">
    <p v-if="loading" class="text-sm text-muted-foreground" aria-busy="true">{{ $t('common.loading') }}</p>
    <AppEmptyState
      v-else-if="!profile"
      :heading="$t('customers.detail.notFound')"
      :description="$t('customers.detail.notFoundBody')"
      icon="🔍"
    />
    <template v-else>
      <OverviewTab v-if="tab === 'overview'" :profile="profile" />
      <ActivityTab
        v-else-if="tab === 'activity'"
        :contact-key="contactKey"
        :profile="profile"
        @open-event="openEvent"
      />
      <EventsTab v-else-if="tab === 'events'" :contact-key="contactKey" @open-event="openEvent" />
      <RulesTab v-else-if="tab === 'rules'" :contact-key="contactKey" @open-event="openEvent" />
      <StreaksTab v-else-if="tab === 'streaks'" :contact-key="contactKey" />
      <RewardsTab v-else-if="tab === 'rewards'" :contact-key="contactKey" @open-event="openEvent" />
      <TiersTab
        v-else-if="tab === 'tiers'"
        :contact-key="contactKey"
        :profile="profile"
        @open-event="openEvent"
      />
      <CardBucketsTab v-else-if="tab === 'cardBuckets'" :contact-key="contactKey" @open-event="openEvent" />
      <MessagesTab v-else :contact-key="contactKey" />
    </template>
  </div>

  <EventDrawer v-model:request="drawer" :contact-key="contactKey" />
</template>
