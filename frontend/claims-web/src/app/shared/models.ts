// Hand-written mirrors of the API DTOs (backend/Claims.Api/Controllers/Dtos.cs). Enums arrive as strings.
export type Role = 'Claimant' | 'Officer' | 'Manager';
export type ClaimStatus = 'Submitted' | 'InReview' | 'AwaitingInfo' | 'Settled' | 'Rejected';
export type ClaimType = 'Motor' | 'Property';
export type Market = 'MY' | 'SG' | 'HK' | 'AU' | 'TH' | 'ID';
export type ClaimEventType =
  'Submitted' | 'Assigned' | 'InfoRequested' | 'InfoProvided' | 'ReserveSet' | 'Settled' | 'Rejected';

export interface ClaimSummary {
  id: string;
  type: ClaimType;
  market: Market;
  currency: string;
  status: ClaimStatus;
  reserve: number;
  settledAmount: number | null;
  claimantId: string;
  assignedOfficerId: string | null;
  submittedAt: string;
  closedAt: string | null;
  ageDays: number;
  isOverdue: boolean;
}

export interface TimelineItem {
  eventType: ClaimEventType;
  oldValue: string | null;
  newValue: string | null;
  note: string | null;
  actorId: string;
  at: string;
}

export interface ClaimDetail {
  claim: ClaimSummary;
  policyNumber: string;
  description: string;
  timeline: TimelineItem[];
}

export interface DevLoginResponse {
  token: string;
  expiresAt: string;
}

export interface ExposureRow {
  market: Market;
  currency: string;
  type: ClaimType;
  openCount: number;
  totalReserve: number;
  asOf: string;
}

export interface OfficerWorkload {
  officerId: string;
  name: string;
  open: number;
  awaitingInfo: number;
  overdue: number;
  oldestOpenDays: number | null;
  closedLast30Days: number;
  avgDaysToCloseLast30Days: number | null;
}

export interface Workload {
  asOf: string;
  unassigned: number;
  officers: OfficerWorkload[];
}
