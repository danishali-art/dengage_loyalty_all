import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { PageHeader } from '../../shared/ui/page-header';
import { StatusPill } from '../../shared/ui/status-pill';
import { Button } from '../../shared/ui/button';
import { ToastService } from '../../core/ui/toast.service';
import { PlatformTenantContextStore } from '../../core/platform/platform-tenant-context.store';
import { PlatformService } from './platform.service';

interface SummaryCard {
  label: string;
  description: string;
  icon: string;
  link: string;
}

@Component({
  selector: 'app-tenant-overview-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, StatusPill, Button, DatePipe],
  template: `
    <app-page-header
      [heading]="tenant()?.name ?? tenantId()"
      [crumbs]="[{ label: 'Tenants', link: ['/platform/tenants'] }, { label: tenant()?.name ?? tenantId() }]"
    >
      <div actions>
        @if (tenant(); as t) {
          <app-button variant="secondary" [pending]="statusPending()" (click)="toggleStatus()">
            {{ t.status === 'active' ? 'Suspend' : 'Reactivate' }}
          </app-button>
        }
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl space-y-6 px-8 py-6">
      @if (tenant(); as t) {
        <section class="card flex items-center gap-6">
          <div>
            <p class="section-label">Status</p>
            <app-status-pill class="mt-1" [tone]="t.status === 'active' ? 'success' : 'neutral'" [dot]="true">
              {{ t.status }}
            </app-status-pill>
          </div>
          <div>
            <p class="section-label">Tenant id</p>
            <p class="mt-1 font-mono text-sm text-gray-700">{{ t.id }}</p>
          </div>
          <div>
            <p class="section-label">Created</p>
            <p class="mt-1 text-sm text-gray-700">{{ t.createdAt | date: 'medium' }}</p>
          </div>
        </section>

        <div class="grid grid-cols-2 gap-4">
          @for (card of cards; track card.link) {
            <a [routerLink]="[card.link]" class="card block transition-colors hover:border-brand">
              <div class="flex items-center gap-3">
                <div class="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg bg-brand-light text-lg">
                  {{ card.icon }}
                </div>
                <div>
                  <div class="text-sm font-medium text-gray-800">{{ card.label }}</div>
                  <div class="mt-0.5 text-xs text-gray-400">{{ card.description }}</div>
                </div>
              </div>
              <div class="mt-3 text-right text-sm font-medium text-brand">Manage ›</div>
            </a>
          }
        </div>
      }
    </div>
  `,
})
export class TenantOverviewPage {
  readonly tenantId = input.required<string>();

  private readonly platform = inject(PlatformService);
  private readonly toast = inject(ToastService);
  private readonly context = inject(PlatformTenantContextStore);

  protected readonly tenant = this.context.tenant;
  protected readonly statusPending = signal(false);

  protected readonly cards: SummaryCard[] = [
    { label: 'API Keys', description: 'Server-to-server event credentials', icon: '🔑', link: 'api-keys' },
    { label: 'Tenant Admins', description: 'Who can sign in and manage this tenant', icon: '👤', link: 'admin-users' },
  ];

  protected async toggleStatus(): Promise<void> {
    const tenant = this.tenant();
    if (!tenant) return;
    const nextStatus = tenant.status === 'active' ? 'suspended' : 'active';
    this.statusPending.set(true);
    try {
      const updated = await this.platform.updateTenant(tenant.id, { status: nextStatus });
      this.context.setTenant(updated);
      this.toast.success(`Tenant ${updated.status === 'active' ? 'reactivated' : 'suspended'}`);
    } finally {
      this.statusPending.set(false);
    }
  }
}
