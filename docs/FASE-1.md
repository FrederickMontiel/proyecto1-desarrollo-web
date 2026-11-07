# Fase 1 — Base del proyecto

Primera entrega (agosto de 2026). Dejó la base técnica sobre la que se construyó el sistema de
monitoreo de la [Fase 2](FASE-2.md):

- Estructura del repositorio y configuración con Docker para desarrollo y producción.
- Entidades del dominio y controladores con operaciones CRUD.
- Autenticación con JWT, validación de datos de entrada con DTOs y errores estandarizados.
- Seguridad básica: CORS, cabeceras de seguridad, límite de peticiones y validación de las
  variables de entorno.
- Frontend con inicio de sesión, tablero, listados con paginación y diseño adaptable.
- Pruebas de integración.

El código de esta fase se puede consultar en el historial de Git (rama `develop`, commits del 8
al 21 de agosto de 2026). En la Fase 2 se reemplazó por el sistema de monitoreo y alerta
temprana.

| Fase | Documento | Arquitectura |
|---|---|---|
| 1 | este documento | Base del proyecto |
| 2 | [FASE-2.md](FASE-2.md) · [arquitectura](FASE-2-arquitectura.md) | Monolito .NET + SQL Server + Angular |
| 3 | [FASE-3.md](FASE-3.md) | Microservicios + API Gateway, 4 lenguajes, 3 motores |
| 4 | [FASE-4.md](FASE-4.md) | OAuth 2.0 / OIDC + Kubernetes/K3s |
