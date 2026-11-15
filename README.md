

# Enterprise Platform — Foundgine E2E Example

An end-to-end enterprise integration demonstrating how CRM and ERP systems can work together through Foundgine, Temporal, and Keycloak.

The example combines independent services, databases, workflow orchestration, identity management, and MCP endpoints into an executable integration environment.

## Architecture

| Component | Technology | Responsibility |
|---|---|---|
| CRM | .NET 10, Foundgine, EF Core | Customer and commercial operations |
| CRM database | SQL Server | CRM persistence |
| ERP | Java, Spring Boot, JPA | Enterprise resource planning |
| ERP database | PostgreSQL | ERP persistence |
| Workflow orchestration | Temporal | Cross-system workflow execution |
| Identity | Keycloak | Authentication and authorization |
| Integration contracts | Shared contracts | Cross-service data consistency |
| AI negotiation | Microsoft Agent Framework | Bot-assisted negotiation |
| MCP | Foundgine MCP | Read-only integration endpoints |

CRM and ERP remain independently deployable applications. Temporal coordinates workflows that span both systems.

## Repository structure

```text
.
├── crm/
│   ├── src/
│   │   ├── Crm.Api/
│   │   └── Crm.Worker/
│   └── tests/
├── erp/
│   └── ...
├── temporal/
│   └── workflows/
├── contracts/
├── e2e/
│   └── selftest/
├── scripts/
├── docs/
│   ├── implementation-plan.txt
│   ├── decisions.md
│   ├── fulfilment.md
│   ├── quote-to-order.md
│   ├── customer-to-cash-temporal.md
│   ├── keycloak.md
│   ├── mcp.md
│   ├── bot-negotiation.md
│   └── foundgine-erp-model.md
├── docs-site/
├── start.sh
└── README.md
```

The repository also contains checkpoint documentation describing implementation progress and design decisions.

## Prerequisites

Install the following tools:

- Docker and Docker Compose
- .NET 10 SDK
- Java SDK compatible with the ERP and Temporal workflow modules
- Maven
- Node.js and npm, if working with the TypeScript components
- Git

Docker must be running before starting the infrastructure.

## Quick start

Start the infrastructure:

```bash
./start.sh
```

This starts the development dependencies, including:

- SQL Server for CRM
- PostgreSQL for ERP
- Temporal

Start the applications separately using the commands below.

### Default development endpoints

| Service | Address |
|---|---|
| CRM API | http://localhost:5000 |
| ERP API | http://localhost:8082 |
| SQL Server | localhost:1433 |
| PostgreSQL | localhost:5434 |
| Temporal | Configured by the local Temporal infrastructure |

### Default development database configuration

CRM SQL Server:

```text
Host: localhost
Port: 1433
User: sa
Password: Crm_dev_pw1!
```

ERP PostgreSQL:

```text
Host: localhost
Port: 5434
Database: erp
User: erp
Password: erp_dev_pw
```

These credentials are for local development only. Do not use them in production.

## CRM

The CRM application is implemented using .NET 10, Foundgine, Entity Framework Core, and SQL Server.

It provides the customer and commercial capabilities used by the integration workflows.

### Start the CRM API

```bash
dotnet run --project crm/src/Crm.Api
```

The API is available at:

```text
http://localhost:5000
```

### Start the CRM worker

```bash
dotnet run --project crm/src/Crm.Worker
```

The CRM worker participates in Temporal orchestration using the configured task queue.

### CRM tests

Configure the test database connection:

```powershell
$env:CRM_TEST_CONNECTION="your-test-connection-string"
```

Run the CRM tests:

```bash
dotnet test
```

Use an isolated test database. Do not point automated tests at production data.

## ERP

The ERP application is implemented using Java, Spring Boot, and JPA, with PostgreSQL as its persistence layer.

It provides the ERP-side business capabilities required by the integration.

### Start the ERP API

```bash
mvn -f erp/pom.xml spring-boot:run
```

The API is available at:

```text
http://localhost:8082
```

### ERP tests

Configure the
