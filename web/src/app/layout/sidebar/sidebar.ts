import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter, map } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { FeatureFlagService } from '../../core/config/feature-flags';
import { SessionStore } from '../../core/auth/session.store';
import { ProgramContextStore } from '../../core/program/program-context.store';
import { PlatformTenantContextStore } from '../../core/platform/platform-tenant-context.store';

interface NavItem {
  label: string;
  link: string;
  icon: string;
  show: () => boolean;
}

interface SubNavItem {
  label: string;
  segment: string;
}

const PROGRAM_SUB_NAV: SubNavItem[] = [
  { label: 'Overview', segment: '' },
  { label: 'Account Types', segment: 'account-types' },
  { label: 'Tiers', segment: 'tiers' },
  { label: 'Rules', segment: 'rules' },
  { label: 'Streak Campaigns', segment: 'streak-campaigns' },
  { label: 'Card Buckets', segment: 'card-buckets' },
  { label: 'Rewards', segment: 'rewards' },
];

const TENANT_SUB_NAV: SubNavItem[] = [
  { label: 'Overview', segment: '' },
  { label: 'API Keys', segment: 'api-keys' },
  { label: 'Tenant Admins', segment: 'admin-users' },
];

@Component({
  selector: 'app-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, TranslatePipe],
  template: `
    <aside class="flex h-screen w-60 flex-shrink-0 flex-col border-r border-gray-200 bg-white">
      <a routerLink="/dashboard" class="flex items-center gap-2.5 px-5 py-4" aria-label="Loyalty home">
        <span class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg bg-brand text-base font-bold text-white">
          L·
        </span>
        <span class="text-sm font-bold text-gray-900">Loyalty</span>
      </a>

      <nav class="flex-1 overflow-y-auto px-3 pb-4" aria-label="Primary">
        @for (item of items; track item.link) {
          @if (item.show()) {
            <a
              [routerLink]="item.link"
              routerLinkActive="bg-brand-light text-brand"
              [routerLinkActiveOptions]="{ exact: false }"
              class="flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm font-medium text-gray-600 transition-colors hover:bg-gray-100 hover:text-gray-800"
            >
              <span aria-hidden="true" class="w-5 text-center text-base">{{ item.icon }}</span>
              {{ item.label | translate }}
            </a>

            <!-- Contextual sub-nav: which program's Account Types/Tiers/Rules/Rewards, or
                 which tenant's API Keys/Tenant Admins, once the user is inside one. -->
            @if (item.link === '/programs' && programSubNav(); as sub) {
              <div class="ml-4 mt-0.5 space-y-0.5 border-l border-gray-200 pl-3">
                @for (s of programSubItems; track s.segment) {
                  <a
                    [routerLink]="s.segment ? ['/programs', sub.programId, s.segment] : ['/programs', sub.programId]"
                    routerLinkActive="text-brand font-medium"
                    [routerLinkActiveOptions]="{ exact: true }"
                    class="block truncate rounded-md px-2 py-1.5 text-xs text-gray-500 transition-colors hover:bg-gray-100 hover:text-gray-800"
                    [attr.title]="sub.programName"
                  >
                    {{ s.label }}
                  </a>
                }
              </div>
            }
            @if (item.link === '/platform' && tenantSubNav(); as sub) {
              <div class="ml-4 mt-0.5 space-y-0.5 border-l border-gray-200 pl-3">
                @for (s of tenantSubItems; track s.segment) {
                  <a
                    [routerLink]="s.segment ? ['/platform/tenants', sub.tenantId, s.segment] : ['/platform/tenants', sub.tenantId]"
                    routerLinkActive="text-brand font-medium"
                    [routerLinkActiveOptions]="{ exact: true }"
                    class="block truncate rounded-md px-2 py-1.5 text-xs text-gray-500 transition-colors hover:bg-gray-100 hover:text-gray-800"
                    [attr.title]="sub.tenantName"
                  >
                    {{ s.label }}
                  </a>
                }
              </div>
            }
          }
        }
      </nav>
    </aside>
  `,
})
export class Sidebar {
  private readonly flags = inject(FeatureFlagService);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly programContext = inject(ProgramContextStore);
  private readonly tenantContext = inject(PlatformTenantContextStore);

  protected readonly programSubItems = PROGRAM_SUB_NAV;
  protected readonly tenantSubItems = TENANT_SUB_NAV;

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  // The context stores don't clear themselves on navigating away (see ProgramContextStore's doc)
  // — cross-check the current URL too, so a stale store from a previous visit never makes the
  // sub-nav appear on an unrelated screen (e.g. Customers).
  protected readonly programSubNav = computed(() => {
    const programId = this.programContext.programId();
    const program = this.programContext.program();
    if (!programId || !program || !this.url().startsWith(`/programs/${programId}`)) return null;
    return { programId, programName: program.name };
  });

  protected readonly tenantSubNav = computed(() => {
    const tenantId = this.tenantContext.tenantId();
    const tenant = this.tenantContext.tenant();
    if (!tenantId || !tenant || !this.url().startsWith(`/platform/tenants/${tenantId}`)) return null;
    return { tenantId, tenantName: tenant.name };
  });

  protected readonly items: NavItem[] = [
    { label: 'nav.dashboard', link: '/dashboard', icon: '◱', show: () => true },
    { label: 'nav.programs', link: '/programs', icon: '▦', show: () => true },
    { label: 'nav.customers', link: '/customers', icon: '☻', show: () => true },
    { label: 'nav.complaints', link: '/complaints', icon: '✉', show: () => true },
    { label: 'nav.events', link: '/events', icon: '⚡', show: () => this.flags.enabled('eventSimulator') },
    { label: 'nav.platform', link: '/platform', icon: '⚙', show: () => this.session.isPlatformAdmin() },
    { label: 'nav.reports', link: '/reports', icon: '▤', show: () => this.flags.enabled('reports') },
    { label: 'nav.settings', link: '/settings', icon: '⚑', show: () => this.flags.enabled('settings') },
  ];
}
