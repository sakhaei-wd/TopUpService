# TopUp Service — MCI Instant Recharge 

> **Sadad Payment Technical Challenge** — .NET 8 Microservice

---

## Overview

TopUp Service is an asynchronous microservice that bridges **Switch.Net** (the existing acquiring switch) and **MCI's TopUp API** to support the *Instant Recharge* transaction type required by the Shaparak ecosystem.

The service receives top-up requests from Switch via RabbitMQ, charges the subscriber through MCI, and publishes the result (success / failure / reversal-required) back to Switch through a second queue.

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Existing Infrastructure                             │
│                                                                             │
│   Terminal (POS/…)  ──TCP──►  Switch.Net  ──[Shaparak Purchase]──► Shaparak │
│                                   │                                         │
│                          (new)    │  topup.request queue                    │
└───────────────────────────────────┼─────────────────────────────────────────┘
                                    │
                    ┌───────────────▼───────────────────────────────┐
                    │           RabbitMQ Broker                     │
                    │  ┌─────────────────┐  ┌──────────────────┐    │
                    │  │  topup.request  │  │   topup.result   │    │
                    │  └────────┬────────┘  └──────────▲───────┘    │
                    └───────────┼───────────────────────┼───────────┘
                                │                       │
                    ┌───────────▼───────────────────────┴───────────┐
                    │               TopUp Service                   │
                    │  ┌──────────┐  ┌───────────┐  ┌───────────┐   │
                    │  │ Consumer │  │  Handler  │  │ Publisher │   │
                    │  └──────────┘  └─────┬─────┘  └───────────┘   │
                    │                      │  retry ×3              │
                    │               ┌──────▼──────┐                 │
                    │               │  MCI Client │                 │
                    │               └──────┬──────┘                 │
                    │               ┌──────▼──────┐                 │
                    │               │  SQLite DB  │ (demo)          │
                    └───────────────┴─────────────┴─────────────────┘
                                          │
                              ┌───────────▼────────────────┐
                              │        MCI API             │
                              │  (instant charge endpoint) │
                              └────────────────────────────┘
```

### Transaction Flow (Sequence)

```
Switch.Net          RabbitMQ            TopUp Service           MCI API
    │                  │                      │                   │
    │──Publish msg────►│                      │                   │
    │  (topup.request) │                      │                   │
    │                  │──Deliver message────►│                   │
    │                  │                      │──Check idempotency│
    │                  │                      │──Save Pending     │
    │                  │                      │──MarkProcessing   │
    │                  │                      │──ChargeAsync────► │
    │                  │                      │◄────────OK / ERR─ │
    │                  │                      │  (retry ×3 on     │
    │                  │                      │   transient err)  │
    │                  │                      │──Save final state │
    │                  │◄──Publish result─────│                   │
    │◄─Consume result──│  (topup.result)      │                   │
    │  → Confirm or    │                      │                   │
    │    Reverse       │                      │                   │
```

### Status State Machine

```
Pending ──MarkProcessing──► Processing
                                │
               ┌────────────────┼─────────────────────┐
               │                │                     │
        MarkSucceeded     MarkFailed          MarkReversalRequired
               │                │                     │
          Succeeded           Failed           ReversalRequired
                                                (Switch issues Reversal
                                                 to Shaparak)
```

---

## Project Structure

```
TopUpService/
├── src/
│   ├── TopUpService.Domain/            # Enterprise business rules
│   │   ├── Entities/                   # TopUpTransaction aggregate root
│   │   ├── Enums/                      # TopUpStatus
│   │   └── Exceptions/                 # TopUpDomainException
│   │
│   ├── TopUpService.Application/       # Use-case orchestration
│   │   ├── Commands/                   # ProcessTopUpCommand (message contract)
│   │   ├── Events/                     # TopUpResultEvent (outbound contract)
│   │   ├── Interfaces/                 # Repository, MCI client, publisher contracts
│   │   └── Services/                   # ProcessTopUpCommandHandler
│   │
│   ├── TopUpService.Infrastructure/    # I/O implementations
│   │   ├── Messaging/
│   │   │   ├── Consumers/              # RabbitMQ BackgroundService consumer
│   │   │   └── Publishers/             # RabbitMQ result publisher
│   │   ├── MciClient/                  # MockMciTopUpClient (swap for real)
│   │   ├── Persistence/                # EF Core + SQLite
│   │   │   ├── Configurations/         # Fluent entity config
│   │   │   └── Repositories/           # ITopUpRepository implementation
│   │   └── Idempotency/                # InMemoryIdempotencyStore
│   │
│   └── TopUpService.API/               # ASP.NET Core host
│       ├── Controllers/                # Read-only operational endpoints
│       ├── Middlewares/                # GlobalExceptionMiddleware
│       ├── Models/                     # Response DTOs
│       └── Program.cs                  # Composition root
│
└── tests/
    └── TopUpService.UnitTests/
        ├── Domain/                     # Entity + state machine tests
        └── Application/                # Handler tests with Moq
