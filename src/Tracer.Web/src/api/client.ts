import type {
  TraceRequest,
  TraceResult,
  CompanyProfile,
  ProfileDetail,
  ProfileHistory,
  DashboardStats,
  PagedResult,
  ChangeEvent,
  ChangeStats,
  ChangeSeverity,
  ValidationStats,
  ValidationQueueItem,
  ChangeTrend,
  Coverage,
  TrendPeriod,
  CoverageGroupBy,
} from '../types';

const BASE_URL = '/api';

async function fetchApi<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...options?.headers,
    },
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(response.status, problem?.title ?? response.statusText, problem);
  }

  return response.json();
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: unknown;

  constructor(status: number, message: string, problem?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

// Trace endpoints
export const traceApi = {
  submit: (input: TraceRequest) =>
    fetchApi<TraceResult>('/trace', {
      method: 'POST',
      body: JSON.stringify(input),
    }),

  get: (traceId: string) =>
    fetchApi<TraceResult>(`/trace/${traceId}`),

  list: (params: {
    page?: number;
    pageSize?: number;
    status?: string;
    search?: string;
  } = {}) => {
    const query = new URLSearchParams();
    if (params.page !== undefined) query.set('page', String(params.page));
    if (params.pageSize !== undefined) query.set('pageSize', String(params.pageSize));
    if (params.status) query.set('status', params.status);
    if (params.search) query.set('search', params.search);
    return fetchApi<PagedResult<TraceResult>>(`/trace?${query}`);
  },
};

export type ExportFormat = 'csv' | 'xlsx';

/**
 * Fetches a binary payload from an API export endpoint and triggers a browser
 * download via a transient anchor element. Centralised so both
 * ProfilesPage and ChangeFeedPage share the same auth / error handling.
 *
 * The server sets Content-Disposition with the filename; we honour it when
 * present, otherwise we fall back to the caller-supplied default.
 */
export async function downloadExport(
  path: string,
  fallbackFileName: string,
): Promise<void> {
  const response = await fetch(`${BASE_URL}${path}`, {
    method: 'GET',
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(
      response.status,
      problem?.title ?? response.statusText,
      problem,
    );
  }

  // Prefer the server-provided filename, else use the caller fallback.
  const disposition = response.headers.get('content-disposition') ?? '';
  const match = /filename\s*=\s*"?([^";]+)"?/i.exec(disposition);
  const fileName = match?.[1] ?? fallbackFileName;

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
  } finally {
    URL.revokeObjectURL(url);
  }
}

function buildProfileExportQuery(params: {
  search?: string;
  country?: string;
  minConfidence?: number;
  maxConfidence?: number;
  validatedBefore?: string;
  includeArchived?: boolean;
  maxRows?: number;
}): URLSearchParams {
  const query = new URLSearchParams();
  if (params.search) query.set('search', params.search);
  if (params.country) query.set('country', params.country);
  if (params.minConfidence !== undefined) query.set('minConfidence', String(params.minConfidence));
  if (params.maxConfidence !== undefined) query.set('maxConfidence', String(params.maxConfidence));
  if (params.validatedBefore) query.set('validatedBefore', params.validatedBefore);
  if (params.includeArchived) query.set('includeArchived', 'true');
  if (params.maxRows !== undefined) query.set('maxRows', String(params.maxRows));
  return query;
}

function buildChangeExportQuery(params: {
  severity?: ChangeSeverity;
  profileId?: string;
  from?: string;
  to?: string;
  maxRows?: number;
}): URLSearchParams {
  const query = new URLSearchParams();
  if (params.severity) query.set('severity', params.severity);
  if (params.profileId) query.set('profileId', params.profileId);
  if (params.from) query.set('from', params.from);
  if (params.to) query.set('to', params.to);
  if (params.maxRows !== undefined) query.set('maxRows', String(params.maxRows));
  return query;
}

