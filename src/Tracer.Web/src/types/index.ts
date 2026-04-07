// Mirror of backend DTOs

export interface TracedField<T> {
  value: T;
  confidence: number;
  source: string;
  enrichedAt: string;
}

export interface Address {
  street: string;
  city: string;
  postalCode: string;
  region?: string;
  country: string;
  formattedAddress?: string;
}

export interface GeoCoordinate {
  latitude: number;
  longitude: number;
}

export interface EnrichedCompany {
  legalName?: TracedField<string>;
  tradeName?: TracedField<string>;
  taxId?: TracedField<string>;
  legalForm?: TracedField<string>;
  registeredAddress?: TracedField<Address>;
  operatingAddress?: TracedField<Address>;
  phone?: TracedField<string>;
  email?: TracedField<string>;
  website?: TracedField<string>;
  industry?: TracedField<string>;
  employeeRange?: TracedField<string>;
  entityStatus?: TracedField<string>;
  parentCompany?: TracedField<string>;
  location?: TracedField<GeoCoordinate>;
}

export interface TraceResult {
  traceId: string;
  status: TraceStatus;
  company?: EnrichedCompany;
  sources?: SourceResult[];
  overallConfidence?: number;
  createdAt: string;
  completedAt?: string;
  durationMs?: number;
  failureReason?: string;
}

export interface TraceRequest {
  companyName?: string;
  phone?: string;
  email?: string;
  website?: string;
  address?: string;
  city?: string;
  country?: string;
  registrationId?: string;
  taxId?: string;
  industryHint?: string;
  depth?: TraceDepth;
  callbackUrl?: string;
}

export interface CompanyProfile {
  id: string;
  normalizedKey: string;
  country: string;
  registrationId?: string;
  enriched?: EnrichedCompany;
  createdAt: string;
  lastEnrichedAt?: string;
  lastValidatedAt?: string;
  traceCount: number;
  overallConfidence?: number;
  isArchived: boolean;
}

export interface SourceResult {
  providerId: string;
  status: SourceStatus;
  fieldsEnriched: number;
  durationMs: number;
  errorMessage?: string;
}

export interface ChangeEvent {
  id: string;
  companyProfileId: string;
  field: FieldName;
  changeType: ChangeType;
  severity: ChangeSeverity;
  previousValueJson?: string;
  newValueJson?: string;
  detectedBy: string;
  detectedAt: string;
  isNotified: boolean;
}

export interface ValidationRecord {
  id: string;
  companyProfileId: string;
  validationType: ValidationType;
  fieldsChecked: number;
  fieldsChanged: number;
  providerId: string;
  durationMs: number;
  validatedAt: string;
}

export interface ProfileHistory {
  changes: PagedResult<ChangeEvent>;
  validations: ValidationRecord[];
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface ProfileDetail {
  profile: CompanyProfile;
  recentChanges: ChangeEvent[];
}

export interface DashboardStats {
  tracesToday: number;
  tracesThisWeek: number;
  totalProfiles: number;
  averageConfidence: number;
}

export interface ChangeStats {
  totalCount: number;
  criticalCount: number;
  majorCount: number;
  minorCount: number;
  cosmeticCount: number;
}

export type TraceStatus = 'Pending' | 'InProgress' | 'Completed' | 'PartiallyCompleted' | 'Failed' | 'Cancelled' | 'Queued';
export type TraceDepth = 'Quick' | 'Standard' | 'Deep';
export type SourceStatus = 'Unknown' | 'Success' | 'NotFound' | 'Error' | 'Timeout' | 'Skipped';
export type ChangeSeverity = 'Critical' | 'Major' | 'Minor' | 'Cosmetic';
export type ChangeType = 'Created' | 'Updated' | 'Deleted';
export type ValidationType = 'Lightweight' | 'Deep';
export type FieldName =
  | 'LegalName' | 'TradeName' | 'RegistrationId' | 'TaxId' | 'LegalForm'
  | 'RegisteredAddress' | 'OperatingAddress' | 'Phone' | 'Email' | 'Website'
  | 'Industry' | 'EmployeeRange' | 'EntityStatus' | 'ParentCompany' | 'Location' | 'Officers';

// SignalR push event payloads (mirror of backend SignalR hub messages)
export interface SourceCompletedEvent {
  traceId: string;
  source: SourceResult;
}

export interface TraceCompletedEvent {
  traceId: string;
  status: TraceStatus;
  overallConfidence?: number;
  durationMs?: number;
}

export interface ChangeDetectedEvent {
  companyProfileId: string;
  normalizedKey: string;
  field: string;
  severity: string;
}

export interface ValidationProgressEvent {
  profileId: string;
  message: string;
}
