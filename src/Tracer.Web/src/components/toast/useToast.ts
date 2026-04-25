import { useContext } from 'react';
import { ToastContext } from './ToastProvider';
import type { ToastApi } from './types';

/**
 * Access the toast API. Must be called inside `<ToastProvider>`.
 *
 * @example
 * const toast = useToast();
 * toast.push({ kind: 'success', title: 'Saved' });
 */
export function useToast(): ToastApi {
  const ctx = useContext(ToastContext);
  if (!ctx) {
    throw new Error('useToast must be used inside <ToastProvider>');
  }
  return ctx;
}
