import type {
  TraceRequest,
  TraceResult,
  CompanyProfile,
  ProfileDetail,
  ProfileHistory,
  DashboardStats,
  PagedResult,
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

// Stats endpoints
export const statsApi = {
  dashboard: () => fetchApi<DashboardStats>('/stats'),
};
