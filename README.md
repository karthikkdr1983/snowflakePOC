# TimeWise × Snowflake POC

A .NET 8 Web API that reads employee and manager data from **Snowflake** using a **key-pair service account**, for the TimeWise timesheet app. The SPFx front end calls this API; it never talks to Snowflake directly.

```
SPFx web part ──HTTPS──▶ TimeWise API (.NET 8) ──Snowflake.Data (key-pair JWT)──▶ Snowflake secure view
                               │                                                  TIMEWISE_POC.API.V_EMPLOYEE
                               └── on-prem SQL Server (timesheets, unchanged)
```

| Endpoint | What it returns |
|---|---|
| `GET /api/me/profile` | The signed-in user's employee record |
| `GET /api/me/team` | Their direct reports (for timesheet approval) |
| `GET /health/snowflake` | Who the API is connected as (user, role, warehouse) |
| `GET /api/demo/users` | Sample users for the demo page (POC only) |
| `/demo.html` | Demo UI · `/swagger` API explorer |

Phase 1 simulates the signed-in user with the `X-Test-User-Email` header. Phase 2 replaces it with the Entra ID token from SPFx. The endpoints don't change.

---

## Setup (about 30 minutes)

### Prerequisites
- .NET 8 runtime and a .NET 8+ SDK (`dotnet --list-sdks`)
- OpenSSL (`openssl version`). Git for Windows includes it.

### 1. Create a Snowflake trial
1. Sign up at <https://signup.snowflake.com>. Choose **Enterprise** edition, any cloud, and a region near you. No card is needed.
2. Activate the account from the email, then sign in to **Snowsight**, the web UI.

### 2. Run the setup scripts
In Snowsight, open a new SQL worksheet (**Projects → Workspaces** or **Worksheets**, depending on your UI version). Paste each script, press **Run All** (Ctrl+Shift+Enter), and run them in this order:

| Script | What it does |
|---|---|
| [`snowflake/01_setup.sql`](snowflake/01_setup.sql) | Creates the warehouse (X-Small, 60 s auto-suspend, 5-credit cap), database, table, secure view, role and service user. **The last query prints your account identifier; copy it.** |
| [`snowflake/02_sample_data.sql`](snowflake/02_sample_data.sql) | Loads 100 fake employees (CEO, then directors, then managers, then individual contributors) and prints test emails |

