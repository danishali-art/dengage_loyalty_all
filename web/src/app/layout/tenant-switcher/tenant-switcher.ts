import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DialogService } from '../../core/ui/dialog.service';
import { TenantStore } from '../../core/tenant/tenant.store';
import { TenantPickerDialog } from './tenant-picker.dialog';

/**
 * Shows the active tenant. For a platform_admin, clicking it opens a searchable popup backed
 * by `GET /platform/tenants?search=`; for a tenant_admin it is a static label.
 */
@Component({
  selector: 'app-tenant-switcher',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.isSwitchable()) {
      <button
        type="button"
        class="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-sm text-gray-600 transition-colors hover:bg-gray-100"
        aria-haspopup="dialog"
        (click)="openPicker()"
      >
        <span class="font-medium">{{ label() }}</span>
        <span aria-hidden="true" class="text-gray-500">▾</span>
      </button>
    } @else {
      <span class="text-sm text-gray-600">{{ label() }}</span>
    }
  `,
})
export class TenantSwitcher {
  protected readonly store = inject(TenantStore);
  private readonly dialog = inject(DialogService);

  constructor() {
    void this.store.ensureActiveTenantCached();
  }

  protected label(): string {
    return this.store.activeTenant()?.name ?? 'No tenant';
  }

  protected openPicker(): void {
    this.dialog.open<void, void, TenantPickerDialog>(TenantPickerDialog);
  }
}
