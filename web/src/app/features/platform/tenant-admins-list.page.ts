import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { PageHeader } from '../../shared/ui/page-header';
import { DataTable, Column } from '../../shared/ui/data-table';
import { StatusPill } from '../../shared/ui/status-pill';
import { Button } from '../../shared/ui/button';
import { EmptyState } from '../../shared/ui/empty-state';
import { Paginator } from '../../shared/ui/paginator';
import { DialogService } from '../../core/ui/dialog.service';
import { ToastService } from '../../core/ui/toast.service';
import { PlatformTenantContextStore } from '../../core/platform/platform-tenant-context.store';
import { PlatformService } from './platform.service';
import { CreateAdminUserDialog } from './create-admin-user.dialog';
import { AdminUser } from './platform.model';

const PAGE_SIZE = 15;

@Component({
  selector: 'app-tenant-admins-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, DataTable, StatusPill, Button, EmptyState, Paginator, DatePipe],
  template: `
    <app-page-header
      heading="Tenant Admins"
      [crumbs]="[
        { label: 'Tenants', link: ['/platform/tenants'] },
        { label: tenantName(), link: ['/platform/tenants', tenantId()] },
        { label: 'Tenant Admins' },
      ]"
    >
      <div actions>
        <app-button (click)="create()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true"><path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" /></svg>
          New admin
        </app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl px-8 py-6">
      <div class="card !p-0">
        @if (!loading() && admins().length === 0) {
          <div class="p-5">
            <app-empty-state heading="No tenant admins yet" description="Create one so someone can sign in and manage this tenant." icon="👤" />
          </div>
        } @else {
          <app-data-table
            [columns]="columns"
            [rows]="admins()"
            caption="Tenant admins"
            [busy]="loading()"
            [trackKey]="trackById"
          >
            <ng-template #row let-admin>
              <td class="px-4 py-3.5 font-medium text-gray-800">{{ admin.email }}</td>
              <td class="px-4 py-3.5">
                <app-status-pill [tone]="admin.status === 'active' ? 'success' : 'neutral'" [dot]="true">
                  {{ admin.status }}
                </app-status-pill>
              </td>
              <td class="px-4 py-3.5 text-gray-500">{{ admin.createdAt | date: 'medium' }}</td>
            </ng-template>
          </app-data-table>
          <div class="px-4">
            <app-paginator [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="loadPage($event)" />
          </div>
        }
      </div>
    </div>
  `,
})
export class TenantAdminsListPage implements OnInit {
  readonly tenantId = input.required<string>();

  private readonly platform = inject(PlatformService);
  private readonly dialog = inject(DialogService);
  private readonly toast = inject(ToastService);
  private readonly context = inject(PlatformTenantContextStore);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly admins = signal<readonly AdminUser[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly trackById = (a: AdminUser): string => a.id;
  protected readonly tenantName = (): string => this.context.tenant()?.name ?? this.tenantId();

  protected readonly columns: Column<AdminUser>[] = [
    { key: 'email', header: 'Email' },
    { key: 'status', header: 'Status' },
    { key: 'createdAt', header: 'Created' },
  ];

  ngOnInit(): void {
    void this.loadPage(1);
  }

  protected async loadPage(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.platform.listAdminUsers(this.tenantId(), page, this.pageSize);
      this.admins.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
    } finally {
      this.loading.set(false);
    }
  }

  protected create(): void {
    const ref = this.dialog.open<AdminUser, string, CreateAdminUserDialog>(CreateAdminUserDialog, {
      data: this.tenantId(),
    });
    ref.closed.subscribe((admin) => {
      if (!admin) return;
      this.toast.success(`Admin "${admin.email}" created`);
      void this.loadPage(1);
    });
  }
}
