/**
 * Toasts from anywhere — components, composables, the API client's error hook. Requests go
 * through this tiny emitter to `AppToastHost`, which renders them; nothing outside
 * `shared/ui` knows which library draws them.
 */
export type ToastKind = 'success' | 'error' | 'info' | 'warning';

export interface ToastRequest {
  kind: ToastKind;
  title: string;
  detail?: string;
  /** Copyable correlation id, shown on errors. */
  traceId?: string;
}

type Listener = (toast: ToastRequest) => void;
const listeners = new Set<Listener>();

export function onToast(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function emit(toast: ToastRequest): void {
  for (const listener of listeners) listener(toast);
}

export const notify = {
  success: (title: string, detail?: string): void => emit({ kind: 'success', title, detail }),
  info: (title: string, detail?: string): void => emit({ kind: 'info', title, detail }),
  warning: (title: string, detail?: string): void => emit({ kind: 'warning', title, detail }),
  error: (title: string, opts?: { detail?: string; traceId?: string }): void =>
    emit({ kind: 'error', title, detail: opts?.detail, traceId: opts?.traceId }),
};
