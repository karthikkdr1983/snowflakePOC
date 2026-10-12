# TimeWise × Snowflake: demo script (15–20 min)

## Before the demo (30 min beforehand)
- [ ] `dotnet run --project src\TimeWise.SnowflakePoc.Api`, then open <http://localhost:5080/demo.html>. The health pill should be **green**.
- [ ] Click through the CEO, Rahul Taylor and Aarav Jones once. This resumes the warehouse and warms the connection pool.
- [ ] Open Snowsight in a second tab: a worksheet with `SELECT * FROM TIMEWISE_POC.HR.EMPLOYEE LIMIT 20;` ready to run.
- [ ] Open a third tab on **Monitoring → Query History**.
- [ ] **Backup:** record a 3-minute screen capture of the full flow (Win+Alt+R with Xbox Game Bar), in case the network fails.
- [ ] Close the terminal tab that shows the passphrase or user-secrets.

## Talk track

| # | Show | Say |
|---|---|---|
| 1 | **Architecture slide** (below) | "User data lives in the client's Snowflake. Our API reads it with a service account. SPFx only talks to our API." |
| 2 | **Snowsight:** the `HR.EMPLOYEE` table, including the SALARY column | "This is the source data. It includes sensitive columns." |
| 3 | **Snowsight:** `DESC USER TIMEWISE_SVC` and `SHOW GRANTS TO ROLE TIMEWISE_API_ROLE` | "A service user: no password and no interactive login, only an RSA key. Its role can read one secure view and nothing else." |
| 4 | **Demo page:** the green health pill | "The API is connected as that service account, with that role and warehouse." |
| 5 | **Pick Rahul Taylor (Engineering Manager)** | "Profile and direct reports, live from Snowflake. This is the approval hierarchy the timesheets need." Point at the **Snowflake · ### ms** badge. |
| 6 | Click **Call API again** | The badge turns green: **Cache · 2 ms**. "Repeat calls don't touch Snowflake, which saves credits and makes the page faster." |
| 7 | **Pick Aarav Jones (IC)** | "No team: an individual contributor has nothing to approve." |
| 8 | **Pick "Unknown user"** | "Clean 404. People who aren't in the directory get nothing." |
| 9 | **Swagger** (`/swagger`): call `/api/me/profile` with no header | "401: no identity, no data." |
| 10 | **Snowsight Query History**, filtered on Query Tag `timewise-api/*` | "Every API query is tagged, so it can be traced and its cost attributed. The warehouse suspends itself after 60 seconds idle." |
| 11 | **`appsettings.json`** | "To move to the client's account we change these six values. No code changes." |
| 12 | **Roadmap slide** | Entra ID SSO, Key Vault, the SPFx web part, then production |

## Slide: why the .NET connector

| # | Option | Effort | Latency | Extra infrastructure | Pick |
|---|---|---|---|---|---|
| 1 | **.NET connector (driver) + service account** | ⭐⭐⭐⭐ Easy, all in the team's skill set | Lowest (pooling + shared cache) | Just the App Service we already have | ✅ |
| 2 | Nightly sync into on-prem SQL Server (SSIS/ADF) | ⭐⭐⭐ ETL plus a driver or self-hosted IR | Fast reads, but data can be up to a day old | ETL runtime and schedule | Later, if needed |
| 3 | Snowflake SQL REST API | ⭐⭐ Hand-written HTTP, JWT and paging | Medium | None | ❌ |
| 4 | SQL Server linked server (ODBC) | ⭐⭐ Needs a DBA, hard to tune | Medium–High | ODBC driver on the SQL Server host | ❌ |
| 5 | SPFx calling Snowflake directly | Not viable | — | — | ❌ It would put credentials in the browser |

## Slide: architecture

```
 SharePoint Online                 Our backend                         Client's Snowflake
┌────────────────┐  Entra token  ┌────────────────────────┐  key-pair JWT  ┌──────────────────────┐
│ SPFx web part  │ ─────────────▶│ TimeWise API (.NET 8)  │ ─────────────▶ │ TIMEWISE_SVC (SERVICE)│
│ (TimeWise UI)  │ ◀──── JSON ── │  • cache (2 min)       │ ◀── rows ───── │ role: read 1 view     │
└────────────────┘               │  • retry + timeout     │                │ X-Small WH, 60s suspend│
                                 │  • QUERY_TAG           │                └──────────────────────┘
                                 └──────────┬─────────────┘
                                            │ timesheets (unchanged)
                                   ┌────────▼─────────┐
                                   │ On-prem SQL Server│
                                   └──────────────────┘
```

## Likely questions
- **What if Snowflake is down?** Cached users keep working. Others get a clean "temporarily unavailable" message. Timesheet data in SQL Server isn't affected. If the client requires it, we can add a nightly copy into SQL Server.
- **What does it cost?** An X-Small warehouse uses 1 credit per hour *while running*, billed per second with a 60 s minimum. With caching and a 60 s auto-suspend, a POC uses cents a day. A resource monitor caps it at 5 credits.
- **How is the key protected?** The private key is encrypted with a passphrase. In production it goes in Azure Key Vault, read through managed identity. Keys can be rotated without downtime using `RSA_PUBLIC_KEY_2`.
- **Can a user see someone else's data?** In production the identity comes from the Entra token, not a header the user can set. The API only returns the caller's own profile and team. The test header exists only in this POC.
- **What do we need from the client?** Account identifier, a service user with our public key, a read-only role on an employee view, and our outbound IPs added to their network policy if they use one.
