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
        CONSUMER[RabbitMQ Consumer\nBackgroundService]
        HANDLER[ProcessTopUpCommand\nHandler]
        IDEMPOTENCY[Idempotency Store\nInMemory / Redis]
        DB[(SQLite / SQL Server\nTopUpTransactions)]
        PUBLISHER[RabbitMQ Result\nPublisher]
        MCIHTTP[MCI HTTP Client\nMock / Real]
    end

    subgraph External["External Operator"]
        MCI[MCI API\nشارژ فوری]
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
    subgraph API["API Layer\nTopUpService.API"]
        PROG[Program.cs\nComposition Root]
        CTRL[TopUpController\nQuery Endpoints]
        MW[GlobalException\nMiddleware]
    end

    subgraph APP["Application Layer\nTopUpService.Application"]
        HDL[ProcessTopUpCommand\nHandler]
        CMD[ProcessTopUpCommand]
        EVT[TopUpResultEvent]
        IREPO[ITopUpRepository]
        IMCI[IMciTopUpClient]
        IPUB[ITopUpResultPublisher]
        IIDEM[IIdempotencyStore]
    end

    subgraph DOM["Domain Layer\nTopUpService.Domain"]
        ENT[TopUpTransaction\nAggregate Root]
        ENUM[TopUpStatus\nEnum]
        EXC[TopUpDomain\nException]
    end

    subgraph INFRA["Infrastructure Layer\nTopUpService.Infrastructure"]
        REPO[TopUpRepository\nEF Core]
        CTX[TopUpDbContext\nSQLite]
        MOCK[MockMciTopUpClient]
        CONS[TopUpRequestConsumer\nBackgroundService]
        PUB[RabbitMqTopUpResult\nPublisher]
        IDEM[InMemoryIdempotency\nStore]
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

    Pending --> Processing : MarkProcessing()\nattemptCount++

    Processing --> Processing : RecordRetryAttempt()\n(transient error, attempt < 3)

    Processing --> Succeeded : MarkSucceeded(mciRef)\nMCI returned OK

    Processing --> Failed : MarkFailed(reason)\nMCI business error\n(no retry)

    Processing --> ReversalRequired : MarkReversalRequired(reason)\nAll 3 retries exhausted

    Succeeded --> [*] : Result published\nIsSuccess=true\nRequiresReversal=false

    Failed --> [*] : Result published\nIsSuccess=false\nRequiresReversal=false

    ReversalRequired --> [*] : Result published\nIsSuccess=false\nRequiresReversal=true\nSwitch issues Shaparak Reversal

    note right of Succeeded : MCI confirmed charge\nFunds stay debited
    note right of Failed : MCI rejected (business error)\nSwitch returns funds
    note right of ReversalRequired : MCI unreachable\nSwitch must reverse\nShaparak transaction
```

---

## 4. Deployment Architecture

```mermaid
graph TB
    subgraph Docker["Docker Compose / Kubernetes"]
        subgraph SVC["topup-service container"]
            API2[ASP.NET Core\nKestrel :8080]
            BGS[BackgroundService\nRabbitMQ Consumer]
        end

        subgraph RMQ2["rabbitmq container"]
            MGMT[Management UI\n:15672]
            AMQP[AMQP\n:5672]
        end

        subgraph DATA["Volume: topup-data"]
            SQLITE[(topup.db\nSQLite)]
        end
    end

    subgraph OPS["Operations"]
        SW[Swagger UI\nlocalhost:8080/swagger]
        RMQUI[RabbitMQ UI\nlocalhost:15672]
    end

    BGS <-->|AMQP| AMQP
    API2 --> SQLITE
    BGS --> SQLITE
    SW --> API2
    RMQUI --> MGMT
```
