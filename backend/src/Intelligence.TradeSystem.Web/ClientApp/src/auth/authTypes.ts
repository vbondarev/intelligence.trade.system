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
}

export interface AuthContextValue extends AuthState {
  login: () => void;
  logout: () => Promise<void>;
  retry: () => void;
}
