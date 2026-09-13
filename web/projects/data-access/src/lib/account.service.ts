import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from 'auth';
import { Observable } from 'rxjs';

/**
 * The signed-in person's own account, as opposed to the shop they run.
 *
 * Separate from `VendorService` because these calls belong to whoever is holding the token — they
 * work identically on the buyer, seller and admin surfaces.
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * Changes the password and **ends every session, including this one** — the API revokes all
   * refresh tokens and clears the cookie. Whatever calls this has to send the user back to sign in
   * rather than pretending they are still logged in.
   */
  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/v1/me/change-password`, {
      currentPassword,
      newPassword,
    });
  }
}
