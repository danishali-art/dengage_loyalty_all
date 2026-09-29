import { Injectable, signal } from '@angular/core';

/** Deliberately minimal — just enough for the Sidebar's label — so this store (a lower layer)
 * never has to import the full `Program` type from `features/programs`. */
export interface ProgramSummary {
  id: string;
  name: string;
}

/**
 * Route-scoped context for "which program am I currently working inside" — populated by
 * `programContextResolver` (in `features/programs/`, since it needs `ProgramsService`) on the
 * `/programs/:programId` parent route before any of its child pages (Overview, Account Types,
 * Tiers, Rules, Rewards) or the Sidebar's contextual nav render. Mirrors `TenantContext`/
 * `TenantStore`'s shape, one level down.
 */
@Injectable({ providedIn: 'root' })
export class ProgramContextStore {
  private readonly _programId = signal<string | null>(null);
  private readonly _program = signal<ProgramSummary | null>(null);

  readonly programId = this._programId.asReadonly();
  readonly program = this._program.asReadonly();

  setProgram(program: ProgramSummary): void {
    this._programId.set(program.id);
    this._program.set(program);
  }

  /** Called after an in-place edit (e.g. the Overview form) so the Sidebar's label stays fresh. */
  updateName(program: ProgramSummary): void {
    if (program.id === this._programId()) this._program.set(program);
  }
}