```

---

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Docker + Docker Compose](https://docs.docker.com/get-docker/)

### Run with Docker Compose

```bash
# Start RabbitMQ + TopUp Service
docker-compose up --build

# Service: http://localhost:8080
# Swagger:  http://localhost:8080/swagger
# RabbitMQ Management: http://localhost:15672 (guest/guest)
```

### Run locally (without Docker)

```bash
# 1. Start RabbitMQ
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3.13-management-alpine

# 2. Run the API
cd src/TopUpService.API
dotnet run
```

### Run Tests

```bash
dotnet test tests/TopUpService.UnitTests
```

---

## API Endpoints

| Method  | Path                                 | Description |
|---------|--------------------------------------|----------------------------------|
| `GET`   | `/api/topup/{id}`                    | Get transaction by internal GUID |
| `GET`   | `/api/topup/by-key/{idempotencyKey}` | Get transaction by Switch's idempotency key |
| `GET`   | `/health`                            | Liveness probe |

Full Swagger documentation available at `/swagger` in Development mode.

---

## Message Contracts

### topup.request (consumed from Switch.Net)

```json
{
  "IdempotencyKey": "SWITCH-UNIQUE-KEY-001",
  "SwitchReferenceNumber": "2024050900001",
  "PhoneNumber": "09120000000",
  "AmountRials": 50000
}
```

### topup.result (published back to Switch.Net)

```json
{
  "IdempotencyKey": "SWITCH-UNIQUE-KEY-001",
  "SwitchReferenceNumber": "2024050900001",
  "IsSuccess": true,
  "MciReferenceNumber": "MCI-A1B2C3D4E5F6",
  "RequiresReversal": false,
  "FailureReason": null,
  "ProcessedAt": "2024-05-09T10:30:00Z"
}
```

When `RequiresReversal = true`, Switch.Net must send a **Reversal (اصلاحیه)** to Shaparak to return the funds already deducted from the customer's card.

---

## Design Decisions

See [docs/architecture-decisions.md](docs/architecture-decisions.md) for full ADR log.

Key choices:
- **Async via RabbitMQ** — decouples Switch from MCI latency; Switch can continue processing other transactions while MCI is contacted.
- **Idempotency guard** — prevents duplicate MCI charges on RabbitMQ redelivery.
- **Reversal signalling** — when MCI is unreachable after 3 retries, the service signals Switch to issue a Shaparak reversal rather than silently failing.
- **Clean Architecture** — Domain has zero external dependencies; all I/O is behind interfaces.
- **SQLite for demo** — swap `UseSqlite` → `UseSqlServer` in `Program.cs` with zero other changes required.

---

## Git Strategy (GitFlow)

```
main          ← production-ready only; tagged with semver
  └── develop ← integration branch
        ├── feature/topup-domain-entities
        ├── feature/topup-application-handler
        ├── feature/rabbitmq-consumer-publisher
        ├── feature/mci-mock-client
        └── feature/unit-tests
```

### Commit Message Convention (Conventional Commits)

```
feat(domain): add TopUpTransaction aggregate with state machine
feat(application): implement ProcessTopUpCommandHandler with retry logic
feat(infra): add RabbitMQ consumer and result publisher
feat(infra): add MockMciTopUpClient with configurable outcomes
feat(api): expose read-only transaction query endpoints
test(domain): add state machine unit tests for TopUpTransaction
test(application): add handler tests covering success/failure/reversal
chore: add Docker Compose and Dockerfile for local development
docs: add architecture decision records
```

---

## Production Readiness Checklist

- [ ] Replace `MockMciTopUpClient` with real HTTP client (add `HttpClientFactory`, `Polly`)
- [ ] Replace `InMemoryIdempotencyStore` with Redis store for multi-instance deployments
- [ ] Replace SQLite with SQL Server / PostgreSQL
- [ ] Add a Dead-Letter Exchange (DLX) for poisoned messages in RabbitMQ
- [ ] Add distributed tracing (OpenTelemetry)
- [ ] Secure `appsettings.json` credentials with Azure Key Vault / env secrets
- [ ] Add integration tests against a real RabbitMQ instance
