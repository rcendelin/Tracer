export type ToastKind = 'info' | 'success' | 'warning' | 'error';

export interface Toast {
  id: string;
  kind: ToastKind;
  title: string;
  description?: string;
  /** Auto-dismiss delay in milliseconds; 0 means sticky. Default 5000. */
  durationMs?: number;
}

export type ToastInput = Omit<Toast, 'id'>;

export interface ToastApi {
  push: (toast: ToastInput) => string;
  dismiss: (id: string) => void;
  dismissAll: () => void;
}
