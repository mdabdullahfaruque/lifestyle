/**
 * A deliberately loose check for "did this person mistype their address".
 *
 * Not a validator. Whether an address exists is something only sending to it can answer, and the
 * API validates properly on arrival — all this does is catch an obvious slip before a round trip.
 * Anything stricter starts rejecting addresses that are perfectly deliverable, which is worse than
 * letting a bad one through to a server that will say so.
 */
export function looksLikeEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(value.trim());
}
