import { useState } from 'react';
import { useNavigate } from 'react-router';
import { useMutation } from '@tanstack/react-query';
import { traceApi } from '../api/client';
import { useToast } from '../components/toast/useToast';
import { ErrorMessage } from '../components/ErrorMessage';
import type { TraceRequest, TraceDepth } from '../types';

const COUNTRIES = [
  { code: '', label: 'Select country...' },
  { code: 'CZ', label: 'Czech Republic (CZ)' },
  { code: 'SK', label: 'Slovakia (SK)' },
  { code: 'DE', label: 'Germany (DE)' },
  { code: 'AT', label: 'Austria (AT)' },
  { code: 'GB', label: 'United Kingdom (GB)' },
  { code: 'US', label: 'United States (US)' },
  { code: 'PL', label: 'Poland (PL)' },
  { code: 'FR', label: 'France (FR)' },
  { code: 'NL', label: 'Netherlands (NL)' },
  { code: 'AU', label: 'Australia (AU)' },
];

const DEPTH_OPTIONS: { value: TraceDepth; label: string; description: string }[] = [
  { value: 'Quick', label: 'Quick', description: 'Cache + fastest APIs (~5s)' },
  { value: 'Standard', label: 'Standard', description: 'Full waterfall (~10s)' },
  { value: 'Deep', label: 'Deep', description: 'All sources + AI (~30s)' },
];

export function NewTracePage() {
  const navigate = useNavigate();
  const toast = useToast();
  const [form, setForm] = useState<TraceRequest>({
    companyName: '',
    country: 'CZ',
    depth: 'Standard',
  });

  const mutation = useMutation({
    mutationFn: (input: TraceRequest) => traceApi.submit(input),
    onSuccess: (result) => {
      toast.push({
        kind: 'info',
        title: 'Trace submitted',
        description: 'Enrichment is running in the background.',
      });
      navigate(`/traces/${result.traceId}`);
    },
    onError: (err) => {
      toast.push({
        kind: 'error',
        title: 'Could not submit trace',
        description: err instanceof Error ? err.message : undefined,
      });
    },
  });

  const handleChange = (field: keyof TraceRequest, value: string) => {
    setForm((prev) => ({ ...prev, [field]: value || undefined }));
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    mutation.mutate(form);
  };

  const hasIdentifyingField =
    !!form.companyName || !!form.registrationId || !!form.taxId ||
    !!form.phone || !!form.email || !!form.website;

  return (
    <div className="max-w-2xl">
      <h2 className="text-2xl font-bold text-gray-900 mb-6">New Trace Request</h2>

      <form onSubmit={handleSubmit} className="space-y-6">
        {/* Company identification */}
        <div className="bg-white rounded-lg shadow p-6 space-y-4">
          <h3 className="text-lg font-semibold text-gray-900">Company Identification</h3>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Company Name</label>
              <input
                type="text"
                value={form.companyName ?? ''}
                onChange={(e) => handleChange('companyName', e.target.value)}
                placeholder="e.g. Skoda Auto"
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Country</label>
              <select
                value={form.country ?? ''}
                onChange={(e) => handleChange('country', e.target.value)}
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                {COUNTRIES.map((c) => (
                  <option key={c.code} value={c.code}>{c.label}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Registration ID</label>
              <input
                type="text"
                value={form.registrationId ?? ''}
                onChange={(e) => handleChange('registrationId', e.target.value)}
                placeholder="e.g. 00027006"
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Tax ID</label>
              <input
                type="text"
                value={form.taxId ?? ''}
                onChange={(e) => handleChange('taxId', e.target.value)}
                placeholder="e.g. CZ00027006"
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
          </div>
        </div>

        {/* Contact info */}
        <div className="bg-white rounded-lg shadow p-6 space-y-4">
          <h3 className="text-lg font-semibold text-gray-900">Contact Information</h3>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Phone</label>
              <input
                type="text"
                value={form.phone ?? ''}
                onChange={(e) => handleChange('phone', e.target.value)}
                placeholder="+420..."
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Email</label>
              <input
                type="email"
                value={form.email ?? ''}
                onChange={(e) => handleChange('email', e.target.value)}
                placeholder="info@company.cz"
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Website</label>
              <input
                type="url"
                value={form.website ?? ''}
                onChange={(e) => handleChange('website', e.target.value)}
                placeholder="https://..."
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Industry Hint</label>
              <input
                type="text"
                value={form.industryHint ?? ''}
                onChange={(e) => handleChange('industryHint', e.target.value)}
                placeholder="e.g. Automotive"
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
          </div>
        </div>

        {/* Address */}
        <div className="bg-white rounded-lg shadow p-6 space-y-4">
          <h3 className="text-lg font-semibold text-gray-900">Address</h3>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div className="md:col-span-2">
              <label className="block text-sm font-medium text-gray-700 mb-1">Street Address</label>
              <input
                type="text"
                value={form.address ?? ''}
                onChange={(e) => handleChange('address', e.target.value)}
                placeholder="e.g. tř. Václava Klementa 869"
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">City</label>
              <input
                type="text"
                value={form.city ?? ''}
                onChange={(e) => handleChange('city', e.target.value)}
                placeholder="e.g. Mladá Boleslav"
                className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
          </div>
        </div>

        {/* Depth selector */}
        <div className="bg-white rounded-lg shadow p-6">
          <h3 className="text-lg font-semibold text-gray-900 mb-3">Enrichment Depth</h3>
          <div className="flex gap-4">
            {DEPTH_OPTIONS.map((opt) => (
              <label
                key={opt.value}
                className={`flex-1 p-3 border-2 rounded-lg cursor-pointer transition-colors ${
                  form.depth === opt.value
                    ? 'border-blue-500 bg-blue-50'
                    : 'border-gray-200 hover:border-gray-300'
                }`}
              >
                <input
                  type="radio"
                  name="depth"
                  value={opt.value}
                  checked={form.depth === opt.value}
                  onChange={(e) => handleChange('depth', e.target.value)}
                  className="sr-only"
                />
                <div className="text-sm font-medium text-gray-900">{opt.label}</div>
                <div className="text-xs text-gray-500 mt-1">{opt.description}</div>
              </label>
            ))}
          </div>
        </div>

        {/* Submit */}
        {mutation.isError && (
          <ErrorMessage title="Submission failed" error={mutation.error} />
        )}

        <button
          type="submit"
          disabled={!hasIdentifyingField || mutation.isPending}
          aria-busy={mutation.isPending}
          className="w-full py-3 bg-blue-600 text-white rounded-lg font-medium hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          {mutation.isPending ? 'Submitting…' : 'Submit Trace Request'}
        </button>

        <p className="sr-only" aria-live="polite">
          {mutation.isPending ? 'Submitting trace request.' : ''}
          {mutation.isSuccess ? 'Trace submitted successfully.' : ''}
        </p>

        {!hasIdentifyingField && (
          <p className="text-sm text-amber-600 text-center">
            At least one identifying field is required (company name, registration ID, tax ID, phone, email, or website).
          </p>
        )}
      </form>
    </div>
  );
}
