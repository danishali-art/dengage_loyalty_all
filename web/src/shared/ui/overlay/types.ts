export interface PopupMenuItem {
  label: string;
  action?: () => void;
  /** A non-interactive line (e.g. the signed-in role). */
  info?: boolean;
}
