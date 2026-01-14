# PnP Middleware

## Nodes

| Node | Team | Role | Protocol | Signature |
|------|------|------|----------|-----------|
| **PnP Middleware** | pnp middleware service | Payment Orchestrator | REST API | AES-256 (MessageId) |
| **Trumo** | Trumo | Payment Provider | REST + Webhooks | RSA-SHA256 |
| **HittiKasino** | hittikasino | Casino Operator | REST API | SHA1 |
| **Carouseller** | carouseller | User Authorization | REST API | SHA1 |

---

## Deposit Flow

```
 Client                Middleware              Trumo              Casino           Carouseller
    │                      │                     │                   │                  │
    │  DEPOSIT_REQ         │                     │                   │                  │
    │───────────────── ───▶│                     │                   │                  │
    │                      │                     │                   │                  │
    │                      │  PAYPROV_REQ        │                   │                  │
    │                      │────────────────────▶│                   │                  │
    │                      │                     │                   │                  │
    │                      │  PAYPROV_RESP       │                   │                  │
    │                      │◀────────────────────│                   │                  │
    │                      │                     │                   │                  │
    │  DEPOSIT_RESP        │                     │                   │                  │
    │◀─────────────────────│                     │                   │                  │
    │                      │                     │                   │                  │
    │  PAYMENT_REDIRECT ───────────────────────▶│                   │                  │
    │     (user makes payment)                   │                   │                  │
    │                      │                     │                   │                  │
    │                      │  KYC_NOTIFY         │                   │                  │
    │                      │◀────────────────────│                   │                  │
    │                      │                     │                   │                  │
    │                      │  USER_CHECK_REQ     │                   │                  │
    │                      │────────────────────────────────────────▶│                  │
    │                      │                     │                   │                  │
    │                      │  USER_CHECK_RESP    │                   │                  │
    │                      │◀────────────────────────────────────────│                  │
    │                      │                     │                   │                  │
    │                      │  USER_CREATE_REQ    │                   │                  │
    │                      │────────────────────────────────────────▶│                  │
    │                      │                     │                   │                  │
    │                      │  USER_CREATE_RESP   │                   │                  │
    │                      │◀────────────────────────────────────────│                  │
    │                      │                     │                   │                  │
    │                      │  AUTH_REQ           │                   │                  │
    │                      │───────────────────────────────────────────────────────────▶│
    │                      │                     │                   │                  │
    │                      │  AUTH_RESP          │                   │                  │
    │                      │◀──────────────────────────────────────────────────────────│
    │                      │                     │                   │                  │
    │                      │  KYC_RESP           │                   │                  │
    │                      │────────────────────▶│                   │                  │
    │                      │                     │                   │                  │
    │  SUCCESS_REDIRECT  ◀──────────────────────│                   │                  │
    │                      │                     │                   │                  │
    │  AUTOLOGIN_RESP      │                     │                   │                  │
    │◀─────────────────────│                     │                   │                  │
    │                      │                     │                   │                  │
    │                      │  PAYPROV_NOTIFY_FWD │                   │                  │
    │                      │◀────────────────────│                   │                  │
    │                      │                     │                   │                  │
    │                      │  PAYPROV_NOTIFY_FWD │                   │                  │
    │                      │───────────────────────────────────────────────────────────▶│
    │                      │                     │                   │                  │
    │                      │  PAYPROV_NOTIFY_RESP│                   │                  │
    │                      │◀──────────────────────────────────────────────────────────│
    │                      │                     │                   │                  │
    │                      │  PAYPROV_NOTIFY_RESP│                   │                  │
    │                      │────────────────────▶│                   │                  │
    │                      │                     │                   │                  │
```

### PNP Process Description

**1. Deposit Initiation**
- Client sends `DEPOSIT_REQ` → Middleware
- Middleware:
  - Generates MessageId (AES-256 password encryption)
  - Saves session to MongoDB
  - Sends `PAYPROV_REQ` → Trumo
  - Receives `PAYPROV_RESP` with payment page URL
- Middleware returns `DEPOSIT_RESP` → Client

