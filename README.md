# playoffs 2.0

This guide describes how to run the entire PlayOffs application stack (backend API and frontend) using Docker Compose.

## Prerequisites

- Docker Desktop installed and running
- Docker Compose (included with Docker Desktop)
- Git (optional, for cloning)

## Quick Start

### 1. Start Backend Services

From the project root directory, run:

```bash
docker compose up --build
```

For production testing mode, use:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build
```

This automatically starts:
- **PostgreSQL** database (port 5432)
- **Redis** cache (port 6379)
- **Elasticsearch** search engine (port 9200)
- **RabbitMQ** message broker (port 5672)
- **PlayOffs API** backend (port 5000)

### 2. Verify Backend is Running

Open your browser and go to:
```
http://localhost:5000/swagger/
```

You should see the Swagger API documentation with all available endpoints.

### 3. Start Frontend

In a **new terminal** window, navigate to the `front/` folder:

```bash
cd front
npm install
npm run dev
```

The frontend will start at:
```
http://localhost:5173/
```

## What Docker Compose Handles

✅ Database creation (PostgreSQL "playoffs" database)  
✅ Schema initialization (from `back/api/schema.sql`)  
✅ Service networking (all containers can communicate)  
✅ Health checks (waits for services to be ready before starting API)  
✅ Volume management (persistent PostgreSQL data)  

## Stopping Everything

To stop all services:

```bash
docker compose down
```

To stop and remove all data (fresh start):

```bash
docker compose down -v
```

or 

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml down -v
```

## Environment Variables

The docker-compose.yml is pre-configured with default values:
- PostgreSQL user: `postgres`
- PostgreSQL password: `123456`
- PostgreSQL database: `playoffs`
- RabbitMQ user: `guest`
- RabbitMQ password: `guest`

To customize, edit `docker-compose.yml` under the `environment:` section.

## Troubleshooting

### API crashes on startup
- Check logs: `docker compose logs api`
- Verify all services are healthy: `docker compose ps`

### Database connection fails
- Remove old volumes: `docker compose down -v`
- Rebuild: `docker compose up --build`

### Frontend can't connect to API
- Ensure API is running: `docker compose ps`
- Check browser console for CORS errors
- Verify API is accessible at `http://localhost:5000/swagger/`

## Architecture

```
Browser (http://localhost:5173/)
    ↓
Frontend (Vite - port 5173)
    ↓
API (ASP.NET Core - port 5000)
    ↓
PostgreSQL (port 5432) + Redis (port 6379) + Elasticsearch (port 9200) + RabbitMQ (port 5672)
```

## Next Steps

1. Access the frontend at `http://localhost:5173/`
2. Use the API documentation at `http://localhost:5000/swagger/`
3. Check RabbitMQ management UI at `http://localhost:15672/` (default: guest/guest)
