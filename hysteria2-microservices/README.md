# Hysteria2 VPN - Распределенная архитектура

## Обзор

Система управления распределенной VPN сетью Hysteria2 с Master-сервером и множественными VPS узлами.

## Архитектура

### Master Server (Центральный сервер управления)
- **Orchestrator Service**: Управление VPS узлами
- **API Service**: REST API и web интерфейс  
- **Database**: PostgreSQL + Redis
- **GRPC Server**: Управление узлами

### VPS Nodes (Узлы сети)
- **Agent Service**: gRPC агент для связи с master
- **Hysteria2 Service**: Core VPN сервер
- **Metrics Collector**: Сбор метрик производительности

## Структура проекта

```
hysteria2-microservices/
├── orchestrator-service/     # Master сервер
│   ├── cmd/server/          # Основной сервер
│   ├── internal/
│   │   ├── config/         # Конфигурация
│   │   ├── models/         # Модели данных
│   │   ├── repositories/   # Репозитории
│   │   └── services/       # Бизнес-логика
│   └── pkg/proto/          # gRPC протоколы
├── agent-service/          # VPS агент
│   ├── cmd/agent/          # Основной агент
│   ├── internal/
│   │   ├── config/         # Конфигурация
│   │   ├── handlers/       # gRPC обработчики
│   │   └── services/       # Локальные сервисы
│   └── pkg/proto/          # gRPC протоколы
├── api-service/            # Существующий API
├── web-service/            # Существующий web UI
├── proto/                  # Общие .proto файлы
├── migrations/             # Миграции БД
├── deployments/            # Docker конфиги
└── scripts/               # Скрипты

```

## Коммуникация

### gRPC Протоколы

#### Master → Node
- `UpdateConfig`: Обновление конфигурации
- `ReloadConfig`: Перезагрузка сервисов
- `GetStatus`: Получение статуса узла
- `RestartServer`: Перезапуск сервера
- `GetLogs`: Получение логов

#### Node → Master  
- `RegisterNode`: Регистрация нового узла
- `Heartbeat`: Периодические heartbeat
- `ReportMetrics`: Отправка метрик
- `ReportEvent`: Отчет о событиях

## Быстрый старт

### 1. Настройка Master сервера
```bash
cd orchestrator-service
go mod tidy
go run cmd/server/main.go
```

### 2. Настройка VPS агента
```bash
cd agent-service
go mod tidy
go run cmd/agent/main.go
```

### 3. Запуск через Docker
```bash
cd deployments/docker
docker-compose up -d
```

## Конфигурация

### Master сервер (.env)
```env
# Database
DB_HOST=localhost
DB_PORT=5432
DB_USER=postgres
DB_PASSWORD=postgres
DB_NAME=hysteryvpn

# GRPC
GRPC_PORT=50052
GRPC_HOST=0.0.0.0

# Security
JWT_SECRET=your-secret-key
NODE_AUTH_TOKEN=node-auth-token
```

### VPS агент (.env)
```env
# Master connection
MASTER_SERVER=master.yourdomain.com:50052

# Node identification
NODE_ID=node-001
NODE_NAME=US-East-1
NODE_IP_ADDRESS=192.168.1.100
NODE_LOCATION=New York
NODE_COUNTRY=US

# Logging
LOG_LEVEL=info
LOG_FORMAT=json
```

## Развёртывание

### Docker Compose
```yaml
services:
  orchestrator:
    build: ./orchestrator-service
    ports:
      - "8081:8081"  # REST API
      - "50052:50052"  # gRPC
    environment:
      - DB_HOST=postgres
      - REDIS_HOST=redis
    depends_on: [postgres, redis]

  agent:
    build: ./agent-service
    environment:
      - MASTER_SERVER=orchestrator:50052
      - NODE_ID=node-001
```

## API документация

### Управление узлами
```
GET    /api/v1/nodes              # Список узлов
POST   /api/v1/nodes              # Регистрация узла
GET    /api/v1/nodes/{id}         # Детали узла
PUT    /api/v1/nodes/{id}         # Обновление узла
DELETE /api/v1/nodes/{id}         # Удаление узла
```

### Метрики и мониторинг
```
GET    /api/v1/nodes/{id}/metrics    # Метрики узла
GET    /api/v1/nodes/{id}/logs        # Логи узла
POST   /api/v1/nodes/{id}/restart     # Перезапуск узла
```

## Безопасность

1. **mTLS аутентификация** между master и узлами
2. **JWT токены** для API аутентификации  
3. **Rate limiting** для защиты от DDoS
4. **Шифрование** всех gRPC коммуникаций

## Мониторинг

- **Метрики производительности**: CPU, RAM, Network
- **Трафик пользователей**: Upload/Download statistics
- **Статус узлов**: Online/Offline/Maintenance
- **История развертываний**: Deployment tracking

## Разработка

### Генерация gRPC кода
```bash
./scripts/generate-proto.sh
```

### Запуск в режиме разработки
```bash
# Master server
cd orchestrator-service && go run cmd/server/main.go

# Agent
cd agent-service && go run cmd/agent/main.go
```

## Следующие шаги

1. ✅ Создать структуру microservices
2. ✅ Определить gRPC протоколы  
3. ✅ Создать базовые модели данных
4. 🔄 Реализовать gRPC коммуникацию
5. 📋 Добавить веб-интерфейс для управления узлами
6. 📋 Настроить автоматическое развертывание
7. 📋 Добавить мониторинг и алерты