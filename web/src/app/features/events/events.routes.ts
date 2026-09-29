import { Routes } from '@angular/router';

export const EVENTS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./event-simulator.page').then((m) => m.EventSimulatorPage),
    title: 'Event Simulator',
  },
];
