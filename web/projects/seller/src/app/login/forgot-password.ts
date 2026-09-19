import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthStore } from 'auth';
import { BrandLogo } from 'ui';
import { looksLikeEmail } from 'util';

@Component({
  selector: 'app-forgot-password',
  imports: [FormsModule, RouterLink, BrandLogo],
  templateUrl: './forgot-password.html',
  styleUrl: './login.scss',
})
export class ForgotPassword {
  private readonly auth = inject(AuthStore);

  protected email = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /**
   * Set once the request has been made. The wording deliberately does not confirm that an account
   * exists — the API answers identically either way so it cannot be used to discover who sells
   * here, and a message saying "we sent it" for a known address and "no such account" for an
   * unknown one would give away exactly what the endpoint is careful not to.
   */
  protected readonly sent = signal(false);

  protected async submit(): Promise<void> {
    if (this.busy()) return;

    const address = this.email.trim();
    if (!looksLikeEmail(address)) {
      this.error.set('That does not look like an email address.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.auth.forgotPassword(address);
      this.sent.set(true);
    } catch {
      // Even a failure here must not distinguish accounts. The only honest thing to report is
      // that the request itself did not get through.
      this.error.set('Could not send the email just now. Check your connection and try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
