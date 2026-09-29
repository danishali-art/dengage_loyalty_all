import { Injectable, computed, signal } from '@angular/core';

/** Ref-counted in-flight request tracker. Drives the shell's top progress bar. */
@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly count = signal(0);
  readonly isLoading = computed(() => this.count() > 0);

  begin(): void {
    this.count.update((n) => n + 1);
  }

  end(): void {
    this.count.update((n) => Math.max(0, n - 1));
  }
}
