// CR-05: the API's AdditionalConditions moved from the flat ConditionClause {field, op, value}
// shape to the grouped DSL's ConditionLeaf {field, operator, value:{type,data,...}} shape when
// CardBucketConditionMapper was rewritten onto ConditionTree — a card bucket is stored as one
// FixedBonusRule row's single-AND-group ConditionTree. Card Buckets never expose the AND/OR
// grouping UI itself (that's Rules-only), just this flat escape-hatch list of extra leaves.
import { ConditionLeaf } from '../../../shared/forms/condition-tree-dsl';

export type CountryMode = 'in' | 'not_in';

export interface CardBucket {
  id: string;
  name: string;
  mccCodes: string[] | null;
  amountMin: string | null;
  amountMax: string | null;
  countryMode: CountryMode | null;
  countries: string[] | null;
  requireCaptured: boolean;
  hourFrom: number | null;
  hourTo: number | null;
  daysOfWeek: string[] | null;
  perCustomerPerDay: string | null;
  perCustomerTotal: string | null;
  targetAccountTypeId: string;
  rewardAmount: string;
  priority: number;
  activeFrom: string | null;
  activeTo: string | null;
  additionalConditions: ConditionLeaf[] | null;
  status: 'active' | 'disabled' | 'deleted';
  createdAt: string;
  updatedAt: string;
}

export interface CreateCardBucketRequest {
  name: string;
  mccCodes: string[] | null;
  amountMin: string | null;
  amountMax: string | null;
  countryMode: CountryMode | null;
  countries: string[] | null;
  requireCaptured: boolean;
  hourFrom: number | null;
  hourTo: number | null;
  daysOfWeek: string[] | null;
  perCustomerPerDay: string | null;
  perCustomerTotal: string | null;
  targetAccountTypeId: string;
  rewardAmount: string;
  priority: number;
  activeFrom: string | null;
  activeTo: string | null;
  additionalConditions: ConditionLeaf[] | null;
}

export type UpdateCardBucketRequest = Partial<CreateCardBucketRequest>;

export interface CardBucketListFilter {
  status?: string;
}
