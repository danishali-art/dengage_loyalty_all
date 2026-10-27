import type { RouteLocationRaw } from 'vue-router';

export interface Crumb {
  label: string;
  to?: RouteLocationRaw;
}
