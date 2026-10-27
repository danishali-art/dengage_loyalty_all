/**
 * How a large presentational form reports edits: `set('name', value)`. The composable wires it to
 * VeeValidate's `setFieldValue`, so the component stays free of form state.
 */
export type FieldSetter<T> = <K extends keyof T & string>(field: K, value: T[K]) => void;
