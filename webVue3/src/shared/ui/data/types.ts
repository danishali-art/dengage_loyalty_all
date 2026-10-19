export interface TableColumn {
  /** Row property shown by default, and the `#cell-<key>` slot name. */
  key: string;
  header: string;
  align?: 'left' | 'right';
}

export type StatusTone = 'success' | 'neutral' | 'warning' | 'danger' | 'info' | 'streak';
