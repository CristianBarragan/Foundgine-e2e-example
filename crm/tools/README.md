# CRM EF tooling
- `gen_migration.py <outdir>` regenerates InitialCrm (Up/Down), its Designer and the model snapshot from one schema spec.
- With a .NET SDK, prefer: `dotnet ef migrations add <Name> --project src/Crm.Api` (uses CrmDesignTimeFactory; set CrmConnectionString).
- The generator now targets SQL Server (nvarchar/datetime2/identity). Checkpoint 1 was written without an SDK: run `dotnet build` and `dotnet ef migrations has-pending-model-changes` first.
