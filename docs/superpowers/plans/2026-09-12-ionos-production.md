# NexoMail IONOS Production Rollout

**Goal:** Publish the React frontend and ASP.NET Core API as one IIS application at `https://nexomail.eidosdigital.cl`, backed by IONOS SQL Server.

## 1. Production compatibility

- Add the Entity Framework Core SQL Server provider.
- Select SQLite locally and SQL Server through production configuration.
- Run SQLite legacy schema bootstraps only against SQLite.
- Persist Data Protection keys outside `wwwroot`.
- Serve the React build from ASP.NET Core and keep API routes under `/api`.
- Restrict development CORS and OpenAPI to Development.

## 2. Safe configuration

- Commit only non-secret production URLs and provider settings.
- Supply the SQL password, OAuth secrets, SMTP password, OpenAI key and billing secrets through IIS environment variables.
- Use `ConnectionStrings__NexoMail` for the database connection.
- Keep `App_Data` inaccessible from the public static-file root.

## 3. Repeatable package

- Build and lint React.
- Compile/test the .NET solution.
- Publish the API while including `frontend/dist` as `wwwroot`.
- Produce `artifacts/NexoMail-IONOS.zip`.
- Verify the package contains `wwwroot/index.html`.

## 4. Deployment and smoke tests

- Configure secrets in the generated IIS `web.config` without committing them.
- Upload the extracted publish contents to IONOS `/nexomail`.
- Restart the application and verify:
  - `GET /api/health` returns HTTP 200.
  - `GET /` returns the React application.
  - Registration and login work.
  - The SQL Server tables are created.
  - Google, Microsoft and IMAP account connections work.
- Preserve the previous webspace contents until all smoke tests pass.
