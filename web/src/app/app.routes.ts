import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/auth/guards';
import { tenantResolvedGuard } from './core/tenant/tenant.guard';
import { featureGuard } from './core/config/feature-flags';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.page').then((m) => m.LoginPage),
    title: 'Sign in',
  },
  {
    path: 'select-tenant',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./layout/select-tenant/select-tenant.page').then((m) => m.SelectTenantPage),
    title: 'Select tenant',
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        canActivate: [tenantResolvedGuard],
        loadChildren: () => import('./features/dashboard/dashboard.routes').then((m) => m.DASHBOARD_ROUTES),
      },
      {
        path: 'programs',
        canActivate: [tenantResolvedGuard],
        loadChildren: () => import('./features/programs/programs.routes').then((m) => m.PROGRAMS_ROUTES),
      },
      {
        path: 'customers',
        canActivate: [tenantResolvedGuard],
        loadChildren: () =>
          import('./features/customers/customers.routes').then((m) => m.CUSTOMERS_ROUTES),
      },
      {
        path: 'complaints',
        canActivate: [tenantResolvedGuard],
        loadChildren: () => import('./features/complaints/complaints.routes').then((m) => m.COMPLAINTS_ROUTES),
      },
      {
        path: 'platform',
        canMatch: [roleGuard('platform_admin')],
        loadChildren: () => import('./features/platform/platform.routes').then((m) => m.PLATFORM_ROUTES),
      },
      {
        path: 'events',
        canActivate: [tenantResolvedGuard, featureGuard('eventSimulator')],
        loadChildren: () => import('./features/events/events.routes').then((m) => m.EVENTS_ROUTES),
      },
    ],
  },
  {
    path: 'forbidden',
    loadComponent: () => import('./layout/status-pages/forbidden.page').then((m) => m.ForbiddenPage),
    title: 'Access denied',
  },
  {
    path: 'not-found',
    loadComponent: () => import('./layout/status-pages/not-found.page').then((m) => m.NotFoundPage),
    title: 'Not found',
  },
  { path: '**', redirectTo: 'not-found' },
];
