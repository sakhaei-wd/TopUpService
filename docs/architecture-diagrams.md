# Architecture Diagrams

## 1. System Architecture — Component View

```mermaid
graph TB
    subgraph Customer["Customer Side"]
        TERM[Terminal / POS]
    end

    subgraph Existing["Existing Infrastructure (untouched)"]
        SWITCH[Switch.Net]
        SHAPARAK[Shaparak]
    end

    subgraph Broker["Message Broker"]
        RMQ_REQ([topup.request queue])
        RMQ_RES([topup.result queue])
    end

    subgraph TopUpSvc["TopUp Service — New Microservice"]
        CONSUMER[RabbitMQ Consumer<br/>BackgroundService]
        HANDLER[ProcessTopUpCommand<br/>Handler]
        IDEMPOTENCY[Idempotency Store<br/>InMemory / Redis]
        DB[(SQLite / SQL Server<br/>TopUpTransactions)]
        PUBLISHER[RabbitMQ Result<br/>Publisher]
        MCIHTTP[MCI HTTP Client<br/>Mock / Real]
    end

    subgraph External["External Operator"]
        MCI[MCI API<br/>شارژ فوری]
    end

    TERM -->|TCP Purchase| SWITCH
    SWITCH <-->|Purchase / Reversal| SHAPARAK
    SWITCH -->|Publish| RMQ_REQ
    RMQ_REQ -->|Consume| CONSUMER
    CONSUMER --> HANDLER
    HANDLER <--> IDEMPOTENCY
    HANDLER <--> DB
    HANDLER --> MCIHTTP
    MCIHTTP <-->|HTTP REST| MCI
    HANDLER --> PUBLISHER
    PUBLISHER -->|Publish| RMQ_RES
    RMQ_RES -->|Consume result| SWITCH
```

---

## 2. Clean Architecture — Dependency Flow

```mermaid
graph TD
    subgraph API["API Layer<br/>TopUpService.API"]
        PROG[Program.cs<br/>Composition Root]
        CTRL[TopUpController<br/>Query Endpoints]
        MW[GlobalException<br/>Middleware]
    end

    subgraph APP["Application Layer<br/>TopUpService.Application"]
        HDL[ProcessTopUpCommand<br/>Handler]
        CMD[ProcessTopUpCommand]
        EVT[TopUpResultEvent]
        IREPO[ITopUpRepository]
        IMCI[IMciTopUpClient]
        IPUB[ITopUpResultPublisher]
        IIDEM[IIdempotencyStore]
    end

    subgraph DOM["Domain Layer<br/>TopUpService.Domain"]
        ENT[TopUpTransaction<br/>Aggregate Root]
        ENUM[TopUpStatus<br/>Enum]
        EXC[TopUpDomain<br/>Exception]
    end

    subgraph INFRA["Infrastructure Layer<br/>TopUpService.Infrastructure"]
        REPO[TopUpRepository<br/>EF Core]
        CTX[TopUpDbContext<br/>SQLite]
        MOCK[MockMciTopUpClient]
        CONS[TopUpRequestConsumer<br/>BackgroundService]
        PUB[RabbitMqTopUpResult<br/>Publisher]
        IDEM[InMemoryIdempotency<br/>Store]
    end

    API --> APP
    API --> INFRA
    INFRA --> APP
    APP --> DOM

    REPO -.implements.-> IREPO
    MOCK -.implements.-> IMCI
    PUB -.implements.-> IPUB
    IDEM -.implements.-> IIDEM

    style DOM fill:#2d6a4f,color:#fff
    style APP fill:#1d3557,color:#fff
    style INFRA fill:#457b9d,color:#fff
    style API fill:#e63946,color:#fff
```

---

## 3. State Machine — TopUpTransaction Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Pending : TopUpTransaction.Create()

    Pending --> Processing : MarkProcessing()<br/>attemptCount++

    Processing --> Processing : RecordRetryAttempt()<br/>(transient error, attempt < 3)

    Processing --> Succeeded : MarkSucceeded(mciRef)<br/>MCI returned OK

    Processing --> Failed : MarkFailed(reason)<br/>MCI business error<br/>(no retry)

    Processing --> ReversalRequired : MarkReversalRequired(reason)<br/>All 3 retries exhausted

    Succeeded --> [*] : Result published<br/>IsSuccess=true<br/>RequiresReversal=false

    Failed --> [*] : Result published<br/>IsSuccess=false<br/>RequiresReversal=false

    ReversalRequired --> [*] : Result published<br/>IsSuccess=false<br/>RequiresReversal=true<br/>Switch issues Shaparak Reversal

    note right of Succeeded : MCI confirmed charge<br/>Funds stay debited
    note right of Failed : MCI rejected (business error)<br/>Switch returns funds
    note right of ReversalRequired : MCI unreachable<br/>Switch must reverse<br/>Shaparak transaction
```

---

## 4. Deployment Architecture

```mermaid
graph TB
    subgraph Docker["Docker Compose / Kubernetes"]
        subgraph SVC["topup-service container"]
            API2[ASP.NET Core<br/>Kestrel :8080]
            BGS[BackgroundService<br/>RabbitMQ Consumer]
        end

        subgraph RMQ2["rabbitmq container"]
            MGMT[Management UI<br/>:15672]
            AMQP[AMQP<br/>:5672]
        end

        subgraph DATA["Volume: topup-data"]
            SQLITE[(topup.db<br/>SQLite)]
        end
    end

    subgraph OPS["Operations"]
        SW[Swagger UI<br/>localhost:8080/swagger]
        RMQUI[RabbitMQ UI<br/>localhost:15672]
    end

    BGS <-->|AMQP| AMQP
    API2 --> SQLITE
    BGS --> SQLITE
    SW --> API2
    RMQUI --> MGMT
```