**2. Bank Authentication**
- Client opens payment page in iframe
- User selects bank and authenticates
- Bank confirms identity and initiates payment

**3. KYC Processing and Registration**
- Trumo sends `KYC_NOTIFY` → Middleware (webhook with user data)
- Middleware retrieves session from MongoDB by `merchantOrderID`
- Middleware sends `USER_CHECK_REQ` → Casino
- Casino returns `USER_CHECK_RESP`
- **Condition:** `exists == true` OR `(valid == true AND errors == 0)`
  - **On success:**
    - Middleware sends `USER_CREATE_REQ` → Casino
    - Casino returns `USER_CREATE_RESP` (user_id, autologin URL (SuccessLoginUrl))
    - Middleware sends `AUTH_REQ` → Carouseller
    - Carouseller returns `AUTH_RESP`
    - Middleware saves SuccessLoginUrl to session
    - Middleware responds `KYC_RESP` → Trumo (`proceed`)
  - **On error:**
    - Middleware responds `KYC_RESP` → Trumo (`cancel`)

**4. Completion and Redirect**
- Trumo completes payment and redirects user → `SUCCESS_REDIRECT`
- Middleware retrieves SuccessLoginUrl from session
- Middleware returns HTML page with automatic redirect
- Client navigates to casino autologin page
- User is authenticated in casino with deposited balance

### Payment Provider Notification Handling

Middleware receives notifications from Trumo at the `/trumo/notifications` endpoint and processes them based on type:

| type | status | Processing |
|------|--------|------------|
| `payerDetails` | — | KYC processing: user verification/creation in Casino, authorization in Carouseller |
| `deposit` | `initiated` | Middleware responds `processed` (session not yet created in Carouseller) |
| All others | — | Forward to Carouseller (`a.papaya.ninja`), response returned to Trumo |

**Notification Forwarding:**
- Middleware forwards notification to `https://a.papaya.ninja/gate/trumo/`
- Waits for response from Carouseller
- Returns received response back to Trumo

### Request Parameters

#### DEPOSIT_REQ
**Client → Middleware** | PNP deposit initiation

`POST /trumo/deposit`
```json
{
  "Email": "user@example.com",
  "Amount": 100,
  "Password": "user_password",
  "Currency": "EUR",
  "Country": "FI",
  "Locale": "fi_FI",
  "FailUrl": "https://casino.com/failed",
  "PartnerId": "partner123"
}
```

#### DEPOSIT_RESP
**Middleware → Client** | Response with data for redirect to payment page

```json
{
    "Url": "https://payer-stg.trumo.io/banks/?merchant=68ff32c9f200f31ee5f57bfd&order_id=696788567fd66d82905727b9&lang=fi",
    "OrderId": "1b1f0fd8-5295-4b03-8941-8ae446cb945c",
    "MessageId": "Rg0F6aXpA1o4pLMSTdlnsQrJhQqTL4mrRMOI4Jp9rmk"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `Url` | string | Payment page URL for iframe |
| `OrderId` | string | Order ID in Trumo system |
| `MessageId` | string | Encrypted transaction identifier |

#### PAYPROV_REQ
**Middleware → Trumo** | Payment session creation request

`POST https://api-stg.trumo.io/v1/deposit`

```json
{
    "signature": "...",
    "UUID": "3c7bc146-3bec-4541-86dc-f5edeaadc128",
    "data": {
        "notificationURL": "https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/trumo/notifications",
        "successURL": "https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/success?messageid=Rg0F6aXpA1o4pLMSTdlnsQrJhQqTL4mrRMOI4Jp9rmk",
        "failureURL": "https://yourpage.com/fail",
        "orderDetails": {
            "merchantOrderID": "Rg0F6aXpA1o4pLMSTdlnsQrJhQqTL4mrRMOI4Jp9rmk",
            "amount": "250.00",
            "currency": "EUR",
            "country": "FI",
            "locale": "fi_FI"
        }
    }
}
```

| Field | Description |
|-------|-------------|
| `signature` | RSA-SHA256 request signature |
| `UUID` | Unique request identifier |
| `merchantOrderID` | MessageId |
| `notificationURL` | URL for webhook notifications |
| `successURL` | Success redirect URL |
| `failureURL` | Failure redirect URL |

