import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type { AccountType, CreateAccountTypeRequest, UpdateAccountTypeRequest } from '../model/types';

/** A program rarely has more than a handful of wallets; the API caps a page at 100. */
const ALL_PAGE_SIZE = 100;

/** `/tenants/{tenantId}/programs/{programId}/account-types` (AccountTypesController.cs). */
export function listAccountTypes(
  tenantId: string,
  programId: string,
  page: number,
  pageSize: number,
): Promise<Page<AccountType>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}/account-types', {
      params: { path: { tenantId, programId }, query: { page, pageSize } },
    }),
  );
}

export function listAllAccountTypes(tenantId: string, programId: string): Promise<Page<AccountType>> {
  return listAccountTypes(tenantId, programId, 1, ALL_PAGE_SIZE);
}

/** The form maps server field errors itself, so no toast. */
export function createAccountType(
  tenantId: string,
  programId: string,
  body: CreateAccountTypeRequest,
): Promise<AccountType> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/programs/{programId}/account-types', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
      body,
    }),
  );
}

export function updateAccountType(
  tenantId: string,
  programId: string,
  accountTypeId: string,
  body: UpdateAccountTypeRequest,
): Promise<AccountType> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}/account-types/{accountTypeId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId, accountTypeId } },
      body,
    }),
  );
}
