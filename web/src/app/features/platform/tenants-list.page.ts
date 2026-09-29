import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { PageHeader } from '../../shared/ui/page-header';
import { DataTable, Column } from '../../shared/ui/data-table';
import { Paginator } from '../../shared/ui/paginator';
import { StatusPill } from '../../shared/ui/status-pill';
import { Button } from '../../shared/ui/button';
import { DialogService } from '../../core/ui/dialog.service';
import { ToastService } from '../../core/ui/toast.service';
import { PlatformService } from './platform.service';
import { CreateTenantDialog } from './create-tenant.dialog';
import { Tenant } from '../../core/tenant/tenant.model';

const PAGE_SIZE = 50;

@Component({
  selector: 'app-tenants-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, DataTable, Paginator, StatusPill, Button, DatePipe],
  template: `
    <app-page-header heading="Tenants" subtitle="Platform administration">
      <div actions>
        <app-button (click)="openCreate()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true"><path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" /></svg>
          New tenant
        </app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl px-8 py-6">
      <div class="card !p-0">
        <app-data-table
          [columns]="columns"
          [rows]="tenants()"
          caption="Tenants"
          [busy]="loading()"
          emptyText="No tenants yet"
          [trackKey]="trackById"
        >
          <ng-template #row let-tenant>
            <td class="cursor-pointer px-4 py-3.5 font-medium text-gray-800" (click)="open(tenant.id)">
              {{ tenant.name }}
            </td>
            <td class="cursor-pointer px-4 py-3.5 font-mono text-xs text-gray-500" (click)="open(tenant.id)">{{ tenant.id }}</td>
            <td class="cursor-pointer px-4 py-3.5" (click)="open(tenant.id)">
              <app-status-pill [tone]="tenant.status === 'active' ? 'success' : 'neutral'" [dot]="true">
                {{ tenant.status }}
              </app-status-pill>
            </td>
            <td class="cursor-pointer px-4 py-3.5 text-gray-500" (click)="open(tenant.id)">{{ tenant.createdAt | date: 'medium' }}</td>
          </ng-template>
        </app-data-table>
        <div class="px-4">
          <app-paginator [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="loadPage($event)" />
        </div>
      </div>
    </div>
  `,
})
export class TenantsListPage implements OnInit {
  private readonly platform = inject(PlatformService);
  private readonly dialog = inject(DialogService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly tenants = signal<readonly Tenant[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly trackById = (t: Tenant): string => t.id;

  protected readonly columns: Column<Tenant>[] = [
    { key: 'name', header: 'Name' },
    { key: 'id', header: 'Id' },
    { key: 'status', header: 'Status' },
    { key: 'createdAt', header: 'Created' },
  ];

  ngOnInit(): void {
    void this.loadPage(1);
  }

  protected async loadPage(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.platform.listTenants(page, this.pageSize);
      this.tenants.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
    } finally {
      this.loading.set(false);
    }
  }

  protected open(tenantId: string): void {
    void this.router.navigate(['/platform/tenants', tenantId]);
  }

  protected openCreate(): void {
    const ref = this.dialog.open<Tenant, void, CreateTenantDialog>(CreateTenantDialog);
    ref.closed.subscribe((tenant) => {
      if (!tenant) return;
      this.toast.success(`Tenant "${tenant.name}" created`);
      void this.loadPage(1);
    });
  }
}
