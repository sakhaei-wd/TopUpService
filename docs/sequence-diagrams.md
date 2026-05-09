# Sequence Diagrams

## 1. Happy Path — Successful TopUp

```mermaid
sequenceDiagram
    autonumber
    actor Customer as Customer (Subscriber)
    participant Terminal as Terminal (POS)
    participant Switch as Switch.Net
    participant Shaparak as Shaparak
    participant RMQ as RabbitMQ
    participant TopUp as TopUp Service
    participant MCI as MCI API

    Customer->>Terminal: Request top-up (phone, amount)
    Terminal->>Switch: TCP — Purchase Transaction
    Switch->>Shaparak: Purchase Request
    Shaparak-->>Switch: Purchase OK (funds debited)

    Note over Switch,RMQ: Switch detects topup transaction type
    Switch->>RMQ: Publish → topup.request queue
    Note right of RMQ: {IdempotencyKey, SwitchRef,<br/>PhoneNumber, AmountRials}

    RMQ->>TopUp: Deliver message (prefetch=1)
    TopUp->>TopUp: Check idempotency key
    TopUp->>TopUp: Persist (status=Pending)
    TopUp->>TopUp: MarkProcessing() → status=Processing

    TopUp->>MCI: ChargeAsync(phone, amount, requestId)
    MCI-->>TopUp: 200 OK — {referenceNumber}

    TopUp->>TopUp: MarkSucceeded(mciRef) → status=Succeeded
    TopUp->>TopUp: Persist final state
    TopUp->>TopUp: Mark idempotency key as done
    TopUp->>RMQ: Publish → topup.result queue
    Note right of RMQ: {IsSuccess=true,<br/>MciReferenceNumber,<br/>RequiresReversal=false}

    RMQ->>Switch: Deliver result
    Switch->>Switch: Confirm transaction ✓
    Switch-->>Terminal: TopUp confirmed
    Terminal-->>Customer: Recharge successful ✓
```

---

## 2. Transient Failure Path — MCI Unreachable (Reversal Required)

```mermaid
sequenceDiagram
    autonumber
    participant Switch as Switch.Net
    participant RMQ as RabbitMQ
    participant TopUp as TopUp Service
    participant MCI as MCI API
    participant Shaparak as Shaparak

    Switch->>RMQ: Publish → topup.request
    RMQ->>TopUp: Deliver message

    TopUp->>TopUp: Persist (status=Pending)
    TopUp->>TopUp: MarkProcessing()

    loop Retry up to 3 times (exponential back-off: 1s, 2s, 4s)
        TopUp->>MCI: ChargeAsync(...)
        MCI--xTopUp: Timeout / Connection refused
        TopUp->>TopUp: RecordRetryAttempt()
        TopUp->>TopUp: Persist (attempt count updated)
    end

    Note over TopUp: All 3 attempts exhausted
    TopUp->>TopUp: MarkReversalRequired()
    TopUp->>RMQ: Publish → topup.result
    Note right of RMQ: {IsSuccess=false,<br/>RequiresReversal=true}

    RMQ->>Switch: Deliver result
    Switch->>Shaparak: Reversal (اصلاحیه) — return funds
    Shaparak-->>Switch: Reversal OK
    Switch-->>Switch: Transaction reversed ✓

    Note over Switch,Shaparak: Customer funds fully returned
```

---

## 3. Business Failure Path — MCI Rejects Request

```mermaid
sequenceDiagram
    autonumber
    participant Switch as Switch.Net
    participant RMQ as RabbitMQ
    participant TopUp as TopUp Service
    participant MCI as MCI API
    participant Shaparak as Shaparak

    Switch->>RMQ: Publish → topup.request
    RMQ->>TopUp: Deliver message

    TopUp->>TopUp: Persist (status=Pending)
    TopUp->>TopUp: MarkProcessing()

    TopUp->>MCI: ChargeAsync(...)
    MCI-->>TopUp: {IsSuccess=false, ErrorCode="ERR_INVALID_MSISDN"}

    Note over TopUp: Business error — do NOT retry
    TopUp->>TopUp: MarkFailed(reason)
    TopUp->>RMQ: Publish → topup.result
    Note right of RMQ: {IsSuccess=false,<br/>RequiresReversal=false}

    RMQ->>Switch: Deliver result
    Switch->>Shaparak: Reversal (اصلاحیه) — return funds
    Note over Switch,Shaparak: MCI never charged →<br/>funds returned to customer
```

---

## 4. Duplicate Message (Idempotency Guard)

```mermaid
sequenceDiagram
    autonumber
    participant RMQ as RabbitMQ
    participant TopUp as TopUp Service
    participant Store as Idempotency Store

    Note over RMQ,TopUp: Same message delivered twice (RabbitMQ at-least-once)

    RMQ->>TopUp: Deliver message #1
    TopUp->>Store: ExistsAsync("KEY-001")
    Store-->>TopUp: false
    TopUp->>TopUp: Process normally...
    TopUp->>Store: MarkAsync("KEY-001")
    TopUp->>RMQ: BasicAck ✓

    RMQ->>TopUp: Deliver message #2 (duplicate)
    TopUp->>Store: ExistsAsync("KEY-001")
    Store-->>TopUp: true ← already processed
    Note over TopUp: Skip processing entirely
    TopUp->>RMQ: BasicAck ✓ (safe to discard)
```
