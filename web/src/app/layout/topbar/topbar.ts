import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TenantSwitcher } from '../tenant-switcher/tenant-switcher';
import { UserMenu } from '../user-menu/user-menu';

@Component({
  selector: 'app-topbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TenantSwitcher, UserMenu],
  template: `
    <header
      class="flex h-14 flex-shrink-0 items-center gap-4 border-b border-gray-200 bg-white px-5"
    >
      <span class="text-sm font-medium text-gray-700">Loyalty</span>
      <div class="flex flex-1 justify-center">
        <label class="relative w-full max-w-md">
          <span class="sr-only">Search</span>
          <input
            type="search"
            placeholder="Search"
            class="w-full rounded-lg bg-gray-100 px-3 py-2 text-sm placeholder-gray-500 outline-none focus:ring-2 focus:ring-brand/20"
          />
        </label>
      </div>
      <app-tenant-switcher />
      <app-user-menu />
    </header>
  `,
})
export class Topbar {}
