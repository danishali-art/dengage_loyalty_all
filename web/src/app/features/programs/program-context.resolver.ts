import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { ProgramContextStore } from '../../core/program/program-context.store';
import { ProgramsService } from './programs.service';
import { Program } from './program.model';

/**
 * Resolves once per `/programs/:programId` entry (not on every child sub-page navigation, since
 * they share this same parent `ActivatedRoute`) and populates `ProgramContextStore` before the
 * Overview page or the Sidebar's contextual nav render. Lives here, not in `core/`, because it
 * needs `ProgramsService` — lower layers must never depend on a feature.
 */
export const programContextResolver: ResolveFn<Program> = async (route) => {
  const programId = route.paramMap.get('programId')!;
  const programs = inject(ProgramsService);
  const store = inject(ProgramContextStore);

  const program = await programs.get(programId);
  store.setProgram({ id: program.id, name: program.name });
  return program;
};
