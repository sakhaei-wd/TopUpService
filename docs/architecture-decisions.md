# Architecture Decision Records

## ADR-001: Asynchronous Processing via RabbitMQ

**Status:** Accepted

**Context:**
The MCI TopUp API has variable latency (50ms–3s) and potential downtime windows. Switch.Net handles high-volume Shaparak transactions and cannot afford to block a thread per TopUp call.

**Decision:**
Use RabbitMQ as the message broker. Switch.Net publishes to `topup.request`; TopUp Service consumes asynchronously and publishes results to `topup.result`. Switch.Net consumes results to finalize or reverse the original Shaparak purchase.

**Consequences:**
- (+) Switch.Net is never blocked by MCI latency.
- (+) Natural backpressure via RabbitMQ `prefetchCount`.
- (+) Messages survive service restarts (durable queues, `persistent=true`).
- (-) Operational complexity of running and monitoring RabbitMQ.
- (-) Result is eventually consistent — Switch polls or listens asynchronously.

---

## ADR-002: Idempotency Guard

**Status:** Accepted

**Context:**
RabbitMQ delivers messages at-least-once. A duplicate delivery without guards would charge the subscriber twice.

**Decision:**
Before any processing, check an idempotency store keyed on `IdempotencyKey` (supplied by Switch). If already seen, ACK the message and return immediately.

**Implementation:**
- Demo: `InMemoryIdempotencyStore` (thread-safe `HashSet`).
- Production: Redis `SET NX EX` for cluster-wide idempotency.

---

## ADR-003: Retry Strategy and Reversal Signalling

**Status:** Accepted

**Context:**
MCI API can be transiently unavailable. If we never retry, we fail unnecessarily. If we retry indefinitely, we risk double-charging (MCI may have processed the request but timed out on response).

**Decision:**
- Retry up to **3 times** with **exponential back-off** (1s, 2s, 4s) on transient errors (exceptions / HTTP 5xx).
- Do **not** retry on MCI business errors (`ERR_INVALID_MSISDN`, etc.) — the call will always fail.
- After 3 transient failures: set status `ReversalRequired` and publish `RequiresReversal=true` to Switch.
- Switch.Net then issues a Shaparak **Reversal (اصلاحیه)** to return the customer's money.

**Why reversal instead of silent failure?**
The customer's card was already debited at the Shaparak Purchase step. If TopUp fails, those funds must be returned via the formal Shaparak reversal flow. Silent failure would leave the customer out of pocket.

---

## ADR-004: Clean Architecture Layering

**Status:** Accepted

**Context:**
The codebase needs to be maintainable by a team over time, and infrastructure choices (DB, broker, MCI client) must be swappable.

**Decision:**
Four layers with strict dependency direction (inward only):

```
API → Application → Domain
Infrastructure → Application → Domain
```

- **Domain**: zero NuGet dependencies. Pure C# business logic.
- **Application**: depends on Domain only. Defines interfaces; no I/O code.
- **Infrastructure**: implements interfaces. Owns EF Core, RabbitMQ, HTTP clients.
- **API**: ASP.NET Core host. Composition root in `Program.cs`.

**Consequences:**
- Domain and Application are fully unit-testable with no mocking of infrastructure.
- Swapping SQLite→SQL Server or Mock→Real MCI client requires only one line change in `Program.cs`.

---

## ADR-005: SQLite for Demo / SQL Server for Production

**Status:** Accepted

**Context:**
The challenge asks for an in-memory or SQLite database for demo purposes.

**Decision:**
Use EF Core with SQLite. Connection string is externalized in `appsettings.json`. Switching to SQL Server or PostgreSQL requires:
1. Replace `Microsoft.Data.Sqlite` package with `Microsoft.EntityFrameworkCore.SqlServer`.
2. Change one line in `Program.cs`: `UseSqlite` → `UseSqlServer`.
3. Zero application or domain code changes.

---

## ADR-006: Single RabbitMQ Channel per Publisher

**Status:** Accepted

**Context:**
`IModel` (RabbitMQ channel) is not thread-safe but is cheap to create. Publishing results is serialized per transaction.

**Decision:**
Register `RabbitMqTopUpResultPublisher` as a **Singleton** with one persistent channel. The Application handler awaits publishing synchronously (one call per transaction). For high-throughput production use, consider a channel pool (`IObjectPool<IModel>`).
