/**
 * The app's UI kit: App* wrappers over the shadcn-vue primitives in `primitives/` (Reka UI +
 * Tailwind). The primitives, Reka UI and the icon set are imported ONLY inside this folder —
 * lint enforces it — so the rest of the app depends on these wrappers' props alone.
 */
export { default as AppButton } from './button/AppButton.vue';
export { default as AppIcon } from './icon/AppIcon.vue';
export type { IconName } from './icon/types';
export { default as AppInput } from './form/AppInput.vue';
export { default as AppTextarea } from './form/AppTextarea.vue';
export { default as AppSelect } from './form/AppSelect.vue';
export { default as AppSwitch } from './form/AppSwitch.vue';
export { default as AppFormField } from './form/AppFormField.vue';
export { default as AppFormErrors } from './form/AppFormErrors.vue';
export { default as AppCheckbox } from './form/AppCheckbox.vue';
export { default as AppRadioCards } from './form/AppRadioCards.vue';
export { default as AppInfoTip } from './form/AppInfoTip.vue';
export { default as AppFilterBar } from './form/AppFilterBar.vue';
export type { RadioCardOption, SelectOption } from './form/types';
export { default as AppDataTable } from './data/AppDataTable.vue';
export { default as AppPaginator } from './data/AppPaginator.vue';
export { default as AppStatusPill } from './data/AppStatusPill.vue';
export { default as AppEmptyState } from './data/AppEmptyState.vue';
export { default as AppSkeletonList } from './data/AppSkeletonList.vue';
export { default as AppMeter } from './data/AppMeter.vue';
export { meterPercent } from './data/meter';
export type { StatusTone, TableColumn } from './data/types';
export { default as AppPageHeader } from './layout/AppPageHeader.vue';
export { default as AppCard } from './layout/AppCard.vue';
export { default as AppTabs } from './layout/AppTabs.vue';
export type { Crumb } from './layout/types';
export { default as AppDialog } from './overlay/AppDialog.vue';
export { default as AppPopupMenu } from './overlay/AppPopupMenu.vue';
export { default as AppDrawer } from './overlay/AppDrawer.vue';
export type { PopupMenuChoice, PopupMenuItem } from './overlay/types';
export { default as AppToastHost } from './feedback/AppToastHost.vue';
export { default as AppConfirmHost } from './feedback/AppConfirmHost.vue';
export { default as AppProgressBar } from './feedback/AppProgressBar.vue';
export { notify, onToast, type ToastKind, type ToastRequest } from './feedback/toast';
export { useAppConfirm, type ConfirmOptions } from './feedback/useAppConfirm';
