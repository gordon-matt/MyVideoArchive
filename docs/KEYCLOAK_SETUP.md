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

Click **Next**.

### Login settings

| Field                        | Value                                      |
|------------------------------|--------------------------------------------|
| Root URL                     | `http://localhost:8080`                    |
| Home URL                     | `http://localhost:8080`                    |
| Valid redirect URIs          | `http://localhost:8080/signin-oidc`        |
| Valid post logout redirect URIs | `http://localhost:8080/`               |
| Web origins                  | `http://localhost:8080`                    |

> **Why the exact path?** Keycloak 26 enables Pushed Authorization Requests (PAR) by
> default. PAR rejects wildcard redirect URIs (e.g. `/*`), so the exact callback path
> `/signin-oidc` must be used. The app disables PAR by default (`PushedAuthorizationBehavior.Disable`
> in `Program.cs`), which makes wildcards work again — but using the exact URI is better
> practice regardless.
>
> **Production note:** Replace `http://localhost:8080` with your actual public URL.
> Enable HTTPS and remove `RequireHttpsMetadata = false` from `Program.cs`.

Click **Save**.

---

## Step 5 — Copy the Client Secret

1. Stay on the client page and click the **Credentials** tab.
2. Copy the value under **Client secret**.
3. Add it to your configuration (see [Step 8](#step-8--configure-myvideoarchive)).

> **Security:** Never commit the secret to source control. Use an environment variable
> or user secrets in development.

---

## Step 6 — Configure the Roles Claim Mapper

By default Keycloak puts realm roles inside a nested JSON object (`realm_access.roles`).
MyVideoArchive reads this automatically via `KeycloakRolesClaimsTransformation`, so
**no extra mapper is required for the default setup**.

However, if you prefer to expose roles as a top-level `roles` array in the token
(which is slightly more interoperable), you can add a mapper:

1. Go to **Clients → myvideoarchive → Client scopes**.
2. Click on the `myvideoarchive-dedicated` scope.
3. Click **Add mapper → By configuration → User Realm Role**.
4. Set **Token Claim Name** to `roles`.
5. Enable **Add to ID token** and **Add to access token**.
6. Click **Save**.

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

In `docker-compose.yml`, uncomment the three Keycloak lines under the `app` service:

```yaml
Authentication__Provider: Keycloak
Authentication__Keycloak__Authority: http://keycloak:8080/realms/myvideoarchive
Authentication__Keycloak__ClientId: myvideoarchive
Authentication__Keycloak__ClientSecret: "${KEYCLOAK_CLIENT_SECRET}"
```

Set `KEYCLOAK_CLIENT_SECRET` in a `.env` file next to `docker-compose.yml`
(never commit this file):

```
KEYCLOAK_CLIENT_SECRET=paste-your-client-secret-here
```

> Note the authority URL uses `keycloak:8080` (the Docker service name and internal port)
> rather than `localhost:8081`. This is because the .NET app resolves the URL from inside
> the Docker network.

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
| Redirect loop on login | Wrong `Valid redirect URIs` in Keycloak | Add `http://localhost:8080/*` to Valid redirect URIs |
| "Client not found" error | `ClientId` mismatch | Ensure `Authentication:Keycloak:ClientId` matches the Keycloak client ID exactly |
| 401 after successful Keycloak login | User has no `User` or `Administrator` role | Assign a realm role to the user in Keycloak |
| `IDX20803: Unable to obtain configuration` | App cannot reach Keycloak | Check that the `Authority` URL is reachable from the app container (use the Docker service name, not `localhost`, when running in Docker) |
| HTTPS errors in development | `RequireHttpsMetadata` defaults | The code already sets `RequireHttpsMetadata = false` for dev. For production, put Keycloak behind a reverse proxy with TLS. |