#### PAYPROV_RESP
**Trumo → Middleware** | Response with payment page URL and order identifier

```json
{
    "UUID": "3c7bc146-3bec-4541-86dc-f5edeaadc128",
    "data": {
        "orderDetails": {
            "merchantOrderID": "Rg0F6aXpA1o4pLMSTdlnsQrJhQqTL4mrRMOI4Jp9rmk",
            "trumoOrderID": "1b1f0fd8-5295-4b03-8941-8ae446cb945c",
            "amount": "250.00000",
            "currency": "EUR",
            "url": "https://payer-stg.trumo.io/banks/?merchant=68ff32c9f200f31ee5f57bfd&order_id=696788567fd66d82905727b9&lang=fi"
        }
    }
}
```

| Field | Description |
|-------|-------------|
| `trumoOrderID` | Order ID in Trumo system |
| `url` | Payment page URL for user |

#### KYC_NOTIFY (payerDetails)
**Trumo → Middleware** | Webhook with user KYC data after bank authentication

`POST /trumo/notifications`

```json
{
    "signature": "...",
    "UUID": "b155cfe1-be2a-4f50-8716-5f6ef90e4720",
    "type": "payerDetails",
    "data": {
        "notificationID": "6967527d9ec43bad81e98b12",
        "orderDetails": {
            "trumoOrderID": "14e4baea-991c-4d57-9f8b-afa1f8fadf5b",
            "merchantOrderID": "i9eGxCiLGwv_wHN0CDuBBcFLoxvEIpIcixgQoibZ_9ey0w6Vvg11kbbjwb2AZ8en"
        },
        "payerDetails": {
            "trumoPayerID": "4329ceae-0bd6-47c8-a673-f75197164f71",
            "personalID": "56d27f4a53bd5441",
            "firstName": "Nichols",
            "lastName": "Jennie",
            "birthDate": "1985-10-22",
            "street": null,
            "city": null,
            "zipcode": "00100",
            "country": null
        }
    }
}
```

| Field | Description |
|-------|-------------|
| `type` | Notification type: `payerDetails`, `orderStatus`, `bankAccount` |
| `merchantOrderID` | MessageId for session linking |
| `trumoPayerID` | Payer ID in Trumo system |
| `firstName`, `lastName` | First and last name from bank |
| `birthDate` | Date of birth (YYYY-MM-DD) |
| `zipcode`, `country`, `city`, `street` | Address data |

#### USER_CHECK_REQ
**Middleware → Casino** | User registration eligibility check

`POST /registration/api/`

**Authentication:** Basic Auth (`123:123`)

**Parameters:**

| Parameter | Description |
|-----------|-------------|
| `ident` | Integration identifier (`ptz`) |
| `email` | User email |
| `first_name` | First name |
| `last_name` | Last name |
| `password` | Password |
| `birth_day`, `birth_month`, `birth_year` | Date of birth |
| `address_zip`, `address_country`, `address_city`, `address_street` | Address |
| `sign` | SHA1(sorted_params + secret) |

**Request example:**
```
https://hittikasino.com/registration/api/?ident=ptz&email=user@example.com&first_name=Nichols&last_name=Jennie&password=user@example.com&birth_day=22&birth_month=10&birth_year=1985&address_zip=00100&sign=52c5318376261ed8aa35e88d6745b592d4142250
```

#### USER_CHECK_RESP
**Casino → Middleware** | User data validation result
```json
{
    "_xsrf": "2|34acc9d9|9f5fe75c07a26d424f49c1164262465e|1768379008",
    "DEBUG": false,
    "exists": false,
    "form_errors": {},
    "error": 0,
    "valid": true
}
```

| Field | Type | Description |
|-------|------|-------------|
| `exists` | bool | User already exists in system |
| `valid` | bool | User data is valid for registration |
| `error` | int | Number of validation errors |
| `form_errors` | object | Error details by field |

#### USER_CREATE_REQ
**Middleware → Casino** | New user creation in casino system. Also called for existing users to get user ID and autologin URL

