import { localDateTimeToUtcIso, localDaysAgoInput } from '../../shared/date/utc';
import {
  AccountBalance,
  EventQuery,
  InboxStatus,
  LedgerQuery,
  ReasonGroup,
  MessageQuery,
  OutboxStatus,
  RuleFireQuery,
} from './customer.model';

/** D9: filter bars open on the last 30 days. */
export const DEFAULT_RANGE_DAYS = 30;

/** Activity filter form value — `datetime-local` strings in the admin's local time. */
export interface ActivityFilterValue {
  programId: string;
  accountTypeId: string;
  reasonGroup: ReasonGroup | '';
  from: string;
  to: string;
  eventId: string;
}

export interface EventFilterValue {
  eventType: string;
  status: InboxStatus | '';
  from: string;
  to: string;
}

export function defaultActivityFilter(now: Date = new Date()): ActivityFilterValue {
  return {
    programId: '',
    accountTypeId: '',
    reasonGroup: '',
    from: localDaysAgoInput(DEFAULT_RANGE_DAYS, now),
    to: '',
    eventId: '',
  };
}

export function defaultEventFilter(now: Date = new Date()): EventFilterValue {
  return { eventType: '', status: '', from: localDaysAgoInput(DEFAULT_RANGE_DAYS, now), to: '' };
}

/** Empty fields are left out; times go to the API as UTC (guardrails: UTC everywhere). */
export function toLedgerQuery(value: ActivityFilterValue): LedgerQuery {
  return {
    programId: value.programId || undefined,
    accountTypeId: value.accountTypeId || undefined,
    reasonGroup: value.reasonGroup || undefined,
    from: localDateTimeToUtcIso(value.from) ?? undefined,
    to: localDateTimeToUtcIso(value.to) ?? undefined,
    eventId: value.eventId.trim() || undefined,
  };
}

export function toEventQuery(value: EventFilterValue): EventQuery {
  return {
    eventType: value.eventType || undefined,
    status: value.status || undefined,
    from: localDateTimeToUtcIso(value.from) ?? undefined,
    to: localDateTimeToUtcIso(value.to) ?? undefined,
  };
}

export interface ProgramOption {
  id: string;
  name: string;
}

/** The customer's programs, from their balances (one entry per program). */
export function programOptions(balances: readonly AccountBalance[]): ProgramOption[] {
  const seen = new Map<string, string>();
  for (const b of balances) if (!seen.has(b.programId)) seen.set(b.programId, b.programName);
  return [...seen].map(([id, name]) => ({ id, name }));
}

/** The customer's wallets, narrowed to one program when one is chosen. */
export function walletOptions(
  balances: readonly AccountBalance[],
  programId: string,
): AccountBalance[] {
  return balances.filter((b) => !programId || b.programId === programId);
}

/** Amounts stay DecimalStrings; the sign is read from the text, never by parsing to a number. */
export function isDebit(amount: string): boolean {
  return amount.trim().startsWith('-');
}

/** "0", "0.0000", "-0" — read from the text, not parsed. */
export function isZeroAmount(amount: string): boolean {
  return /^-?0*(\.0*)?$/.test(amount.trim());
}

// ── CR 2026-10-02 P2: Rules & campaigns tab ──

export interface RuleFireFilterValue {
  from: string;
  to: string;
}

export function defaultRuleFireFilter(now: Date = new Date()): RuleFireFilterValue {
  return { from: localDaysAgoInput(DEFAULT_RANGE_DAYS, now), to: '' };
}

export function toRuleFireQuery(value: RuleFireFilterValue, ruleId?: string): RuleFireQuery {
  return {
    ruleId: ruleId || undefined,
    from: localDateTimeToUtcIso(value.from) ?? undefined,
    to: localDateTimeToUtcIso(value.to) ?? undefined,
  };
}

// ── CR 2026-10-02 P3: Messages sent tab ──

export interface MessageFilterValue {
  eventType: string;
  status: OutboxStatus | '';
  from: string;
  to: string;
}

export function defaultMessageFilter(now: Date = new Date()): MessageFilterValue {
  return { eventType: '', status: '', from: localDaysAgoInput(DEFAULT_RANGE_DAYS, now), to: '' };
}

export function toMessageQuery(value: MessageFilterValue): MessageQuery {
  return {
    eventType: value.eventType.trim() || undefined,
    status: value.status || undefined,
    from: localDateTimeToUtcIso(value.from) ?? undefined,
    to: localDateTimeToUtcIso(value.to) ?? undefined,
  };
}
