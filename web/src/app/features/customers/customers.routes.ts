import { Routes } from '@angular/router';

export const CUSTOMERS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./customers-search.page').then((m) => m.CustomersSearchPage),
    title: 'Customers',
  },
  {
    path: ':contactKey',
    loadComponent: () => import('./customer-detail.page').then((m) => m.CustomerDetailPage),
    title: 'Customer',
  },
];
