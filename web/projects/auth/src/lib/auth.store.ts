import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL, SURFACE, Surface } from './surface';

export interface UserProfile {
  id: string;
  email: string;
  fullName: string;
  phoneNumber: string | null;
  emailVerified: boolean;
  twoFactorEnabled: boolean;
  permissions: string[];
  /** False for an account that has only ever signed in with Google — it can set one, not change one. */
  hasPassword: boolean;
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

  /**
   * The surface this session actually holds, which is not always the app's own.
   *
   * A seller who has not been approved yet cannot hold a seller token — they have no vendor, so
   * PermissionResolver refuses that surface. They still have to reach the application form, and
   * the vendor-application endpoints accept any signed-in token precisely so approval is
   * reachable. The console therefore signs such a user in on `buyer` and records it here, so the
   * shell can show onboarding instead of pretending they are a seller.
   */
  private readonly granted = signal<Surface | null>(null);

  private readonly token = signal<string | null>(null);
  private readonly profile = signal<UserProfile | null>(null);
  private readonly vendor = signal<string | null>(null);

  readonly user = this.profile.asReadonly();
  readonly vendorId = this.vendor.asReadonly();
  readonly isAuthenticated = computed(() => this.token() !== null);

  /** The surface actually granted. Null when signed out. */
  readonly activeSurface = this.granted.asReadonly();

  /** True when signed in, but not on this app's own surface — onboarding, in the seller console. */
  readonly isProvisional = computed(() => this.granted() !== null && this.granted() !== this.surface);
  readonly permissions = computed(() => new Set(this.profile()?.permissions ?? []));

  accessToken(): string | null {
    return this.token();
  }

  can(permission: string): boolean {
    return this.permissions().has(permission);
  }

  /**
   * `surface` overrides the app's own — used by the seller console to sign a not-yet-approved
   * applicant in on the buyer surface so they can reach the application form.
   */
  async login(
    email: string,
    password: string,
    totpCode?: string,
    vendorId?: string,
    surface?: Surface,
  ): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(
        `${this.baseUrl}/v1/auth/login`,
        { email, password, surface: surface ?? this.surface, totpCode, vendorId },
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
  async loginWithGoogle(idToken: string, vendorId?: string, surface?: Surface): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(
        `${this.baseUrl}/v1/auth/google`,
        { idToken, surface: surface ?? this.surface, vendorId },
        { withCredentials: true },
      ),
    );

    this.apply(response);
  }

  /**
   * Asks for a reset link. Resolves the same way whether or not the account exists — the server
   * answers 202 either way, deliberately, so this cannot be used to discover who has an account.
   * The caller must not imply otherwise in what it shows afterwards.
   */
  async forgotPassword(email: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`${this.baseUrl}/v1/auth/forgot-password`, { email, surface: this.surface }),
    );
  }

  /** Redeems a reset link. The token comes from the emailed URL, not from a session. */
  async resetPassword(token: string, newPassword: string): Promise<void> {
    await firstValueFrom(
      this.http.post(
        `${this.baseUrl}/v1/auth/reset-password`,
        { token, newPassword },
        { withCredentials: true },
      ),
    );
  }

  /**
   * Exchanges the refresh cookie for a fresh access token. Called on app start and by the
   * interceptor after a 401. Resolves to false when there is no valid session, which is the normal
   * state for a first-time visitor rather than an error.
   */
  async restore(): Promise<boolean> {
    if (await this.refreshAs(this.surface)) return true;

    // An applicant mid-onboarding holds a buyer session in the seller console. Without this second
    // attempt a page reload would sign them out of a form they are halfway through.
    if (this.surface === 'seller' && (await this.refreshAs('buyer'))) return true;

    this.clear();
    return false;
  }

  private async refreshAs(surface: Surface): Promise<boolean> {
    try {
      const response = await firstValueFrom(
        this.http.post<AuthResponse>(
          `${this.baseUrl}/v1/auth/refresh`,
          { surface },
          { withCredentials: true },
        ),
      );

      this.apply(response);
      return true;
    } catch {
      return false;
    }
  }

  /** Creates an account. Does not sign in — the caller decides which surface to ask for. */
  async register(email: string, password: string, fullName: string, phoneNumber?: string | null): Promise<void> {
    await firstValueFrom(
      this.http.post(
        `${this.baseUrl}/v1/auth/register`,
        { email, password, fullName, phoneNumber: phoneNumber || null },
        { withCredentials: true },
      ),
    );
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
    this.granted.set(response.surface as Surface);
    this.token.set(response.accessToken);
    this.profile.set(response.user);
    this.vendor.set(response.vendorId);
  }

  private clear(): void {
    this.granted.set(null);
    this.token.set(null);
    this.profile.set(null);
    this.vendor.set(null);
  }
}
