import { apiClient, unwrap, withFlags, type Page } from '@/shared/api';
import type { CreateProgramRequest, Program, UpdateProgramRequest } from '../model/types';

/** Request functions for `/tenants/{tenantId}/programs` (ProgramsController.cs). Composables call these. */
export function listPrograms(tenantId: string, page: number, pageSize: number): Promise<Page<Program>> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs', {
      params: { path: { tenantId }, query: { page, pageSize } },
    }),
  );
}

export function getProgram(tenantId: string, programId: string): Promise<Program> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/programs/{programId}', {
      params: { path: { tenantId, programId } },
    }),
  );
}

/** The form maps server field errors itself, so no toast. */
export function createProgram(tenantId: string, body: CreateProgramRequest): Promise<Program> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/programs', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId } },
      body,
    }),
  );
}

export function updateProgram(
  tenantId: string,
  programId: string,
  body: UpdateProgramRequest,
): Promise<Program> {
  return unwrap(
    apiClient().PATCH('/api/v1/tenants/{tenantId}/programs/{programId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
      body,
    }),
  );
}

/** 1.3.CL items 8/9: Draft → Published, recording one ProgramPublication history version. */
export function publishProgram(tenantId: string, programId: string): Promise<Program> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/programs/{programId}/publish', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
    }),
  );
}

export function deleteProgram(tenantId: string, programId: string): Promise<void> {
  return unwrap(
    apiClient().DELETE('/api/v1/tenants/{tenantId}/programs/{programId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, programId } },
    }),
  );
}
