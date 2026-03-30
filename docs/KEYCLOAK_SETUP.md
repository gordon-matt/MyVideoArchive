# Keycloak Setup Guide for MyVideoArchive

This guide walks you through configuring Keycloak so that MyVideoArchive (and any other apps
on your home NAS) can delegate authentication to a single, centralised identity provider.

Once configured you will be able to:

- Log in to MyVideoArchive using your Keycloak credentials.
- Reuse the same account across all apps that are connected to the same Keycloak realm.
- Manage users, passwords, and roles entirely inside Keycloak — no need to touch each app.

---

## Overview

The flow looks like this:

```
Browser → MyVideoArchive → redirects to Keycloak login page
                         ← Keycloak issues a token after successful login
         MyVideoArchive reads the token → user is authenticated
```

MyVideoArchive supports two roles:

| Role            | Access                                                              |
|-----------------|---------------------------------------------------------------------|
| `User`          | Can browse and manage their own video archive.                      |
| `Administrator` | Full access including the Admin panel (tools, tags, failed videos). |

---

## Prerequisites

- Keycloak is running and accessible (default: `http://localhost:8081`).
- If you are using the provided `docker-compose.yml`, uncomment the `keycloak` service and
  re-run `docker compose up -d`.
- **Keycloak database:** When Keycloak is configured to use PostgreSQL (as in the
  commented `keycloak` service), the `keycloak` database must exist. Create the
  database, then restart Keycloak:
  ```bash
  docker exec -it <postgres-container-name> psql -U postgres -c "CREATE DATABASE keycloak;"
  ```

---

## Step 1 — Log in to the Keycloak Admin Console

1. Open `http://localhost:8081` in your browser.
2. Click **Administration Console**.
3. Log in with the bootstrap admin credentials (default: `admin` / `admin`).  
   Change these in `docker-compose.yml` via `KC_BOOTSTRAP_ADMIN_PASSWORD` before
   deploying to a production environment.

---

## Step 2 — Create a Realm

A *realm* is an isolated namespace for users, roles, and clients. Create one per
group of related apps.

1. In the top-left dropdown (showing **master**), click **Create realm**.
2. Set **Realm name** to `myvideoarchive` (you may use any name; just keep it
   consistent with the `Authority` URL in your app configuration).
3. Leave **Enabled** toggled on.
4. Click **Create**.

You are now working inside the `myvideoarchive` realm.

---

## Step 3 — Create Application Roles

1. In the left sidebar, go to **Realm roles**.
2. Click **Create role**.
3. Set **Role name** to `Administrator` (exact case matters — it must match the
   value in `MyVideoArchive.Constants.Roles`).
4. Click **Save**.
5. Repeat to create a second role named `User`.

---

## Step 4 — Create a Client

A *client* represents one application that authenticates through Keycloak.

1. In the left sidebar, go to **Clients**.
2. Click **Create client**.

### General settings

| Field         | Value           |
|---------------|-----------------|
| Client type   | OpenID Connect  |
| Client ID     | `myvideoarchive`|

Click **Next**.

### Capability config

| Field                   | Value      |
|-------------------------|------------|
| Client authentication   | **On**     |
| Authorization           | Off        |
| Standard flow           | **On**     |
| Direct access grants    | Off        |
| Service account roles    | **On**    |

Click **Next**.

### Login settings

| Field                        | Value                                      |
|------------------------------|--------------------------------------------|
| Root URL                     | `http://localhost:8080`                    |
| Home URL                     | `http://localhost:8080`                    |
| Valid redirect URIs          | `http://localhost:8080/signin-oidc`        |
| Valid post logout redirect URIs | `http://localhost:8080/signout-callback-oidc` |
| Web origins                  | `http://localhost:8080`                    |

