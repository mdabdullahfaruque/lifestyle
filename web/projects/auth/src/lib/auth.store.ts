import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL, SURFACE } from './surface';

export interface UserProfile {
  id: string;
  email: string;
  fullName: string;
  phoneNumber: string | null;
  emailVerified: boolean;
  twoFactorEnabled: boolean;
  permissions: string[];
}

export interface AuthResponse {
  accessToken: string;
  expiresInSeconds: number;
  surface: string;
  vendorId: string | null;
  user: UserProfile;
}

/**
 * Holds the session.
 *
 * The access token lives in memory only and the refresh token is an HttpOnly cookie the browser
 * sends on its own (FRD §4.2). Nothing is written to `localStorage`: a token readable by JavaScript
 * is a token an XSS bug can exfiltrate. The cost is that a page reload starts with no access token,
 * which `restore()` fixes by exchanging the cookie on app start.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly surface = inject(SURFACE);

  private readonly token = signal<string | null>(null);
  private readonly profile = signal<UserProfile | null>(null);
  private readonly vendor = signal<string | null>(null);

  readonly user = this.profile.asReadonly();
  readonly vendorId = this.vendor.asReadonly();
  readonly isAuthenticated = computed(() => this.token() !== null);
  readonly permissions = computed(() => new Set(this.profile()?.permissions ?? []));

  accessToken(): string | null {
    return this.token();
  }

  can(permission: string): boolean {
    return this.permissions().has(permission);
  }

  async login(email: string, password: string, totpCode?: string, vendorId?: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(
        `${this.baseUrl}/v1/auth/login`,
        { email, password, surface: this.surface, totpCode, vendorId },
        { withCredentials: true },
      ),
    );

    this.apply(response);
  }

  /**
   * Exchanges a Google ID token for a session.
   *
   * The browser never sees our own credentials here: Google Identity Services hands the page a
   * signed ID token, and the server verifies it against Google's public keys. The admin surface
   * refuses this endpoint outright, so an admin console calling it gets a 403 by design.
   */
  async loginWithGoogle(idToken: string, vendorId?: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(
        `${this.baseUrl}/v1/auth/google`,
        { idToken, surface: this.surface, vendorId },
        { withCredentials: true },
      ),
    );

    this.apply(response);
  }

  /**
   * Exchanges the refresh cookie for a fresh access token. Called on app start and by the
   * interceptor after a 401. Resolves to false when there is no valid session, which is the normal
   * state for a first-time visitor rather than an error.
   */
  async restore(): Promise<boolean> {
    try {
      const response = await firstValueFrom(
        this.http.post<AuthResponse>(
          `${this.baseUrl}/v1/auth/refresh`,
          { surface: this.surface },
          { withCredentials: true },
        ),
      );

      this.apply(response);
      return true;
    } catch {
      this.clear();
      return false;
    }
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(
        this.http.post(`${this.baseUrl}/v1/auth/logout`, {}, { withCredentials: true }),
      );
    } finally {
      // Clear locally even if the call failed — the user asked to be signed out.
      this.clear();
    }
  }

  private apply(response: AuthResponse): void {
    this.token.set(response.accessToken);
    this.profile.set(response.user);
    this.vendor.set(response.vendorId);
  }

  private clear(): void {
    this.token.set(null);
    this.profile.set(null);
    this.vendor.set(null);
  }
}
