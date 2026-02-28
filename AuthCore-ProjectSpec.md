# AuthCore
## Multi-Tenant Authentication Microservice
### Project Specification & Technical Reference | v1.0

---

## 1. Project Overview

AuthCore is a self-hostable, multi-tenant authentication microservice built in .NET (C#) with a PostgreSQL backend. Its purpose is to abstract authentication away from individual projects entirely — register once, plug any backend into it, and never rebuild auth logic again.

The core problem it solves: rebuilding authentication for every new personal or side project is slow, repetitive, and error-prone. AuthCore is the solution — a single running service that any backend can delegate auth to via a simple API contract.

This is an open source project. The admin UI is built in Angular with NgRx Signal Store.

### 1.1 Goals

- Eliminate the need to rebuild auth on any new project
- Provide a clean, well-documented REST API that any backend can consume
- Support multiple tenants — each registered application gets fully isolated user data
- Ship an admin UI for tenant administrators to manage users and roles
- Serve as a public portfolio and content asset (documented on X as it is built)

### 1.2 Out of Scope (MVP)

- Social login / OAuth2 provider integration (post-MVP)
- Multi-factor authentication (post-MVP)
- Password reset email flows (post-MVP)
- SDK or client library (post-MVP)
- Billing or subscription management

---

## 2. Technical Decisions

### 2.1 Stack

| Concern | Choice |
|---|---|
| Backend | .NET 8, C#, Minimal API, Entity Framework Core |
| Database | PostgreSQL (shared database, tenant-scoped via `tenant_id`) |
| Mediator | MediatR — command/query dispatch |
| Frontend | Angular with NgRx Signal Store |
| Hosting | Developer self-hosted — no managed infrastructure in scope for MVP |

### 2.2 Multi-Tenancy Model

All tenants share a single PostgreSQL database. Every user-facing table includes a `tenant_id` foreign key column. All queries are automatically scoped to the calling tenant — this is enforced at the service/repository layer, not the database layer.

This approach was chosen over per-tenant databases for MVP because it is significantly simpler to operate solo, avoids dynamic connection string management, and is sufficient for the anticipated usage scale. Migration to per-tenant databases is possible later if isolation requirements change.

### 2.3 Tenant Authentication (Service-to-Service)

When an external backend calls AuthCore, it authenticates using an API key passed in the request header. AuthCore resolves the tenant from that key before processing any request.

- API keys are generated on tenant registration
- The raw key is returned exactly once — it is never stored in plaintext
- Only a hashed version (SHA-256 with salt) is persisted in the database
- Tenants can regenerate their API key at any time — the old key is immediately invalidated

This model was chosen over OAuth2 client credentials because it is simpler to implement and consume, and is well understood by developers. It can be layered with client credentials later.

### 2.4 Password Hashing

User passwords are hashed using **Argon2id** via the `Konscious.Security.Cryptography` library. Argon2id was chosen over bcrypt because it is the current OWASP recommendation, resistant to both GPU and side-channel attacks, and configurable for memory and iteration cost.

### 2.5 JWT Strategy

- JWTs are issued on successful login and contain: `user_id`, `tenant_id`, `email`, and assigned roles as claims
- Access token TTL: **15 minutes**
- Refresh tokens are long-lived (configurable, default 7 days), stored hashed in the database
- Refresh token rotation is enforced — each use issues a new refresh token and revokes the old one
- Token validation is exposed as an API endpoint so any resource server can verify tokens without needing the JWT secret directly

### 2.6 Result Pattern

All command and query handlers return `Result<T>` rather than throwing exceptions for control flow. Domain exceptions are reserved for truly exceptional and unexpected failures. Business rule violations (invalid credentials, duplicate email, etc.) are expressed as `Result.Failure(error)` and unwrapped in the Presentation layer.

This keeps handlers predictable, testable, and free of hidden exception paths.

### 2.7 Repository Strategy

A single generic `IRepository` interface is used across the entire application. There is one implementation in Infrastructure backed by EF Core's `DbContext`. Individual tables do not get their own repository classes. Handlers inject `IRepository` directly and specify the entity type at the call site.

This was chosen to avoid the overhead of maintaining N repository classes for a project of this scale, while still keeping Infrastructure behind an interface that Core never references directly.

---

## 3. Architecture

### 3.1 Project Structure

```
AuthCore/
├── AuthCore.Core            # All business logic: entities, DTOs, commands, queries, handlers, interfaces, Result
├── AuthCore.Infrastructure  # EF Core, generic repository implementation, token service, password hasher
└── AuthCore.Presentation    # Minimal API endpoints, middleware, DI wiring — thin shell only
```

### 3.2 Dependency Rule

```
Presentation → Core
Infrastructure → Core
Core → nothing
```

Infrastructure and Presentation never reference each other. Core has zero external dependencies — it is pure C#.

### 3.3 Core Internals

```
AuthCore.Core/
├── Domain/
│   ├── Entities/
│   │   ├── Tenant.cs
│   │   ├── User.cs
│   │   ├── Role.cs
│   │   ├── UserRole.cs
│   │   └── RefreshToken.cs
│   └── Exceptions/
│       ├── NotFoundException.cs
│       └── UnauthorizedException.cs
├── DTOs/
│   ├── Requests/
│   │   ├── RegisterUserRequest.cs
│   │   ├── LoginRequest.cs
│   │   └── ...
│   └── Responses/
│       ├── AuthTokenResponse.cs
│       ├── UserResponse.cs
│       └── ...
├── Commands/
│   ├── RegisterUser/
│   │   ├── RegisterUserCommand.cs
│   │   └── RegisterUserCommandHandler.cs
│   ├── LoginUser/
│   │   ├── LoginUserCommand.cs
│   │   └── LoginUserCommandHandler.cs
│   ├── RefreshToken/
│   │   ├── RefreshTokenCommand.cs
│   │   └── RefreshTokenCommandHandler.cs
│   ├── RevokeToken/
│   │   ├── RevokeTokenCommand.cs
│   │   └── RevokeTokenCommandHandler.cs
│   └── RegisterTenant/
│       ├── RegisterTenantCommand.cs
│       └── RegisterTenantCommandHandler.cs
├── Queries/
│   ├── GetUserById/
│   │   ├── GetUserByIdQuery.cs
│   │   └── GetUserByIdQueryHandler.cs
│   ├── GetUsers/
│   │   ├── GetUsersQuery.cs
│   │   └── GetUsersQueryHandler.cs
│   └── ValidateToken/
│       ├── ValidateTokenQuery.cs
│       └── ValidateTokenQueryHandler.cs
├── Interfaces/
│   ├── IRepository.cs
│   ├── ITokenService.cs
│   └── IPasswordHasher.cs
└── Common/
    └── Result.cs
```

### 3.4 Infrastructure Internals

```
AuthCore.Infrastructure/
├── Persistence/
│   ├── AuthCoreDbContext.cs
│   ├── Repository.cs                  # Generic IRepository implementation
│   └── Configurations/                # EF Core entity configurations
│       ├── TenantConfiguration.cs
│       ├── UserConfiguration.cs
│       └── ...
├── Services/
│   ├── TokenService.cs                # JWT generation and validation
│   └── PasswordHasher.cs              # Argon2id implementation
└── DependencyInjection.cs             # Infrastructure service registration extension
```

### 3.5 Presentation Internals

```
AuthCore.Presentation/
├── Endpoints/
│   ├── TenantEndpoints.cs
│   ├── AuthEndpoints.cs
│   └── UserEndpoints.cs
├── Middleware/
│   ├── TenantResolutionMiddleware.cs
│   └── ExceptionHandlingMiddleware.cs
├── DependencyInjection.cs
└── Program.cs
```

---

## 4. Key Patterns

### 4.1 Result\<T\>

All handlers return `Result<T>` or `Result`. Business failures are expressed as `Result.Failure(error)`. The Presentation layer unwraps and maps to the appropriate HTTP response. Exceptions are not used for control flow.

```
Result<T>
├── IsSuccess / IsFailure
├── Value (T)       — populated on success
└── Error (string)  — populated on failure
```

### 4.2 Command / Query Pattern (MediatR)

Every use case is one Command or Query paired with one Handler. Endpoints dispatch via `IMediator.Send()`. Handlers contain all business logic — no business logic lives in endpoints or middleware.

- **Commands** — mutate state (register, login, revoke token)
- **Queries** — read state (get user, validate token, list users)

### 4.3 Generic Repository

`IRepository` exposes typed methods for `GetByIdAsync<T>`, `FindAsync<T>`, `AddAsync<T>`, `Update<T>`, `Remove<T>`, and `SaveChangesAsync`. Handlers specify the entity type at the call site. No per-table repository classes exist.

### 4.4 Tenant Resolution Middleware

Every request (except `POST /tenants`) passes through middleware that reads `X-Api-Key`, resolves the tenant, and injects it into `HttpContext`. Handlers never resolve tenant context themselves — it is always pre-populated by the time a handler executes.

---

## 5. Domain Model & Schema

All tables include `created_at` and `updated_at` auditable fields unless noted.

| Table | Column | Type | Notes |
|---|---|---|---|
| `tenants` | id | UUID PK | Primary key |
| `tenants` | name | varchar | Display name for the tenant |
| `tenants` | api_key_hash | varchar | Hashed API key — never stored plaintext |
| `tenants` | created_at / updated_at | timestamp | Auditable fields |
| `users` | id | UUID PK | Primary key |
| `users` | tenant_id | UUID FK | Foreign key to tenants — all queries scoped by this |
| `users` | email | varchar | Unique per tenant |
| `users` | password_hash | varchar | Argon2id hashed |
| `users` | is_active | boolean | Soft disable without deletion |
| `users` | created_at / updated_at | timestamp | Auditable fields |
| `roles` | id | UUID PK | Primary key |
| `roles` | tenant_id | UUID FK | Roles are tenant-scoped |
| `roles` | name | varchar | e.g. admin, viewer, editor |
| `user_roles` | user_id / role_id | UUID FK | Join table — many to many |
| `refresh_tokens` | id | UUID PK | Primary key |
| `refresh_tokens` | user_id | UUID FK | Owner of the token |
| `refresh_tokens` | token_hash | varchar | Hashed — never stored plaintext |
| `refresh_tokens` | expires_at | timestamp | Hard expiry |
| `refresh_tokens` | revoked | boolean | Explicit revocation flag |

---

## 6. API Endpoints (MVP)

All endpoints (except `POST /tenants`) require a valid tenant API key in the `X-Api-Key` header. Tenant context is resolved by middleware before any handler executes.

| Method | Endpoint | Command / Query | Purpose |
|---|---|---|---|
| POST | `/tenants` | `RegisterTenantCommand` | Register a new tenant — returns API key (once only) |
| POST | `/tenants/{id}/api-key/regenerate` | `RegenerateApiKeyCommand` | Rotate API key — returns new key (once only) |
| POST | `/auth/register` | `RegisterUserCommand` | Create a user under the calling tenant |
| POST | `/auth/login` | `LoginUserCommand` | Authenticate — returns JWT + refresh token |
| POST | `/auth/refresh` | `RefreshTokenCommand` | Exchange refresh token for new JWT |
| POST | `/auth/logout` | `RevokeTokenCommand` | Revoke refresh token |
| POST | `/auth/validate` | `ValidateTokenQuery` | Validate a JWT — returns claims/user data |
| GET | `/users` | `GetUsersQuery` | List users for the tenant (paginated) |
| GET | `/users/{id}` | `GetUserByIdQuery` | Get single user by ID |
| PUT | `/users/{id}` | `UpdateUserCommand` | Update user profile fields |
| DELETE | `/users/{id}` | `DeactivateUserCommand` | Soft delete (sets `is_active = false`) |
| POST | `/roles` | `CreateRoleCommand` | Create a role for the tenant |
| POST | `/users/{id}/roles` | `AssignRoleCommand` | Assign a role to a user |
| DELETE | `/users/{id}/roles/{roleId}` | `RemoveRoleCommand` | Remove a role from a user |

---

## 7. Admin UI (Angular)

The admin dashboard is built in Angular using NgRx Signal Store for state management. It is intended for tenant administrators to manage their user base without needing to call the API directly.

### 7.1 MVP Features

- Login as a tenant admin
- View paginated list of users
- View individual user detail and assigned roles
- Deactivate / reactivate users
- Create and assign roles
- Regenerate API key

### 7.2 State Management

NgRx Signal Store is used for all feature state. Each feature (users, roles, auth) has its own Signal Store slice. HTTP calls are made through Angular services — stores consume services and expose computed signals to components. No direct HTTP calls from components.

---

## 8. Delivery Phases

| Phase | Name | Scope |
|---|---|---|
| 1 | Foundation | Solution scaffold, EF Core + Postgres wiring, migrations, generic repository, Result pattern, DI setup |
| 2 | Tenant Registration | `RegisterTenantCommand`, tenant resolution middleware, API key generation and hashing |
| 3 | Auth Core | Register, login, JWT issuance, refresh token rotation, logout, token validation |
| 4 | User Management | Get user(s), update user, soft delete, role creation and assignment |
| 5 | Admin UI | Angular + NgRx Signal Store dashboard — user list, user detail, role management, API key rotation |
| Post-MVP | Enhancements | MFA, social login (OAuth2), password reset flows, audit log, SDK/client library |

---

## 9. Notes for Card Writing

### 9.1 General Rules

- Every handler that queries data must scope by `tenant_id` — call this out explicitly in AC
- No card should expose raw API keys or raw refresh tokens at any persistence step — hashing is always required
- All handlers return `Result<T>` or `Result` — no exceptions for business rule failures
- Soft delete (`is_active = false`) is used for users — no hard deletes in MVP
- All endpoints return consistent error shapes — cards for new endpoints must include AC for failure cases

### 9.2 Phase 1 Cards (Foundation)

- Scaffold solution with all 3 projects and project references wired
- Configure EF Core with Postgres, verify connection
- Write `Result.cs` and `Result<T>.cs` in Core/Common
- Define `IRepository`, `ITokenService`, `IPasswordHasher` interfaces in Core/Interfaces
- Implement `Repository.cs` in Infrastructure backed by `AuthCoreDbContext`
- Write and apply initial migrations for all MVP tables
- Wire DI in Presentation and Infrastructure `DependencyInjection.cs` extension methods

### 9.3 Phase 2 Cards (Tenant Registration)

- Implement `RegisterTenantCommand` + handler — generates tenant + hashed API key, returns raw key once
- Implement `TenantResolutionMiddleware` — reads `X-Api-Key`, resolves tenant, injects into `HttpContext`
- All subsequent phases depend on middleware being complete

### 9.4 Phase 3 Cards (Auth Core)

- `RegisterUserCommand` — validate input, hash password, scope user to tenant, return `Result<UserResponse>`
- `LoginUserCommand` — verify credentials, issue JWT + hashed refresh token, return `Result<AuthTokenResponse>`
- `RefreshTokenCommand` — validate and rotate refresh token, issue new JWT
- `RevokeTokenCommand` — revoke refresh token by ID
- `ValidateTokenQuery` — verify JWT signature and expiry, return claims as DTO

### 9.5 Dependencies & Risks

- Tenant middleware must be complete before any Phase 3 or Phase 4 cards begin
- JWT signing key must be managed via configuration — never hardcoded
- Argon2id hashing is computationally intentional — load test login under concurrency before shipping
- Refresh token rotation introduces a potential race condition if two requests use the same token simultaneously — known edge case, flagged for post-MVP handling
- `FindAsync<T>` on the generic repository loads results into memory before filtering — acceptable for MVP scale, revisit if query complexity grows

---

## Appendix — Key Configuration Values

| Setting | Default | Notes |
|---|---|---|
| JWT Access Token TTL | 15 minutes | Short-lived by design — refresh token handles session continuity |
| Refresh Token TTL | 7 days | Configurable per tenant post-MVP |
| Argon2id Memory Cost | 65536 KB | OWASP recommended minimum |
| Argon2id Iterations | 3 | Tune based on acceptable login latency |
| API Key Header | `X-Api-Key` | Required on all tenant-scoped requests |
| Tenancy Model | Shared DB | `tenant_id` scoping at application layer |