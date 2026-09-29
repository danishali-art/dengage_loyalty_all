import { Routes } from '@angular/router';
import { programContextResolver } from './program-context.resolver';

export const PROGRAMS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./programs-list.page').then((m) => m.ProgramsListPage),
    title: 'Programs',
  },
  {
    // Component-less grouping route: resolves the program once per :programId entry (shared by
    // every child below, not re-run when navigating between them) so ProgramContextStore is
    // populated before the Sidebar's contextual nav or any child page renders.
    path: ':programId',
    resolve: { program: programContextResolver },
    children: [
      {
        path: '',
        loadComponent: () => import('./program-overview.page').then((m) => m.ProgramOverviewPage),
        title: 'Program',
      },
      {
        path: 'account-types',
        loadComponent: () =>
          import('./account-types/account-types-list.page').then((m) => m.AccountTypesListPage),
        title: 'Account Types',
      },
      {
        path: 'tiers',
        loadComponent: () => import('./tiers/tiers-list.page').then((m) => m.TiersListPage),
        title: 'Tiers',
      },
      {
        path: 'rewards',
        loadComponent: () => import('./rewards/rewards-list.page').then((m) => m.RewardsListPage),
        title: 'Rewards',
      },
      {
        path: 'history',
        loadComponent: () => import('./history/program-history.page').then((m) => m.ProgramHistoryPage),
        title: 'History',
      },
      {
        path: 'rules',
        loadComponent: () => import('./rules/rules-list.page').then((m) => m.RulesListPage),
        title: 'Rules',
      },
      {
        path: 'rules/new',
        loadComponent: () => import('./rules/rule-form.page').then((m) => m.RuleFormPage),
        title: 'New rule',
      },
      {
        path: 'rules/:ruleId',
        loadComponent: () => import('./rules/rule-form.page').then((m) => m.RuleFormPage),
        title: 'Edit rule',
      },
      {
        path: 'streak-campaigns',
        loadComponent: () =>
          import('./streak-campaigns/streak-campaigns-list.page').then((m) => m.StreakCampaignsListPage),
        title: 'Streak Campaigns',
      },
      {
        path: 'streak-campaigns/new',
        loadComponent: () =>
          import('./streak-campaigns/streak-campaign-form.page').then((m) => m.StreakCampaignFormPage),
        title: 'New streak campaign',
      },
      {
        path: 'streak-campaigns/:campaignId',
        loadComponent: () =>
          import('./streak-campaigns/streak-campaign-form.page').then((m) => m.StreakCampaignFormPage),
        title: 'Edit streak campaign',
      },
      {
        path: 'card-buckets',
        loadComponent: () =>
          import('./card-buckets/card-buckets-list.page').then((m) => m.CardBucketsListPage),
        title: 'Card Buckets',
      },
      {
        path: 'card-buckets/new',
        loadComponent: () =>
          import('./card-buckets/card-bucket-form.page').then((m) => m.CardBucketFormPage),
        title: 'New card bucket',
      },
      {
        path: 'card-buckets/:bucketId',
        loadComponent: () =>
          import('./card-buckets/card-bucket-form.page').then((m) => m.CardBucketFormPage),
        title: 'Edit card bucket',
      },
    ],
  },
];
