import { Routes } from '@angular/router';

export const COMPLAINTS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./complaints-list.page').then((m) => m.ComplaintsListPage),
    title: 'Complaints',
  },
];