`GET /a/pr/{ident}/{hash}/`

**Authentication:** Basic Auth (`123:123`)

```
https://hittikasino.com/a/pr/ptz/4d6c9bc2026e6c24ebc68e127338a77048e04392/?ident=ptz&email=ramantest56@gmail.com&first_name=Nichols&last_name=Jennie&password=ramantest56@gmail.com&birth_day=22&birth_month=10&birth_year=1985&address_zip=00100&user_id=on&av_check=true
```

| Parameter | Description |
|-----------|-------------|
| `user_id` | Flag "on" to get user ID |
| `av_check` | Age verification flag. Always passed as "true" |
| `partner` | Partner ID (optional) |

#### USER_CREATE_RESP
**Casino → Middleware** | Created user data and autologin URL
```json
{
    "_xsrf": "2|ff3c187e|63976584e5e9598b715d26e260345a19|1768379009",
    "DEBUG": false,
    "user_id": "69675281a6e7a9d15cc88179",
    "autologin": "https://hittikasino.com/welcome/a/pr/f2943b85a2d94aad9e7204ad1856a759?redirect=....",
    "should_confirm_email": true,
    "should_provide_email": false,
    "user_email": "ramantest56@gmail.com",
    "is_native_app": true,
    "confirm_by_code": true,
    "confirm_attempts_limit_reached": false,
    "can_change_email": false,
    "mailbox_link": "",
    "email_provider": "gmail",
    "email_provider_webui_url": "https://mail.google.com/"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `exists` | bool | true after creation |
| `user_id` | string | Created user ID |
| `autologin` | string | URL for automatic user login |

#### AUTH_REQ
**Middleware → Carouseller** | Session creation

`GET https://a.papaya.ninja/api/authlink/obtain/`
```
?site_id=96
&site_login={user_id}
&user_email=user@example.com
&customer_ip=8.8.8.8
&user_name=Nichols Jennie
&first_name=Nichols
&last_name=Jennie
&currency=EUR
&trumo_uuid={merchantOrderID}:{trumoOrderID}
&birthdate=22-10-1985
&user_country=FI
&user_city=Helsinki
&user_address=Street 1
&user_postal=00100
&sign={sign}
```

| Parameter | Description |
|-----------|-------------|
| `site_id` | Site ID in Carouseller system (96) |
| `site_login` | User ID in casino |
| `trumo_uuid` | Format: `{merchantOrderID}:{trumoOrderID}` |
| `trustly_uuid` | Alternative for Trustly: `{orderid}` |
| `sign` | SHA1(key:value;key:value;...;secret) |

#### AUTH_RESP
**Carouseller → Middleware** | Successful authorization confirmation
```json
{
    "success": true,
    "errors": null,
    "key": "eyJybmQiOiAiNGZiZDhiYzMtNzJmYy00NjE2LTk2N2EtZTMzM2JhNTFkYzdkIn0.aWdShA.De_ultKl_XcpVNQiBekoIhEWIdM"
}

```

| Field | Type | Description |
|-------|------|-------------|
| `success` | bool | Authorization status |

#### KYC_RESP
**Middleware → Trumo** | Decision to continue or cancel transaction
```json
{
    "UUID": "b155cfe1-be2a-4f50-8716-5f6ef90e4720",
    "type": "payerDetails",
    "data": {
        "response": "proceed",
        "payerDetails": {
            "merchantPayerID": "12345678",
            "trumoPayerID": "4329ceae-0bd6-47c8-a673-f75197164f71"
        },
        "orderDetails": {
            "merchantOrderID": "i9eGxCiLGwv_wHN0CDuBBcFLoxvEIpIcixgQoibZ_9ey0w6Vvg11kbbjwb2AZ8en",
            "trumoOrderID": "14e4baea-991c-4d57-9f8b-afa1f8fadf5b"
        }
    }
}
```

| `response` value | Description |
|------------------|-------------|
| `proceed` | Continue transaction |
| `proceedWithLimit` | Continue with limits (minAmount, maxAmount) |
| `cancel` | Cancel transaction |

