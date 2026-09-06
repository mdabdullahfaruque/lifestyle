# nginx sites — the edge on a shared host

These are the **live** site files from `94.136.186.220`, kept here so a host rebuild does not lose
them. They replace `deploy/Caddyfile` on that host, which already runs nginx on 80/443 for three
other products — see [docs/07 §1](../../docs/07-DEPLOYMENT-DEVIATIONS.md).

| File | Serves |
|---|---|
| `api.mylifestylemart.com.conf` | the API — proxies to `127.0.0.1:5600`, blocks `/v1/internal/*` except health |
| `media.mylifestylemart.com.conf` | uploads from `/var/data/lifestyle/bd-prod/uploads`, refuses the `private/` prefix |
| `mylifestylemart.com.conf` | apex + www — holding page today, storefront proxy at Phase 2; also proxies `/v1/*` same-origin |

## Installing a change

```bash
# from the repo on the server, e.g. /home/deploy/lifestyle-bd-prod
sudo cp deploy/nginx/<name>.conf /etc/nginx/sites-available/<name>
sudo ln -sf /etc/nginx/sites-available/<name> /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
```

`nginx -t` before every reload, always: a failed test leaves the running config untouched, and this
host serves three other products' production traffic.

## Two things that will bite

- **nginx here is 1.24**, so `http2 on;` is an unknown directive — use `listen 443 ssl http2;`.
- **`X-Forwarded-For` is overwritten with `$remote_addr`**, not appended via
  `$proxy_add_x_forwarded_for` as the sibling sites do. The API clears `KnownProxies`
  (`EndpointRegistration.cs`) and trusts the header outright, so one clean value is required —
  this is what Caddy's `header_up X-Forwarded-For {client_ip}` produced. If these records are ever
  put behind Cloudflare's proxy, add `set_real_ip_from` for Cloudflare's ranges and
  `real_ip_header CF-Connecting-IP` in the same change.

Certificates are Let's Encrypt via certbot `--webroot -w /var/www/html`; renewal reloads nginx
through `/etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh`.
