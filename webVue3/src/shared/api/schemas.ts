import type { components } from './generated/schema';

/** A generated wire type by its C# name, e.g. `ApiSchema<'ProgramResponse'>`. */
export type ApiSchema<Name extends keyof components['schemas']> = components['schemas'][Name];

/** Offset-paginated list envelope (`PagedResult<T>` in Api.Framework). */
export interface Page<T> {
  readonly data: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

export function totalPages(page: Pick<Page<unknown>, 'total' | 'pageSize'>): number {
  return Math.max(1, Math.ceil(page.total / Math.max(1, page.pageSize)));
}
