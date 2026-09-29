import { Routes } from '@angular/router';
import { platformTenantContextResolver } from './platform-tenant-context.resolver';

export const PLATFORM_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'tenants' },
  {
    path: 'tenants',
    loadComponent: () => import('./tenants-list.page').then((m) => m.TenantsListPage),
    title: 'Platform · Tenants',
  },
  {
    // Component-less grouping route, same pattern as programs.routes.ts — resolves once per
    // :tenantId entry, shared by Overview/API Keys/Tenant Admins so the Sidebar's contextual nav
    // and every child page see a populated PlatformTenantContextStore.
    path: 'tenants/:tenantId',
    resolve: { tenant: platformTenantContextResolver },
    children: [
      {
        path: '',
        loadComponent: () => import('./tenant-overview.page').then((m) => m.TenantOverviewPage),
        title: 'Platform · Tenant',
      },
      {
        path: 'api-keys',
        loadComponent: () => import('./api-keys-list.page').then((m) => m.ApiKeysListPage),
        title: 'API Keys',
      },
      {
        path: 'admin-users',
        loadComponent: () => import('./tenant-admins-list.page').then((m) => m.TenantAdminsListPage),
        title: 'Tenant Admins',
      },
    ],
  },
];
