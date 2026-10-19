import { requestConfirm, type ConfirmOptions } from './confirm';

export type { ConfirmOptions } from './confirm';

/** `await ask({...})` → true when confirmed (port of the Angular ConfirmService). Needs `AppConfirmHost`. */
export function useAppConfirm(): { ask(options: ConfirmOptions): Promise<boolean> } {
  return { ask: requestConfirm };
}
