# Networld Parking Lot - Gate Operation API

This backend is the first focused MVP for **Networld Parking Lot**. It covers the gate operation flow only:

- Check company before vehicle entry
- Validate active subscription and slot availability
- Show payment due before entry
- Generate barcode number
- Allow entry and activate barcode
- Reject entry and invalidate barcode
- Scan barcode on exit
- Detect invalid / used barcode
- Detect overstay
- Show payment required
- Collect payment
- Create extra slot invoice from gate screen
- Allow exit and invalidate barcode
- Live parking list
- Recent gate activity
- Outside display latest status

## Project structure

```text
NetworldParkingLot.Api/
  Common/
  Data/
  Domain/
    Constants/
    Entities/
  Features/
    GateOperations/
      Controllers/
      Dtos/
      Repositories/
      Services/
  SQL/
```

## Step 1: Create database

Open SSMS and run these scripts in order:

```text
SQL/001_Create_GateOperation_Tables.sql
SQL/002_Seed_GateOperation_Data.sql
SQL/003_GateOperation_Views.sql
```

## Step 2: Set connection string

Update `appsettings.json`:

```json
"DefaultConnection": "Server=.;Database=NetworldParkingLot;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

For SQL authentication:

```json
"DefaultConnection": "Server=YOUR_SERVER;Database=NetworldParkingLot;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

## Step 3: Restore packages and run

```bash
dotnet restore
dotnet run
```

Swagger will open at:

```text
https://localhost:xxxx/swagger
```

## Important API endpoints

### Gate summary

```http
GET /api/gate-operation/summary
```

### Company search

```http
GET /api/gate-operation/companies/search?searchText=Company A
```

### Check company before entry

```http
POST /api/gate-operation/entry/check-company
```

Body:

```json
{
  "searchText": "COMP-001",
  "operatorId": 1
}
```

### Generate barcode

```http
POST /api/gate-operation/entry/generate-barcode
```

Body:

```json
{
  "companyId": 1,
  "plateNo": "DXB 45821",
  "vehicleType": "Car",
  "driverName": "",
  "driverMobile": "",
  "remarks": "",
  "allowPaymentDueWarning": true,
  "operatorId": 1
}
```

### Allow entry

```http
POST /api/gate-operation/entry/allow
```

Body:

```json
{
  "sessionId": 1,
  "allowPaymentDueWarning": true,
  "operatorId": 1
}
```

### Reject entry

```http
POST /api/gate-operation/entry/reject
```

Body:

```json
{
  "sessionId": 1,
  "operatorId": 1,
  "reason": "Customer refused payment"
}
```

### Scan exit barcode

```http
POST /api/gate-operation/exit/scan
```

Body:

```json
{
  "barcodeNo": "KP260520000001",
  "operatorId": 1
}
```

### Collect payment

```http
POST /api/gate-operation/payment/collect
```

Body:

```json
{
  "companyId": 1,
  "invoiceId": null,
  "sessionId": 1,
  "amount": 100,
  "paymentMode": "Cash",
  "referenceNo": "",
  "remarks": "Gate collection",
  "savePaymentAndAllowExit": false,
  "operatorId": 1
}
```

### Allow exit

```http
POST /api/gate-operation/exit/allow
```

Body:

```json
{
  "sessionId": 1,
  "barcodeNo": null,
  "forceAllow": false,
  "forceReason": null,
  "operatorId": 1
}
```

If you want to allow exit even when amount is pending, send:

```json
{
  "sessionId": 1,
  "forceAllow": true,
  "forceReason": "Approved by owner",
  "operatorId": 1
}
```

### Create extra slot invoice

```http
POST /api/gate-operation/extra-slots/invoice
```

Body:

```json
{
  "companyId": 1,
  "additionalSlots": 5,
  "planType": "Daily",
  "ratePerSlot": 50,
  "startDate": "2026-05-20",
  "endDate": "2026-05-21",
  "discountAmount": 0,
  "vatAmount": 0,
  "paidAmount": 0,
  "paymentMode": "Cash",
  "referenceNo": "",
  "remarks": "Extra slots from gate",
  "operatorId": 1
}
```

### Live parking

```http
GET /api/gate-operation/live-parking
```

### Recent activity

```http
GET /api/gate-operation/recent-activity?take=25
```

### Outside display latest event

```http
GET /api/gate-operation/outside-display/latest
```

The outside display Flutter page can poll this API every 1 or 2 seconds.

## Flutter integration flow

### Entry mode

```text
1. Search company
2. POST entry/check-company
3. If entryStatus is EntryAllowed or PaymentDue with warning approval:
4. POST entry/generate-barcode
5. Print barcode from Flutter Windows app
6. POST entry/allow
```

### Exit mode

```text
1. Scanner enters barcode into Flutter TextField
2. POST exit/scan
3. If finalStatus = ClearToExit -> POST exit/allow
4. If finalStatus = PaymentRequired -> POST payment/collect
5. Then POST exit/allow
```

## Barcode design

Barcode value example:

```text
KP260520000001
```

The barcode contains only the unique ID. All details are fetched from SQL Server.

## Important MVP fraud controls

- Barcode cannot be reused after exit.
- Same plate cannot enter if already inside.
- Entry is blocked if slots are full.
- Entry can show/block payment due depending on `SystemSettings.BlockEntryIfPaymentDue`.
- Exit is blocked if payment or overstay amount exists.
- Force exit requires reason.
- All gate actions are stored in `GateActivityLogs`.
- Outside display shows the latest scan status.

## Next phase

After this gate MVP is working, add:

- Login/JWT
- Company master APIs
- Full subscription APIs
- Full invoice APIs
- Full reports
- Printer helper for raw ZPL/TSPL
- SignalR for outside display instead of polling
```
