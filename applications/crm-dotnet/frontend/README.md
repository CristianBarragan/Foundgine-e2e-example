# Foundgine CRM Web Application

This is the React-based CRM web application supplied in the CRM archive and re-confirmed
by the separately uploaded `frontend.zip`.

## Stack
- React 19
- TypeScript
- Vite 6
- Keycloak JavaScript adapter
- Nginx production container
- Playwright end-to-end tests

## Commands
```bash
npm install
npm run dev
npm run build
npm run test:e2e
```

## Deployment
The CRM Docker Compose project builds this application from `./frontend` using this directory's
Dockerfile. Nginx serves the compiled Vite application and proxies configured API paths as defined
in `nginx.conf`.

## Architecture ownership
This frontend is the CRM user interface. It is not a replacement for the proposed enterprise-wide
experience layer for AI agents and cross-domain workflows. The CRM UI remains independently
deployable with the CRM application, and communicates with CRM/Foundgine APIs rather than directly
accessing the ERP database or domain model.
