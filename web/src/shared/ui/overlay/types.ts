export interface PopupMenuChoice {
  label: string;
  value: string;
}

export interface PopupMenuItem {
  label: string;
  action?: () => void;
  /** A non-interactive line (e.g. the signed-in role). */
  info?: boolean;
  /** Opens a submenu of mutually exclusive options (e.g. the language); `current` is marked. */
  choices?: {
    options: readonly PopupMenuChoice[];
    current: string;
    select: (value: string) => void;
  };
}
