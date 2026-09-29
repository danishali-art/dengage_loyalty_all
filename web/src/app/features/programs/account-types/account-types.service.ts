import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../../core/http/api-client';
import { Page } from '../../../core/http/pagination';
import { AccountType, CreateAccountTypeRequest, UpdateAccountTypeRequest } from './account-type.model';

/** Realistically bounded (a program rarely has more than a handful) — used by `listAll()`. */
const ALL_PAGE_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class AccountTypesService {
  private readonly api = inject(ApiClient);

  list(programId: string, page = 1, pageSize = 20): Promise<Page<AccountType>> {
    return firstValueFrom(
      this.api.tenantScope.getPage<AccountType>(`programs/${programId}/account-types`, { page, pageSize }),
    );
  }

  /** Every account type in the program, for selects that need the full set (not just one page). */
  async listAll(programId: string): Promise<readonly AccountType[]> {
    return (await this.list(programId, 1, ALL_PAGE_SIZE)).data;
  }

  create(programId: string, request: CreateAccountTypeRequest): Promise<AccountType> {
    return firstValueFrom(
      this.api.tenantScope.post<AccountType>(`programs/${programId}/account-types`, request, {
        skipErrorToast: true,
      }),
    );
  }

  update(programId: string, accountTypeId: string, request: UpdateAccountTypeRequest): Promise<AccountType> {
    return firstValueFrom(
      this.api.tenantScope.patch<AccountType>(`programs/${programId}/account-types/${accountTypeId}`, request, {
        skipErrorToast: true,
      }),
    );
  }
}
