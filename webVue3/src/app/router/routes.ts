import type { RouteComponent, RouteRecordRaw } from 'vue-router';
import { authGuard, featureGuard, roleGuard, tenantResolvedGuard } from './guards';

const statusPage = () => import('@/pages/status').then((m) => m.StatusPage);

/** A screen inside one program: `/programs/:programId/<segment>`, the program id as a prop. */
const programPage = (
  segment: string,
  component: () => Promise<RouteComponent>,
  titleKey: string,
): RouteRecordRaw => ({
  path: `programs/:programId/${segment}`,
  component,
  props: true,
  beforeEnter: [tenantResolvedGuard],
  meta: { titleKey },
});

/** A platform screen: `/platform/<segment>`, platform_admin only, route params as props. */
const platformPage = (
  segment: string,
  component: () => Promise<RouteComponent>,
  titleKey: string,
): RouteRecordRaw => ({
  path: `platform/${segment}`,
  component,
  props: true,
  beforeEnter: [roleGuard('platform_admin')],
  meta: { titleKey },
});

export const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    component: () => import('@/pages/login').then((m) => m.LoginPage),
    meta: { titleKey: 'auth.login.title' },
  },
  {
    path: '/select-tenant',
    component: () => import('@/pages/select-tenant').then((m) => m.SelectTenantPage),
    beforeEnter: [authGuard],
    meta: { titleKey: 'tenant.select.title' },
  },
  {
    path: '/',
    component: () => import('../layouts/ShellLayout.vue'),
    beforeEnter: [authGuard],
    children: [
      { path: '', redirect: '/dashboard' },
      {
        path: 'dashboard',
        component: () => import('@/pages/dashboard').then((m) => m.DashboardPage),
        beforeEnter: [tenantResolvedGuard],
        meta: { titleKey: 'nav.dashboard' },
      },
      {
        path: 'programs',
        component: () => import('@/pages/programs-list').then((m) => m.ProgramsListPage),
        beforeEnter: [tenantResolvedGuard],
        meta: { titleKey: 'programs.list.heading' },
      },
      {
        path: 'programs/:programId',
        component: () => import('@/pages/program-overview').then((m) => m.ProgramOverviewPage),
        props: true,
        beforeEnter: [tenantResolvedGuard],
        meta: { titleKey: 'programs.overview.fallbackHeading' },
      },
      // Phase 1.
      programPage(
        'account-types',
        () => import('@/pages/account-types').then((m) => m.AccountTypesPage),
        'programs.overview.cards.accountTypes.label',
      ),
      programPage('tiers', () => import('@/pages/tiers').then((m) => m.TiersPage), 'tiers.heading'),
      programPage(
        'rewards',
        () => import('@/pages/rewards').then((m) => m.RewardsPage),
        'programs.overview.cards.rewards.label',
      ),
      programPage(
        'history',
        () => import('@/pages/program-history').then((m) => m.ProgramHistoryPage),
        'history.heading',
      ),
      // Phase 2. Each `…/new` route is listed before `…/:id`, so "new" isn't read as an id.
      programPage('rules', () => import('@/pages/rules').then((m) => m.RulesPage), 'rules.heading'),
      programPage(
        'rules/new',
        () => import('@/pages/rule-edit').then((m) => m.RuleEditPage),
        'rules.form.newHeading',
      ),
      programPage(
        'rules/:ruleId',
        () => import('@/pages/rule-edit').then((m) => m.RuleEditPage),
        'rules.form.editHeading',
      ),
      programPage(
        'streak-campaigns',
        () => import('@/pages/streak-campaigns').then((m) => m.StreakCampaignsPage),
        'streaks.heading',
      ),
      programPage(
        'streak-campaigns/new',
        () => import('@/pages/streak-campaign-edit').then((m) => m.StreakCampaignEditPage),
        'streaks.form.newHeading',
      ),
      programPage(
        'streak-campaigns/:campaignId',
        () => import('@/pages/streak-campaign-edit').then((m) => m.StreakCampaignEditPage),
        'streaks.form.editHeading',
      ),
      programPage(
        'card-buckets',
        () => import('@/pages/card-buckets').then((m) => m.CardBucketsPage),
        'cardBuckets.heading',
      ),
      programPage(
        'card-buckets/new',
        () => import('@/pages/card-bucket-edit').then((m) => m.CardBucketEditPage),
        'cardBuckets.form.newHeading',
      ),
      programPage(
        'card-buckets/:bucketId',
        () => import('@/pages/card-bucket-edit').then((m) => m.CardBucketEditPage),
        'cardBuckets.form.editHeading',
      ),
      // Phase 3.
      {
        path: 'customers',
        component: () => import('@/pages/customers').then((m) => m.CustomersPage),
        beforeEnter: [tenantResolvedGuard],
        meta: { titleKey: 'nav.customers' },
      },
      {
        path: 'customers/:contactKey',
        component: () => import('@/pages/customer-detail').then((m) => m.CustomerDetailPage),
        props: true,
        beforeEnter: [tenantResolvedGuard],
        meta: { titleKey: 'customers.detail.title' },
      },
      {
        path: 'events',
        component: () => import('@/pages/event-simulator').then((m) => m.EventSimulatorPage),
        beforeEnter: [tenantResolvedGuard, featureGuard('eventSimulator')],
        meta: { titleKey: 'events.simulator.heading' },
      },
      // Phase 4: the platform screens, platform_admin only. The tenant is the route's, never the
      // active tenant context.
      { path: 'platform', redirect: '/platform/tenants' },
      platformPage(
        'tenants',
        () => import('@/pages/platform-tenants').then((m) => m.TenantsPage),
        'platform.heading',
      ),
      platformPage(
        'tenants/:tenantId',
        () => import('@/pages/platform-tenant-overview').then((m) => m.TenantOverviewPage),
        'nav.tenant.overview',
      ),
      platformPage(
        'tenants/:tenantId/api-keys',
        () => import('@/pages/platform-api-keys').then((m) => m.ApiKeysPage),
        'platform.apiKeys.heading',
      ),
      platformPage(
        'tenants/:tenantId/admin-users',
        () => import('@/pages/platform-admin-users').then((m) => m.AdminUsersPage),
        'platform.admins.heading',
      ),
    ],
  },
  {
    path: '/forbidden',
    component: statusPage,
    props: { kind: 'forbidden' },
    meta: { titleKey: 'forbidden.title' },
  },
  {
    path: '/not-found',
    component: statusPage,
    props: { kind: 'notFound' },
    meta: { titleKey: 'notFound.title' },
  },
  { path: '/:pathMatch(.*)*', redirect: '/not-found' },
];
