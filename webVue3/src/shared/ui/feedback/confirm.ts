import { shallowRef } from 'vue';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel?: string;
  /** Red confirm button for irreversible actions. */
  destructive?: boolean;
}

interface PendingConfirm {
  options: ConfirmOptions;
  resolve: (confirmed: boolean) => void;
}

/** The open confirmation, rendered by `AppConfirmHost`. One at a time: a new one cancels the last. */
export const pendingConfirm = shallowRef<PendingConfirm | null>(null);

export function requestConfirm(options: ConfirmOptions): Promise<boolean> {
  pendingConfirm.value?.resolve(false);
  return new Promise<boolean>((resolve) => {
    pendingConfirm.value = { options, resolve };
  });
}

export function settleConfirm(confirmed: boolean): void {
  const current = pendingConfirm.value;
  pendingConfirm.value = null;
  current?.resolve(confirmed);
}
