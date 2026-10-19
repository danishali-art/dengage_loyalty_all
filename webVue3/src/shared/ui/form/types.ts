export interface SelectOption<V> {
  label: string;
  value: V;
  /** Heading this option is listed under; adjacent options with the same group share one. */
  group?: string;
}

export interface RadioCardOption<V> {
  value: V;
  label: string;
  description?: string;
}
