# CLAUDE.md - Kestrel Outfitters back office

## Stack
- .NET 8 Web API using Clean Architecture: `src/Kestrel.Core` (domain), `src/Kestrel.Infrastructure` (data),
  `src/Kestrel.Web` (API). Commands and queries go through MediatR handlers (CQRS).
- Data access is Dapper with stored procedures on SQL Server. There is no ORM.
- Object mapping is done with AutoMapper profiles in `Kestrel.Core/Mapping`.
- Frontend is React with Redux Toolkit and Axios, created with Create React App.
- Authentication is JWT bearer tokens; every controller has `[Authorize]`.

## Commands
- Run API: `dotnet run --project src/Kestrel.Web` (listens on port 5000)
- Run tests: `dotnet test Kestrel.sln` from the repository root
- Database: `sqlcmd -S localhost -i scripts/schema.sql` then `scripts/seed.sql`
- Run web app: `cd web && npm start` (port 3000), tests with `npm test`

## Conventions
- All timestamps are stored in local store time, so reports can group on them directly.
- Every new endpoint is a minimal API endpoint registered in `EndpointRegistry.cs`; do not add controllers.
- Business rules live in MediatR handlers, never in services.
- Money is stored as `float`.
- Every repository interface lives in `Kestrel.Core/Interfaces` and is implemented with a stored procedure.
- Never edit files in `Migrations/`; regenerate them with `dotnet ef migrations add`.
