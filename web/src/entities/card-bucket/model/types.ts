import type { ApiSchema } from '@/shared/api';

/**
 * A card bucket is stored as one FixedBonusRule whose conditions are a single AND group
 * (CardBucketConditionMapper.cs); the API exposes it as structured fields plus
 * `additionalConditions`, a flat list of extra grouped-DSL leaves.
 */
export type CardBucket = ApiSchema<'CardBucketResponse'>;
export type CreateCardBucketRequest = ApiSchema<'CreateCardBucketRequest'>;
export type UpdateCardBucketRequest = ApiSchema<'UpdateCardBucketRequest'>;
export type CountryMode = NonNullable<CardBucket['countryMode']>;
export type CardBucketStatus = CardBucket['status'];

export const DAYS_OF_WEEK = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
] as const;
export type DayOfWeek = (typeof DAYS_OF_WEEK)[number];

/** CardBucketsValidators: name ≤ 255. */
export const CARD_BUCKET_NAME_MAX = 255;

/** The priority every bucket is saved with; buckets have no priority field of their own. */
export const CARD_BUCKET_PRIORITY = 100;
