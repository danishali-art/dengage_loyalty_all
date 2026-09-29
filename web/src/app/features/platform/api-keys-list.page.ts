import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { PageHeader } from '../../shared/ui/page-header';
import { DataTable, Column } from '../../shared/ui/data-table';
import { StatusPill } from '../../shared/ui/status-pill';
import { Button } from '../../shared/ui/button';
import { IconButton } from '../../shared/ui/icon-button';
import { Paginator } from '../../shared/ui/paginator';
import { DialogService } from '../../core/ui/dialog.service';
import { ConfirmService } from '../../core/ui/confirm.service';
import { ToastService } from '../../core/ui/toast.service';
import { PlatformTenantContextStore } from '../../core/platform/platform-tenant-context.store';
import { PlatformService } from './platform.service';
import { ApiKeyCreatedDialog } from './api-key-created.dialog';
import { ApiKey, CreateApiKeyResult } from './platform.model';

const PAGE_SIZE = 15;

@Component({
  selector: 'app-api-keys-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, DataTable, StatusPill, Button, IconButton, Paginator, DatePipe],
  template: `
    <app-page-header
      heading="API Keys"
      [crumbs]="[
        { label: 'Tenants', link: ['/platform/tenants'] },
        { label: tenantName(), link: ['/platform/tenants', tenantId()] },
        { label: 'API Keys' },
      ]"
    >
      <div actions>
        <app-button [pending]="creating()" (click)="create()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true"><path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" /></svg>
          New key
        </app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl px-8 py-6">
      <div class="card !p-0">
        <app-data-table
          [columns]="columns"
          [rows]="apiKeys()"
          caption="API keys"
          [busy]="loading()"
          emptyText="No API keys yet — create one to let this tenant send events."
          [trackKey]="trackById"
        >
          <ng-template #row let-key>
            <td class="px-4 py-3.5 font-mono text-xs text-gray-700">{{ key.prefix }}…</td>
            <td class="px-4 py-3.5 text-gray-500">{{ key.createdAt | date: 'medium' }}</td>
            <td class="px-4 py-3.5 text-gray-500">{{ key.lastUsedAt ? (key.lastUsedAt | date: 'medium') : 'Never used' }}</td>
            <td class="px-4 py-3.5">
              @if (key.revokedAt) {
                <app-status-pill tone="neutral">revoked</app-status-pill>
              } @else {
                <app-icon-button ariaLabel="Revoke key" (click)="revoke(key)">🗑</app-icon-button>
              }
            </td>
          </ng-template>
        </app-data-table>
        <div class="px-4">
          <app-paginator [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="loadPage($event)" />
        </div>
      </div>
    </div>
  `,
})
export class ApiKeysListPage implements OnInit {
  readonly tenantId = input.required<string>();

  private readonly platform = inject(PlatformService);
  private readonly dialog = inject(DialogService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly context = inject(PlatformTenantContextStore);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly apiKeys = signal<readonly ApiKey[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly creating = signal(false);
  protected readonly trackById = (k: ApiKey): string => k.id;
  protected readonly tenantName = (): string => this.context.tenant()?.name ?? this.tenantId();

  protected readonly columns: Column<ApiKey>[] = [
    { key: 'prefix', header: 'Prefix' },
    { key: 'createdAt', header: 'Created' },
    { key: 'lastUsedAt', header: 'Last used' },
    { key: 'actions', header: '' },
  ];

  ngOnInit(): void {
    void this.loadPage(1);
  }

  protected async loadPage(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.platform.listApiKeys(this.tenantId(), page, this.pageSize);
      this.apiKeys.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
    } finally {
      this.loading.set(false);
    }
  }

  protected async create(): Promise<void> {
    this.creating.set(true);
    try {
      const result = await this.platform.createApiKey(this.tenantId());
      this.dialog.open<void, CreateApiKeyResult, ApiKeyCreatedDialog>(ApiKeyCreatedDialog, { data: result });
      await this.loadPage(1);
    } finally {
      this.creating.set(false);
    }
  }

  protected async revoke(key: ApiKey): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Revoke API key',
      message: `Revoke key ${key.prefix}…? Any client still using it will start getting 401s immediately.`,
      confirmLabel: 'Revoke',
      destructive: true,
    });
    if (!ok) return;
    await this.platform.revokeApiKey(this.tenantId(), key.id);
    this.toast.success('API key revoked');
    await this.loadPage(this.page());
  }
}