> **Redirect URIs:** Use the exact values above. The login callback is
> `http://localhost:8080/signin-oidc`. The **post-logout** redirect must be
> `http://localhost:8080/signout-callback-oidc` — the OIDC middleware uses this
> path to receive the redirect from Keycloak and complete sign-out (clear cookies),
> then redirects the user to the app root. If this URI is missing in Keycloak, you
> get "Invalid redirect uri" after clicking Logout.
>
> **Production / NAS (behind a reverse proxy):**
> Use your **public HTTPS URLs** everywhere:
> - Keycloak client — Root URL, redirect URIs, post-logout URIs, Web origins: `https://mva.example.com/...`
> - App config — `Authority`: `https://keycloak.example.com/realms/myvideoarchive`
>
> **Why HTTPS?** Keycloak posts back to `/signin-oidc` as a cross-site request. Browsers only
> send the OIDC correlation cookie on cross-site POSTs when the cookie has `SameSite=None`
> and `Secure`. So the app must be reached over HTTPS (reverse proxy terminating TLS).
>
> **Separate Docker stacks (app and Keycloak on the same host):** When the callback fires,
> the app makes a server-to-server (backchannel) token exchange call to Keycloak's token
> endpoint (`https://keycloak.example.com/...`). Docker containers cannot normally resolve
> the host's own public hostname (DNS/hairpin NAT issue), causing a 502.
>
> **Fix (backchannel):** if the app and Keycloak share a Docker network (recommended), set
> `BackchannelBaseUrl` to the internal Keycloak URL:
> ```yaml
> Authentication__Keycloak__BackchannelBaseUrl: http://keycloak:8080
> ```
> The app will call Keycloak directly over the Docker network for all server-to-server
> calls (discovery, token exchange). `Authority` stays as the public URL so the browser
> still sees the correct Keycloak login page. `keycloak` must be the Keycloak container
> name (see `container_name:` in its compose file) and both containers must be on the
> same Docker network.
>
> **Fix (Synology NAS 502 on callback):** the OIDC callback can take several seconds (token
> exchange). Synology’s nginx reverse proxy may return **502 Bad Gateway** if its buffer
> or timeout settings are too low. Edit the nginx template (e.g. `nginx.mustache` on DSM)
> and add larger proxy buffers for the app’s server block. For example:
> ```nginx
> proxy_buffer_size           128k;
> proxy_buffers 4             256k;
> proxy_busy_buffers_size     256k;
> ```
> See [Synology nginx reverse proxy – fix 502 Bad Gateway](https://mariushosting.com/synology-nginx-reverse-proxy-how-to-fix-502-bad-gateway-error/) for details and DSM-specific paths.

Click **Save**.

---

### Allowing Administrators to view users details from the app

- Go back to the Client details page and you should see a **Service account roles** tab — if you don't see it, the client may not have been created with "Service account roles" option enabled.
- From the `Assign role` dropdown, select `Realm roles` and assign the `Administrator` role.
- Go to the `Realm roles` menu -> `Administrator` -> `Associated roles` tab.
- Select `Client roles` from the `Assign role` dropdown.
- Add the `view-users` and`query-users` roles.

---

## Step 5 — Copy the Client Secret

1. Stay on the client page and click the **Credentials** tab.
2. Copy the value under **Client secret**.
3. Add it to your configuration (see [Step 8](#step-8--configure-myvideoarchive)).

> **Security:** Never commit the secret to source control. Use an environment variable
> or user secrets in development.

---

## Step 6 — Configure the Roles Claim Mapper

The app expects a flat `roles` claim in the token (e.g. `roles: ["Administrator"]`).
You must add a **User Realm Role** mapper on the client with the following settings;
getting any of them wrong can cause Keycloak to return "unknown_error" or
"cannot map type for token claim".

1. Go to **Clients → myvideoarchive** (click the client name).
2. Open the **Client scopes** tab. Click on the **myvideoarchive-dedicated** scope.
3. Open the **Mappers** tab.
4. Click **Create** (or **Add mapper → By configuration**).
5. Choose **User Realm Role**. If using predefined, then look for **realm roles**: 
6. Set these values **exactly**:

| Field                | Value        |
|----------------------|--------------|
| Name                 | `realm-roles`|
| Token Claim Name     | `roles`      |
| **Claim JSON Type**  | **`String`** — do **not** use `JSON` or Keycloak will throw "cannot map type for token claim". |
| **Multivalued**      | **On**       |
| Add to ID token      | **On**       |
| Add to access token  | On           |
| Add to userinfo      | On           |

7. Click **Save**.

If you previously created a mapper with Claim JSON Type set to `JSON`, delete it
and recreate it with `String`; the JSON type is not supported for this mapper and
causes a server error during the token exchange.

---

## Step 7 — Create Users and Assign Roles

### Create a user

1. In the left sidebar, go to **Users**.
2. Click **Create new user**.
3. Fill in **Username** and **Email**.
4. Toggle **Email verified** on (unless you have email verification configured).
5. Click **Create**.

### Set a password

1. On the user page, click the **Credentials** tab.
2. Click **Set password**.
3. Enter a password, toggle **Temporary** off.
4. Click **Save password**.

### Assign a role

1. On the user page, click the **Role mapping** tab.
2. Click **Assign role**.
3. Select either `Administrator` or `User`.
4. Click **Assign**.

> **Access control:** A user who has neither role assigned can log in via Keycloak but
> will be treated as an unauthenticated user by MyVideoArchive and denied access to
> protected pages.

---

## Step 8 — Configure MyVideoArchive

### Option A — `appsettings.json` (development / local)

Uncomment and fill in the `Authentication` section in `MyVideoArchive/appsettings.json`:

```json
"Authentication": {
  "Provider": "Keycloak",
  "Keycloak": {
    "Authority": "http://localhost:8081/realms/myvideoarchive",
    "ClientId": "myvideoarchive",
    "ClientSecret": "paste-your-client-secret-here"
  }
}
```

> Store the client secret in [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
> rather than directly in `appsettings.json`:
>
> ```bash
> dotnet user-secrets set "Authentication:Keycloak:ClientSecret" "your-secret"
> ```

### Option B — Environment variables (Docker / production)

In `docker-compose.yml`, uncomment the Keycloak lines under the `app` service:

```yaml
Authentication__Provider: Keycloak
Authentication__Keycloak__Authority: http://host.docker.internal:8081/realms/myvideoarchive
Authentication__Keycloak__ClientId: myvideoarchive
Authentication__Keycloak__ClientSecret: "${KEYCLOAK_CLIENT_SECRET}"
```

Set `KEYCLOAK_CLIENT_SECRET` in a `.env` file next to `docker-compose.yml`
(never commit this file):

```
KEYCLOAK_CLIENT_SECRET=paste-your-client-secret-here
```

> **Authority URL (local dev):** Use `http://host.docker.internal:8081/realms/myvideoarchive` so
> both the app container and the browser can reach Keycloak. `keycloak:8080` only resolves
> inside Docker and would make the browser try to open an unreachable URL.
>
> **Authority URL (NAS / production — separate stacks):** Use the public HTTPS URL (e.g.
> `https://keycloak.example.com/realms/myvideoarchive`). Set `BackchannelBaseUrl` to the
> internal Keycloak URL so the app container bypasses DNS/hairpin NAT for the token exchange:
> ```yaml
> Authentication__Keycloak__Authority: https://keycloak.example.com/realms/myvideoarchive
> Authentication__Keycloak__BackchannelBaseUrl: http://keycloak:8080
> ```
> Requires the app and Keycloak containers to share a Docker network (use an external
> named network in both compose files). Set `PublicOrigin` to the app's public URL (e.g.
> `https://mva.example.com`) if login redirects are built with `http://` instead of `https://`.

---

## Step 9 — First Login

1. Start the application.
2. Navigate to `http://localhost:8080`.
3. Click **Login** in the navbar — you will be redirected to Keycloak.
4. Log in with a Keycloak user that has the `User` or `Administrator` role.
5. You are redirected back to the app, now authenticated.

---

## Multiple Apps on the Same NAS

The power of Keycloak is that you can register multiple apps as clients within the same
realm and reuse the same user accounts:

1. Create a new client for each app (e.g. `myotheapp`).
2. Follow Steps 4–5 for each client to get its own client secret.
3. Assign realm roles to users once; all connected apps will see those roles.

If each app has its own role names (e.g. `Administrator` vs `admin`), you can either
standardise on common role names across all apps or map Keycloak roles per-app using
client roles and scope mappers.

---

## Switching Back to Built-in Identity

To revert to the built-in ASP.NET Core Identity system:

1. Remove (or comment out) the `Authentication` section from `appsettings.json` / environment variables.
2. Restart the application.

The built-in Identity system is completely independent of Keycloak; its user database is
unaffected by any Keycloak configuration.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| **Invalid redirect uri** (after clicking Logout) | Post-logout redirect URI not allowed in Keycloak | In **Clients → myvideoarchive → Login settings**, set **Valid post logout redirect URIs** to `http://localhost:8080/signout-callback-oidc` (exact; the OIDC middleware uses this path to complete sign-out). Replace host/port if your app URL is different. Save and try again. |
| **Invalid parameter: redirect_uri** (at Keycloak login) | App sent `http://` but Keycloak has `https://` registered (or host mismatch) | Ensure Keycloak client has the **exact** redirect URI the app sends. When behind a reverse proxy, set **Authentication:Keycloak:PublicOrigin** to the app's public HTTPS URL (e.g. `https://mva.my-domain.synology.me`) so the app sends `https://`. Set **Authority** to the public Keycloak URL the browser can reach (e.g. `https://auth.my-domain.synology.me/realms/myvideoarchive`). |
| Redirect loop on login / 431 Request Header Fields Too Large | (1) OIDC callback failed, then error page required auth and re-challenged; (2) cookies piled up from repeated failures | The app now redirects auth failures to the error page and the error page allows anonymous access. Clear all cookies for the app (e.g. `localhost:8080`), restart the app, and log in again in a clean session. |
| `unknown_error` / "For more on this error consult the server log" | Keycloak token endpoint failed; often **cannot map type for token claim** in Keycloak logs | The roles mapper has **Claim JSON Type** set to `JSON`. It must be **`String`** with **Multivalued** On. In **Clients → myvideoarchive → Mappers**, edit or delete the realm-roles mapper and recreate it with Claim JSON Type = **String**. See [Step 6](#step-6--configure-the-roles-claim-mapper). |
| "We can't connect to the server at keycloak" (browser) | Authority used `keycloak:8080`; that hostname only resolves inside Docker | When the app runs in Docker, set Authority to `http://host.docker.internal:8081/realms/...` so the browser can reach Keycloak. See [Step 8 Option B](#option-b--environment-variables-docker--production). |
| Redirect loop on login (PAR / redirect_uri) | Keycloak 26 PAR rejects wildcard redirect URIs | The app sets `PushedAuthorizationBehavior.Disable`. Use exact **Valid redirect URIs**: `http://localhost:8080/signin-oidc`. |
| "Client not found" error | `ClientId` mismatch | Ensure `Authentication:Keycloak:ClientId` matches the Keycloak client ID exactly. |
| 401 after successful Keycloak login | User has no `User` or `Administrator` role | Assign a realm role to the user in Keycloak (**Users → &lt;user&gt; → Role mapping → Assign role**). |
| `IDX20803: Unable to obtain configuration` | App cannot reach Keycloak | Check that the `Authority` URL is reachable from where the app runs (from the host use `localhost:8081`; from a container use `host.docker.internal:8081`). |
| **502 Bad Gateway** when redirected to `/signin-oidc` | (1) App container cannot reach Keycloak for the token exchange, or (2) reverse proxy buffers/timeout too low (e.g. Synology nginx) | (1) Set `BackchannelBaseUrl: http://keycloak:8080` and use a shared Docker network. (2) On Synology NAS, increase nginx proxy buffer size (e.g. `proxy_buffer_size 128k; proxy_buffers 4 256k; proxy_busy_buffers_size 256k;` in the app’s server block). See [mariushosting.com](https://mariushosting.com/synology-nginx-reverse-proxy-how-to-fix-502-bad-gateway-error/) and the “Synology NAS 502 on callback” note in Step 4. |
| HTTPS errors in development | `RequireHttpsMetadata` defaults | The code sets `RequireHttpsMetadata = false` for dev. For production, put Keycloak behind HTTPS. |
| Keycloak fails to start: "database keycloak does not exist" | PostgreSQL has no `keycloak` database | Create it once: `docker exec -it <postgres-container> psql -U postgres -c "CREATE DATABASE keycloak;"`. See [Prerequisites](#prerequisites). |