// Profile endpoints
export const profileApi = {
  list: (params: {
    page?: number;
    pageSize?: number;
    search?: string;
    country?: string;
    minConfidence?: number;
    maxConfidence?: number;
    validatedBefore?: string;
    includeArchived?: boolean;
  } = {}) => {
    const query = new URLSearchParams();
    if (params.page !== undefined) query.set('page', String(params.page));
    if (params.pageSize !== undefined) query.set('pageSize', String(params.pageSize));
    if (params.search) query.set('search', params.search);
    if (params.country) query.set('country', params.country);
    if (params.minConfidence !== undefined) query.set('minConfidence', String(params.minConfidence));
    if (params.maxConfidence !== undefined) query.set('maxConfidence', String(params.maxConfidence));
    if (params.validatedBefore) query.set('validatedBefore', params.validatedBefore);
    if (params.includeArchived) query.set('includeArchived', 'true');
    return fetchApi<PagedResult<CompanyProfile>>(`/profiles?${query}`);
  },

  get: (profileId: string) =>
    fetchApi<ProfileDetail>(`/profiles/${profileId}`),

  history: (profileId: string, page = 0, pageSize = 20) =>
    fetchApi<ProfileHistory>(
      `/profiles/${profileId}/history?page=${page}&pageSize=${pageSize}`,
    ),

  revalidate: (profileId: string) =>
    fetchApi<string>(`/profiles/${profileId}/revalidate`, { method: 'POST' }),

  /**
   * Downloads the current profiles view as CSV or XLSX. Query filters match
   * the /api/profiles list endpoint; the server caps at 10 000 rows.
   */
  export: (
    format: ExportFormat,
    params: {
      search?: string;
      country?: string;
      minConfidence?: number;
      maxConfidence?: number;
      validatedBefore?: string;
      includeArchived?: boolean;
      maxRows?: number;
    } = {},
  ) => {
    const query = buildProfileExportQuery(params);
    query.set('format', format);
    const fallback = `tracer-profiles.${format}`;
    return downloadExport(`/profiles/export?${query}`, fallback);
  },
};

// Changes endpoints
export const changesApi = {
  list: (params: {
    page?: number;
    pageSize?: number;
    severity?: ChangeSeverity;
    profileId?: string;
  } = {}) => {
    const query = new URLSearchParams();
    if (params.page !== undefined) query.set('page', String(params.page));
    if (params.pageSize !== undefined) query.set('pageSize', String(params.pageSize));
    if (params.severity) query.set('severity', params.severity);
    if (params.profileId) query.set('profileId', params.profileId);
    return fetchApi<PagedResult<ChangeEvent>>(`/changes?${query}`);
  },

  stats: () => fetchApi<ChangeStats>('/changes/stats'),

  /**
   * Downloads the current change feed view as CSV or XLSX.
   */
  export: (
    format: ExportFormat,
    params: {
      severity?: ChangeSeverity;
      profileId?: string;
      from?: string;
      to?: string;
      maxRows?: number;
    } = {},
  ) => {
    const query = buildChangeExportQuery(params);
    query.set('format', format);
    const fallback = `tracer-changes.${format}`;
    return downloadExport(`/changes/export?${query}`, fallback);
  },
};

// Stats endpoints
export const statsApi = {
  dashboard: (): Promise<DashboardStats> => fetchApi<DashboardStats>('/stats'),
};

// Analytics endpoints — aggregate-only responses, safe for dashboards.
export const analyticsApi = {
  changes: (params: { period?: TrendPeriod; months?: number } = {}): Promise<ChangeTrend> => {
    const query = new URLSearchParams();
    if (params.period) query.set('period', params.period);
    if (params.months !== undefined) query.set('months', String(params.months));
    return fetchApi<ChangeTrend>(`/analytics/changes?${query}`);
  },

  coverage: (params: { groupBy?: CoverageGroupBy } = {}): Promise<Coverage> => {
    const query = new URLSearchParams();
    if (params.groupBy) query.set('groupBy', params.groupBy);
    return fetchApi<Coverage>(`/analytics/coverage?${query}`);
  },
};

// Validation endpoints — re-validation engine dashboard (B-71)
export const validationApi = {
  stats: () => fetchApi<ValidationStats>('/validation/stats'),

  queue: (params: { page?: number; pageSize?: number } = {}) => {
    const query = new URLSearchParams();
    if (params.page !== undefined) query.set('page', String(params.page));
    if (params.pageSize !== undefined) query.set('pageSize', String(params.pageSize));
    return fetchApi<PagedResult<ValidationQueueItem>>(`/validation/queue?${query}`);
  },

  revalidate: (profileId: string) =>
    fetchApi<string>(`/profiles/${profileId}/revalidate`, { method: 'POST' }),
};
