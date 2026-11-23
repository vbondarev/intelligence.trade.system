// Browser видит только публичное состояние session; tokens остаются на стороне BFF.
export interface SessionUser {
  userId: string;
  subject: string;
}

export type BrowserSession =
  | { authenticated: false }
  | { authenticated: true; user: SessionUser };

export interface AuthState {
  loading: boolean;
  authenticated: boolean;
  user: SessionUser | null;
  error: string | null;
  logoutAvailable: boolean;
  sessionEnded?: boolean;
  sessionExpired?: boolean;
  sessionTraceId?: string | null;
}

export interface SessionReadContext {
  isCurrent: () => boolean;
  endSession: (traceId?: string | null) => void;
}

export interface AuthContextValue extends AuthState {
  login: () => void;
  logout: () => Promise<void>;
  retry: () => void;
  revalidate: (expectedUserId: string, signal: AbortSignal) => Promise<boolean>;
  captureTimingCheck: () => () => boolean;
  expireSession: () => void;
  restoreSession: (signal: AbortSignal) => Promise<void>;
  captureSessionRead: () => SessionReadContext;
  captureSessionEnd: () => (traceId?: string | null) => void;
}
