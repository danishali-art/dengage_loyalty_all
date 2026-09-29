import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info' | 'warning';

export interface Toast {
  readonly id: number;
  readonly kind: ToastKind;
  readonly title: string;
  readonly detail?: string;
  /** Copyable correlation id, shown on errors. */
  readonly traceId?: string;
}

const DEFAULT_TTL: Record<ToastKind, number> = {
  success: 4000,
  info: 5000,
  warning: 7000,
  error: 0, // sticky — user dismisses
};

@Injectable({ providedIn: 'root' })
export class ToastService {
  private seq = 0;
  private readonly _toasts = signal<readonly Toast[]>([]);
  readonly toasts = this._toasts.asReadonly();

  show(kind: ToastKind, title: string, opts?: { detail?: string; traceId?: string; ttlMs?: number }): number {
    const id = ++this.seq;
    const toast: Toast = { id, kind, title, detail: opts?.detail, traceId: opts?.traceId };
    this._toasts.update((list) => [...list, toast]);

    const ttl = opts?.ttlMs ?? DEFAULT_TTL[kind];
    if (ttl > 0) setTimeout(() => this.dismiss(id), ttl);
    return id;
  }

  success(title: string, detail?: string): number {
    return this.show('success', title, { detail });
  }
  error(title: string, opts?: { detail?: string; traceId?: string }): number {
    return this.show('error', title, opts);
  }
  info(title: string, detail?: string): number {
    return this.show('info', title, { detail });
  }
  warning(title: string, detail?: string): number {
    return this.show('warning', title, { detail });
  }

  dismiss(id: number): void {
    this._toasts.update((list) => list.filter((t) => t.id !== id));
  }

  clear(): void {
    this._toasts.set([]);
  }
}