#### SUCCESS_REDIRECT
**Trumo → Middleware → Client** | User redirect after successful payment. Middleware receives call from Trumo, retrieves autologin URL from session record and redirects user to it

Redirect URL:
```
/success?messageid={MessageId}
```

Middleware returns HTML page with automatic redirect:
```html
<html>
<body>
    <h1>Maksu onnistui</h1>
    <a id="redirectLink" href="{SuccessLoginUrl}">Jatka kasinolle</a>
    <script>
        window.parent.postMessage({ type: 'PAYMENT_SUCCESS', url: '{SuccessLoginUrl}' }, '*');
        document.getElementById('redirectLink').click();
    </script>
</body>
</html>
```

#### PAYPROV_NOTIFY_FWD
**Trumo → Middleware → Carouseller** | Notification forwarding to Carouseller

Middleware forwards incoming notification from Trumo to Carouseller without modification.

`POST https://a.papaya.ninja/gate/trumo/`

Request body — original notification from Trumo (JSON).

#### PAYPROV_NOTIFY_RESP
**Carouseller → Middleware → Trumo** | Notification response

Middleware returns response from Carouseller back to Trumo without modification.

---

## Session Storage

### MongoDB

**Database:** `Sessions`
**Collection:** `Deposit`

**Document structure:**
```json
{
    "_id": "Rg0F6aXpA1o4pLMSTdlnsQrJhQqTL4mrRMOI4Jp9rmk",
    "PaymentProvider": "Trumo",
    "Email": "user@example.com",
    "Currency": "EUR",
    "PartnerId": "partner123",
    "SuccessLoginUrl": "https://hittikasino.com/autologin/?...",
    "RequestOrigin": "https://casino.com",
    "CreatedAt": "2024-01-14T12:30:00Z",
    "ExpiresAt": "2024-01-15T12:30:00Z"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `_id` | string | MessageId (primary key) |
| `PaymentProvider` | string | "Trumo" or "Trustly" |
| `Email` | string | User email |
| `Currency` | string | Transaction currency |
| `PartnerId` | string? | Partner ID |
| `SuccessLoginUrl` | string? | Redirect link to registered user (filled after KYC) |
| `RequestOrigin` | string? | Request origin |
| `CreatedAt` | DateTime | Creation time |
| `ExpiresAt` | DateTime | Expiration time (TTL) |

**TTL index:** Documents are automatically deleted after 24 hours.

---

## Trustly Integration

### Alternative Payment Provider

Trustly is supported as an alternative to Trumo with a similar flow.

### Endpoints

| Endpoint | Description |
|----------|-------------|
| `POST /trustly/deposit` | Deposit initiation |
| `POST /trustly/notifications` | Webhook notifications |

---

## Configuration

### Main Parameters (appsettings.json)

```json
{
    "ConnectionStrings": {
        "MongoDB": "mongodb+srv://...@cluster0.cgjc3.mongodb.net/Sessions",
        "MongoDBLogs": "mongodb+srv://...@cluster0.cgjc3.mongodb.net/Logs"
    },
    "PaymentApi": {
        "AesEncryptionKey": "VjdwUn8zUWOkqaK7mHb4TYiGCVlM9GNe"
    },
    "TrumoApi": {
        "MerchantId": "68ff32c9f200f31ee5f57bfd",
        "ApiBaseUrl": "https://api-stg.trumo.io/v1",
        "NotificationUrl": "https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/trumo/notifications",
        "PrivateKeyFileName": "client_trumo_private.pem",
        "PublicKeyFileName": "server_trumo_public_staging.pem"
    },
    "TrustlyApi": {
        "ApiBaseUrl": "https://test.trustly.com/api/1",
        "Username": "hitticasino_pnph",
        "Password": "72546f67-8e45-47e6-93d6-af8bf40b0c9b",
        "NotificationUrl": "https://tms-acctdbazacbnbvda.westeurope-01.azurewebsites.net/trustly/notifications",
        "PrivateKeyFileName": "client_trustly_private.pem",
        "PublicKeyFileName": "server_trustly_public_test.pem"
    }
}
```
