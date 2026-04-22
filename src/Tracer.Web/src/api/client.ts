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
