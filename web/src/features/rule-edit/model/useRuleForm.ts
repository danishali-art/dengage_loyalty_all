import { computed, ref, toValue, watch, type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { mapServerErrors, type FieldSetter } from '@/shared/lib/forms';
import { conditionsForSave, emptyTree, hasConditions, validateConditionTree } from '@/shared/lib/dsl';
import { useAppConfirm } from '@/shared/ui';
import { useAccountTypesQuery } from '@/entities/account-type';
import { useProgramQuery } from '@/entities/program';
import { DEFAULT_TRIGGER, useEventTypesQuery } from '@/entities/event-type';
import type { KnownField } from '@/entities/condition';
import {
  ALL_LIMIT_FIELDS,
  fieldVisibility,
  isConfigVisible,
  isLimitVisible,
  LIMIT_GROUPS,
  RULE_TYPES,
  ruleFormSchema,
  ruleFormValues,
  toRuleRequest,
  useCreateRuleMutation,
  useRuleQuery,
  useRulesMetadataQuery,
  useUpdateRuleMutation,
  usesOnBreach,
  usesPeriod,
  type CreatableRuleType,
  type LimitValues,
  type Rule,
  type RuleFormValues,
} from '@/entities/rule';
import { burnRuleDefaults, type BurnRuleDefaults } from './burn-rule-defaults';
import { buildTriggerOptions } from './trigger-options';

const CONFIG_KEYS = ['rounding', 'posting', 'expiryOverrideDays', 'reversible', 'notifyOnAward'];

/**
 * Create or edit a rule. `ruleId` null = a new rule. The form fills
 * in once the program's wallets (and the rule) have loaded and the event catalog and rules
 * metadata have answered — either may fail, in which case the form allows everything and leaves
 * the check to the API.
 */
export function useRuleForm(
  programId: MaybeRefOrGetter<string>,
  ruleId: MaybeRefOrGetter<string | null>,
  onSaved: (rule: Rule, created: boolean) => void,
) {
  const { t } = useI18n();
  const confirm = useAppConfirm();
  const accountTypes = useAccountTypesQuery(programId);
  const eventTypes = useEventTypesQuery();
  const metadataQuery = useRulesMetadataQuery(programId);
  const programQuery = useProgramQuery(programId);
  const existing = useRuleQuery(programId, ruleId);
  const isEdit = computed(() => !!toValue(ruleId));
  const formErrors = ref<string[]>([]);

  const { values, errors, setFieldValue, setValues, handleSubmit, resetForm, setErrors } =
    useForm<RuleFormValues>({
      validationSchema: toTypedSchema(ruleFormSchema()),
      initialValues: ruleFormValues(undefined, { trigger: DEFAULT_TRIGGER, targetAccountTypeId: null }),
    });
  const form = values as RuleFormValues;
  const create = useCreateRuleMutation(programId);
  const update = useUpdateRuleMutation(programId);

  const metadata = computed(() => metadataQuery.data.value ?? null);
  const settled = (q: { isPending: { value: boolean }; isError: { value: boolean } }): boolean =>
    !q.isPending.value || q.isError.value;
  const ready = computed(
    () =>
      !!accountTypes.data.value &&
      settled(eventTypes) &&
      settled(metadataQuery) &&
      (!isEdit.value || !!existing.data.value),
  );

  // ── What the chosen trigger and type allow (CR-03 metadata) ──
  const eventMeta = computed(() => metadata.value?.events.find((e) => e.eventType === form.trigger) ?? null);
  /** A tenant-defined trigger: nobody declared whether it carries an amount (D14). */
  const genericTrigger = computed(() => metadata.value !== null && eventMeta.value === null);
  // Unknown to the catalog (a tenant-approved generic type) — compatible with everything,
  // mirroring RuleTypeCatalog.IsCompatible's server-side fallback.
  const typeOptions = computed<readonly CreatableRuleType[]>(() => {
    const evt = eventMeta.value;
    return evt ? RULE_TYPES.filter((type) => evt.compatibleRuleTypes.includes(type)) : RULE_TYPES;
  });
  /** No rule type can be used with the chosen trigger (RuleTypeCatalog allows none). */
  const noRuleType = computed(() => typeOptions.value.length === 0);
  const ruleTypeMeta = computed(
    () => metadata.value?.ruleTypes.find((rt) => rt.ruleType === form.type) ?? null,
  );
  const target = computed(() => accountTypes.data.value?.find((a) => a.id === form.targetAccountTypeId));

  // ── CR 2026-10-06 Phase 2: the fields shown per trigger and type (rule-field-visibility.ts) ──
  /** Null = every field: an existing rule (D5) or a tenant-defined trigger (D7). */
  const visibility = computed(() => fieldVisibility(eventMeta.value, form.type, isEdit.value));
  function showConfig(key: string): boolean {
    // An expiry override only means something for points — cash never expires (§3.9).
    if (key === 'expiryOverrideDays' && !isEdit.value && target.value?.type === 'CASH') return false;
    return isConfigVisible(visibility.value, key);
  }
  const showLimit = (key: string): boolean => isLimitVisible(visibility.value, key);

  // ── Filling in ──
  /** The loaded rule's trigger, kept in the trigger list even if no rule type fits it any more. */
  const loadedTrigger = ref<string | null>(null);
  let filled = false;
  watch(
    ready,
    (isReady) => {
      if (!isReady || filled) return;
      filled = true;
      const rule = existing.data.value ?? undefined;
      loadedTrigger.value = rule?.trigger ?? null;
      resetForm({
        values: ruleFormValues(rule, {
          trigger: DEFAULT_TRIGGER,
          targetAccountTypeId: accountTypes.data.value?.[0]?.id ?? null,
        }),
      });
      applyWalletDefaults();
    },
    { immediate: true },
  );

  /** Fields the admin has typed in — wallet defaults never overwrite them. */
  const edited = new Set<string>();

  /**
   * CR 2026-10-05 (S4): on a new redeem / transfer rule, copy the chosen POINTS wallet's settings
   * into the calculation — once, and never over a field the admin has typed in. Editing a rule
   * never pre-fills: the rule owns its values.
   */
  function applyWalletDefaults(): void {
    if (!filled || isEdit.value) return;
    const defaults = burnRuleDefaults(form.type, target.value);
    const untouched = Object.fromEntries(
      Object.entries(defaults).filter(([key]) => !edited.has(key)),
    ) as BurnRuleDefaults;
    if (Object.keys(untouched).length) setValues(untouched, false);
  }

  // On a new rule, a field that stops applying (another trigger, type or target was picked) is
  // reset, so a value the admin can no longer see is never validated or sent.
  watch([visibility, () => target.value?.type], () => {
    if (!filled || isEdit.value) return;
    const reset: Partial<RuleFormValues> = {};
    const limitValues = form as LimitValues;
    for (const field of ALL_LIMIT_FIELDS)
      if (!showLimit(field.apiKey) && limitValues[field.control] !== '') reset[field.control] = '';
    if (!showLimit('period') && form.limitPeriod !== '') reset.limitPeriod = '';
    if (!showLimit('on_breach') && form.limitOnBreach !== 'Clamp') reset.limitOnBreach = 'Clamp';
    if (!showConfig('rounding') && form.cfgRounding !== '') reset.cfgRounding = '';
    if (!showConfig('posting') && form.cfgPosting !== 'Immediate') {
      reset.cfgPosting = 'Immediate';
      reset.cfgHoldDays = '';
    }
    if (!showConfig('expiryOverrideDays') && form.cfgExpiryOverrideDays !== '')
      reset.cfgExpiryOverrideDays = '';
    if (!showConfig('reversible') && !form.cfgReversible) reset.cfgReversible = true;
    if (!showConfig('notifyOnAward') && form.cfgNotifyOnAward) reset.cfgNotifyOnAward = false;
    if (Object.keys(reset).length) setValues(reset);
  });

  /**
   * A9 UI invariant: changing the trigger resets the condition tree (the field vocabulary changes
   * with the event). When conditions are already filled in, confirm first and keep the old
   * trigger on cancel. A new rule then moves to a type the trigger allows; with none, the form
   * shows the "no rule type" state instead of a hidden default type.
   */
  async function changeTrigger(next: string): Promise<void> {
    if (next === form.trigger) return;
    if (hasConditions(form.conditions)) {
      const confirmed = await confirm.ask({
        title: t('rules.form.changeTrigger.title'),
        message: t('rules.form.changeTrigger.message'),
        confirmLabel: t('rules.form.changeTrigger.confirm'),
        destructive: true,
      });
      if (!confirmed) return;
    }
    setFieldValue('trigger', next);
    setFieldValue('conditions', emptyTree());
    if (!isEdit.value) {
      const first = typeOptions.value[0];
      if (first !== undefined && !typeOptions.value.includes(form.type as CreatableRuleType))
        setFieldValue('type', first);
    }
    applyWalletDefaults();
  }

  // FieldSetter already ties `value` to `field`; VeeValidate's path types can't follow a generic key.
  const set: FieldSetter<RuleFormValues> = (field, value) => {
    if (field === 'trigger') {
      void changeTrigger(value as string);
      return;
    }
    edited.add(field);
    setFieldValue(field, value as never);
    if (field === 'type' || field === 'targetAccountTypeId') applyWalletDefaults();
  };

  const submit = handleSubmit(
    async (v) => {
      if (noRuleType.value) return;
      // CR 2026-10-05 item 4: the form starts with one blank condition, which means "no
      // conditions", not an invalid one.
      const conditions = conditionsForSave(v.conditions);
      const conditionErrors = validateConditionTree(conditions);
      if (conditionErrors.length) {
        formErrors.value = conditionErrors;
        return;
      }
      formErrors.value = [];
      try {
        const id = toValue(ruleId);
        const body = toRuleRequest(v, conditions, { showConfig, showLimit });
        const saved = id ? await update.mutateAsync({ id, body }) : await create.mutateAsync(body);
        onSaved(saved, !id);
      } catch (error) {
        const mapped = mapServerErrors(error, Object.keys(form), translate('common.genericError'));
        setErrors(mapped.fields);
        formErrors.value = mapped.form;
      }
    },
    () => {
      formErrors.value = [translate('rules.form.fixFields')];
    },
  );

  const accounts = computed(() => accountTypes.data.value ?? []);
  const view = computed(() => {
    const kinds = ruleTypeMeta.value?.validTargetAccountKinds ?? null;
    const evt = eventMeta.value;
    const values = form as LimitValues;
    return {
      triggerOptions: buildTriggerOptions(
        eventTypes.data.value ?? null,
        metadata.value,
        loadedTrigger.value,
      ).map((o) => ({ ...o, group: o.group === 'Other' ? t('rules.form.otherTriggers') : o.group })),
      typeOptions: typeOptions.value.map((type) => ({ value: type, label: type })),
      noRuleType: noRuleType.value,
      typeNote: ruleTypeMeta.value?.note ?? null,
      needsTargetAccount: form.type !== 'ReversalRule',
      targetAccountOptions: (kinds
        ? accounts.value.filter((a) => kinds.includes(a.type))
        : accounts.value
      ).map((a) => ({ value: a.id, label: `${a.name} (${a.type})` })),
      /** Redeem into: the program's CASH wallets. */
      cashWalletOptions: accounts.value
        .filter((a) => a.type === 'CASH')
        .map((a) => ({ value: a.id, label: a.name })),
      knownFields: (evt?.fields ?? []).map((f): KnownField => ({
        path: f.path,
        kind: f.kind.toLowerCase() as KnownField['kind'],
      })),
      genericTrigger: genericTrigger.value,
      /** CR 2026-10-06 Phase 4: the program's default rounding, shown on "Inherit from program". */
      programRounding: programQuery.data.value?.defaultRounding ?? null,
      /** CR 2026-10-06 Phase 5: the target wallet's own expiry, the override's placeholder. */
      walletExpirationDays:
        ((target.value?.config ?? null) as { expiration_days?: number | null } | null)?.expiration_days ??
        null,
      limitGroups: LIMIT_GROUPS.map((g) => ({
        ...g,
        fields: g.fields.filter((f) => showLimit(f.apiKey)),
      })).filter((g) => g.fields.length > 0),
      showPeriod: usesPeriod(values) && showLimit('period'),
      showOnBreach: usesOnBreach(values) && showLimit('on_breach'),
      config: Object.fromEntries(CONFIG_KEYS.map((k) => [k, showConfig(k)])) as Record<string, boolean>,
      anyConfig: CONFIG_KEYS.some((k) => showConfig(k)),
    };
  });

  return {
    isEdit,
    ready,
    loadFailed: computed(() => existing.isError.value),
    view,
    values: form,
    errors,
    set,
    formErrors,
    pending: computed(() => create.isPending.value || update.isPending.value),
    submit,
  };
}

export type RuleFormView = ReturnType<typeof useRuleForm>['view']['value'];
