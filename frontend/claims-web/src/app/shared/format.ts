import { SEED_USERS } from '../core/auth';
import { ClaimEventType, ClaimStatus, Market } from './models';

// Display words only; the API keeps the codes.
export const MARKET_NAMES: Record<Market, string> = {
  MY: 'Malaysia', SG: 'Singapore', HK: 'Hong Kong', AU: 'Australia', TH: 'Thailand', ID: 'Indonesia',
};

export const STATUS_LABELS: Record<ClaimStatus, string> = {
  Submitted: 'Submitted', InReview: 'In review', AwaitingInfo: 'Awaiting info', Settled: 'Settled', Rejected: 'Rejected',
};

// Pill tone per status: a pale tint with dark ink of the same hue (styles.css .pill-*).
export const STATUS_TONE: Record<ClaimStatus, 'neutral' | 'warning' | 'positive'> = {
  Submitted: 'neutral', InReview: 'neutral', AwaitingInfo: 'warning', Settled: 'positive', Rejected: 'neutral',
};

export const EVENT_LABELS: Record<ClaimEventType, string> = {
  Submitted: 'Submitted', Assigned: 'Picked up', InfoRequested: 'Asked for information',
  InfoProvided: 'Answered', ReserveSet: 'Reserve set', Settled: 'Settled', Rejected: 'Rejected',
};

// shortcut: names come from the seeded list (no users endpoint); unknown ids show as "someone else".
export function nameOf(id: string | null | undefined) {
  return SEED_USERS.find(u => u.id === id)?.name ?? 'someone else';
}

// A stable pale tint per person (identity, not data); neighbours in SEED_USERS sit at least 60° apart.
const HUES = [20, 200, 140, 40, 340, 100];
export function personHue(id: string | null | undefined) {
  const i = SEED_USERS.findIndex(u => u.id === id);
  return i < 0 ? 0 : HUES[i % HUES.length];
}

export function initialOf(id: string | null | undefined) {
  return nameOf(id).charAt(0).toUpperCase();
}