### 3. Generate the key pair
From the repo root, in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\New-SnowflakeKeyPair.ps1
```

The script:
- asks for a passphrase
- creates `keys\rsa_key.p8` (the encrypted private key) and `keys\rsa_key.pub`
- stores the passphrase in .NET user-secrets
- prints an `ALTER USER ... SET RSA_PUBLIC_KEY = '...'` statement

**Run that printed statement in Snowsight.** That's [`snowflake/03_set_public_key.sql`](snowflake/03_set_public_key.sql) with your key filled in. Then check that `RSA_PUBLIC_KEY_FP` in `DESC USER` matches the fingerprint the script printed.

<details><summary>Manual OpenSSL commands (if you can't run the script)</summary>

```bash
mkdir keys
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -aes-256-cbc -out keys/rsa_key.p8
openssl pkey -in keys/rsa_key.p8 -pubout -out keys/rsa_key.pub
```
Paste the lines of `rsa_key.pub` between the BEGIN and END lines, joined into one line, into `03_set_public_key.sql`. Put the passphrase in `appsettings.Development.json` as `Snowflake:PrivateKeyPassphrase`.
</details>

### 4. Configure
Edit `src/TimeWise.SnowflakePoc.Api/appsettings.Development.json`. This file is git-ignored; copy it from `appsettings.Development.example.json` if it's missing.

```json
"Snowflake": { "Account": "ABCDEFG-XY12345" }
```

Every other setting has a working default in `appsettings.json`:

| Setting | Default | Notes |
|---|---|---|
| `Snowflake:Account` | *(required)* | `ORGNAME-ACCOUNTNAME`, printed by `01_setup.sql` |
| `Snowflake:User` | `TIMEWISE_SVC` | |
| `Snowflake:Role` | `TIMEWISE_API_ROLE` | |
| `Snowflake:Warehouse` | `TIMEWISE_WH` | |
| `Snowflake:Database` / `Schema` | `TIMEWISE_POC` / `API` | |
| `Snowflake:PrivateKeyPath` | `../../keys/rsa_key.p8` | Relative to the API project folder |
| `Snowflake:PrivateKeyPassphrase` | *(user-secrets)* | Never put it in `appsettings.json` |
| `Cache:TtlSeconds` | `120` | Set to `0` to turn caching off |
| `Resilience:AttemptTimeoutSeconds` / `MaxRetries` | `15` / `2` | |

### 5. Run

```powershell
dotnet run --project src\TimeWise.SnowflakePoc.Api
```

Open <http://localhost:5080/demo.html>. The health pill at the top should turn green and read **Connected · TIMEWISE_SVC · TIMEWISE_API_ROLE · TIMEWISE_WH**.

You can also test from the command line:

```powershell
curl.exe http://localhost:5080/health/snowflake
curl.exe -H "X-Test-User-Email: rahul.taylor@timewise-demo.com" http://localhost:5080/api/me/profile
curl.exe -H "X-Test-User-Email: rahul.taylor@timewise-demo.com" http://localhost:5080/api/me/team
```

**Test users** (from the sample data):

| Email | Role | Direct reports |
|---|---|---|
| `aarav.sharma@timewise-demo.com` | CEO | 4 directors |
| `olivia.smith@timewise-demo.com` | Director of Engineering | 4 managers |
| `rahul.taylor@timewise-demo.com` | Engineering Manager | 6 engineers |
| `aarav.jones@timewise-demo.com` | Senior Software Engineer | none |

### 6. Tests (no Snowflake needed)

```powershell
dotnet test
```

---

## Troubleshooting

In Development, error responses include a `hint` field. The demo page shows it under 💡.

| Symptom | Fix |
|---|---|
| App won't start: `Account field is required` | Set `Snowflake:Account` in `appsettings.Development.json` |
| 503 with hint "Snowflake returned 404" | The account identifier is wrong. Use `ORGNAME-ACCOUNTNAME` from `01_setup.sql`, not the URL. |
| 503 with hint "Private key problem" | Wrong `PrivateKeyPath`, or a wrong or missing passphrase. Check with `dotnet user-secrets list --project src\TimeWise.SnowflakePoc.Api`. |
| 503 with code `390144` (JWT invalid) | The public key in Snowflake doesn't match `rsa_key.p8`. Run the `ALTER USER` statement again and compare fingerprints. |
| 503 with code `390100` | The user doesn't exist, or no public key is set on it |
| `Object 'V_EMPLOYEE' does not exist or not authorized` | `01_setup.sql` didn't finish. Re-run it; it is safe to re-run. |
| The first call takes 1–3 s | The warehouse was resuming from auto-suspend. Later calls are fast, and cached calls take a few ms. |
| `404 Employee not found` | That email isn't in the view, or the employee is inactive (IDs 99 and 100 are inactive on purpose) |

Every query is tagged. In Snowsight go to **Monitoring → Query History** and filter on **Query Tag** `timewise-api/get-profile`, `timewise-api/get-team` and so on.

---

## Moving to the client's Snowflake account
**Only configuration changes. The code stays the same.**

1. The client creates a `TYPE=SERVICE` user and a read-only role with SELECT on their employee view. `01_setup.sql` shows the pattern.
2. Generate a key pair. The client sets the public key on their user.
3. Change `Account`, `User`, `Role`, `Warehouse`, `Database` and `Schema`, plus the key path and passphrase.
4. If their column names differ, add a view on their side that maps them to the `V_EMPLOYEE` column list. The API stays unchanged.
5. If they use a network policy, give them the API host's outbound IPs. For App Service: **Properties → Outbound IP addresses**.

## Phase 2 (+1–2 days)
- **Entra ID:** add `Microsoft.Identity.Web`, write an `EntraIdUserProvider : ICurrentUserProvider` that reads the `preferred_username` or `email` claim, and remove the test header and `/api/demo/users`.
- **Key Vault:** store the private key as a secret and read it with the App Service's managed identity. Pass it to the driver with `private_key=` instead of `private_key_file=`.
- **SPFx:** call the API with `AadHttpClient`, add `webApiPermissionRequests` in `package-solution.json`, and have an admin approve it in the SharePoint admin centre under **API access**.
- **Optional:** a nightly sync into SQL Server if the timesheet app must keep working when Snowflake is down.

## Project layout
```
snowflake/          SQL setup scripts (run in Snowsight)
scripts/            Key-pair generator
src/TimeWise.SnowflakePoc.Api/
  Data/             Connection factory + repository (parameterised SQL, QUERY_TAG)
  Resilience/       Polly retry + timeout
  Identity/         Current-user abstraction (test header now, Entra ID later)
  Services/         Cache layer
  Infrastructure/   Error handling, Swagger header
  wwwroot/demo.html Demo UI
tests/              xUnit tests (offline, fake repository)
```
