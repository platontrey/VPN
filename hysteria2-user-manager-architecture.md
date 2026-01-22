# Hysteria2 User Management System - Architecture Design

## Overview

A comprehensive Go application for managing Hysteria2 VPN users with PostgreSQL, Redis caching, REST API, and web panel interface.

## Technology Stack

### Backend
- **Go 1.21+**: Main application language
- **Gin**: HTTP web framework for REST API
- **GORM**: PostgreSQL ORM
- **Redis**: Caching and session storage
- **Echo**: Alternative web framework (optional)
- **JWT**: Authentication tokens
- **WebSocket**: Real-time updates

### Frontend (Web Panel)
- **React 18+**: UI framework
- **TypeScript**: Type safety
- **Ant Design**: UI component library
- **React Query**: Data fetching and caching
- **React Router**: Navigation
- **Axios**: HTTP client

### Database & Caching
- **PostgreSQL 15+**: Primary database
- **Redis 7+**: Caching and sessions
- **Flyway**: Database migrations

### DevOps & Deployment
- **Docker**: Containerization
- **Docker Compose**: Local development
- **Kubernetes**: Production deployment
- **Nginx**: Reverse proxy
- **Prometheus**: Monitoring
- **Grafana**: Visualization

## Project Structure

```
hysteria2-user-manager/
├── cmd/
│   ├── api/
│   │   └── main.go
│   ├── migrator/
│   │   └── main.go
│   └── websocket/
│       └── main.go
├── internal/
│   ├── config/
│   │   ├── config.go
│   │   └── loader.go
│   ├── database/
│   │   ├── postgres.go
│   │   ├── redis.go
│   │   └── migrations/
│   ├── models/
│   │   ├── user.go
│   │   ├── device.go
│   │   ├── session.go
│   │   ├── traffic.go
│   │   └── config.go
│   ├── repositories/
│   │   ├── interfaces/
│   │   │   ├── user_repository.go
│   │   │   ├── device_repository.go
│   │   │   └── traffic_repository.go
│   │   ├── user_repository.go
│   │   ├── device_repository.go
│   │   └── traffic_repository.go
│   ├── services/
│   │   ├── interfaces/
│   │   │   ├── user_service.go
│   │   │   ├── auth_service.go
│   │   │   ├── hysteria_service.go
│   │   │   └── traffic_service.go
│   │   ├── user_service.go
│   │   ├── auth_service.go
│   │   ├── hysteria_service.go
│   │   ├── traffic_service.go
│   │   └── websocket_service.go
│   ├── handlers/
│   │   ├── auth_handler.go
│   │   ├── user_handler.go
│   │   ├── device_handler.go
│   │   ├── traffic_handler.go
│   │   └── websocket_handler.go
│   ├── middleware/
│   │   ├── auth.go
│   │   ├── cors.go
│   │   ├── logging.go
│   │   ├── rate_limit.go
│   │   └── validation.go
│   ├── validators/
│   │   ├── user_validator.go
│   │   └── device_validator.go
│   ├── utils/
│   │   ├── crypto.go
│   │   ├── jwt.go
│   │   ├── response.go
│   │   └── errors.go
│   └── hysteria/
│       ├── config_generator.go
│       ├── client_manager.go
│       └── protocol_handler.go
├── pkg/
│   ├── logger/
│   │   └── logger.go
│   ├── cache/
│   │   ├── redis_cache.go
│   │   └── cache_interface.go
│   └── events/
│       ├── event_bus.go
│       └── events.go
├── migrations/
│   ├── 001_create_users_table.sql
│   ├── 002_create_devices_table.sql
│   ├── 003_create_sessions_table.sql
│   ├── 004_create_traffic_stats_table.sql
│   └── 005_create_configs_table.sql
├── web/
│   ├── public/
│   ├── src/
│   │   ├── components/
│   │   ├── pages/
│   │   ├── hooks/
│   │   ├── services/
│   │   ├── types/
│   │   └── utils/
│   ├── package.json
│   └── tsconfig.json
├── docker/
│   ├── Dockerfile.api
│   ├── Dockerfile.web
│   └── docker-compose.yml
├── configs/
│   ├── app.yaml
│   ├── database.yaml
│   └── hysteria.yaml
├── scripts/
│   ├── build.sh
│   ├── deploy.sh
│   └── migrate.sh
├── docs/
│   ├── api.md
│   ├── deployment.md
│   └── architecture.md
├── go.mod
├── go.sum
├── Makefile
└── README.md
```

## Database Schema Design

### PostgreSQL Tables

```sql
-- Users table
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username VARCHAR(50) UNIQUE NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    full_name VARCHAR(100),
    status VARCHAR(20) DEFAULT 'active' CHECK (status IN ('active', 'suspended', 'deleted')),
    role VARCHAR(20) DEFAULT 'user' CHECK (role IN ('admin', 'user')),
    data_limit BIGINT DEFAULT 0, -- bytes
    data_used BIGINT DEFAULT 0, -- bytes
    expiry_date TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    last_login TIMESTAMP WITH TIME ZONE,
    notes TEXT
);

-- Devices table
CREATE TABLE devices (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name VARCHAR(100) NOT NULL,
    device_id VARCHAR(255) UNIQUE NOT NULL, -- Hysteria2 client ID
    public_key VARCHAR(255) NOT NULL,
    ip_address INET,
    status VARCHAR(20) DEFAULT 'active' CHECK (status IN ('active', 'inactive', 'blocked')),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    last_seen TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    data_used BIGINT DEFAULT 0
);

-- Sessions table
CREATE TABLE sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_id UUID REFERENCES devices(id) ON DELETE CASCADE,
    session_token VARCHAR(255) UNIQUE NOT NULL,
    refresh_token VARCHAR(255) UNIQUE,
    ip_address INET,
    user_agent TEXT,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    is_active BOOLEAN DEFAULT true
);

-- Traffic statistics table
CREATE TABLE traffic_stats (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_id UUID REFERENCES devices(id) ON DELETE CASCADE,
    upload_bytes BIGINT DEFAULT 0,
    download_bytes BIGINT DEFAULT 0,
    total_bytes BIGINT GENERATED ALWAYS AS (upload_bytes + download_bytes) STORED,
    recorded_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Hysteria2 configurations table
CREATE TABLE hysteria_configs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_id UUID REFERENCES devices(id) ON DELETE CASCADE,
    config_name VARCHAR(100) NOT NULL,
    config_data JSONB NOT NULL,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Indexes for performance
CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_users_username ON users(username);
CREATE INDEX idx_users_status ON users(status);
CREATE INDEX idx_devices_user_id ON devices(user_id);
CREATE INDEX idx_devices_device_id ON devices(device_id);
CREATE INDEX idx_sessions_user_id ON sessions(user_id);
CREATE INDEX idx_sessions_token ON sessions(session_token);
CREATE INDEX idx_traffic_user_id ON traffic_stats(user_id);
CREATE INDEX idx_traffic_recorded_at ON traffic_stats(recorded_at);
CREATE INDEX idx_traffic_device_id ON traffic_stats(device_id);
```

## Redis Caching Strategy

### Cache Keys Structure

```
# User sessions
session:{token} -> {user_id, device_id, expires_at}
user_sessions:{user_id} -> Set[session_tokens]

# User data cache
user:{user_id} -> {user_data}
user_devices:{user_id} -> Set[device_ids]

# Device data
device:{device_id} -> {device_data}
device_status:{device_id} -> {status, last_seen}

# Traffic statistics (temporary)
traffic:{user_id}:{date} -> {upload, download, total}
traffic_device:{device_id}:{date} -> {upload, download, total}

# Rate limiting
rate_limit:{ip}:{endpoint} -> counter
rate_limit_user:{user_id}:{endpoint} -> counter

# Hysteria2 config cache
hysteria_config:{user_id}:{device_id} -> {config_data}
hysteria_active_devices -> Set[active_device_ids]

# Authentication tokens
auth_token:{token} -> {user_id, expires_at}
refresh_token:{token} -> {user_id, expires_at}
```

### Cache TTL Settings

```go
const (
    // Session tokens - 24 hours
    SessionTTL = 24 * time.Hour
    
    // User data - 1 hour
    UserDataTTL = time.Hour
    
    // Device data - 30 minutes
    DeviceDataTTL = 30 * time.Minute
    
    // Traffic stats - 5 minutes
    TrafficTTL = 5 * time.Minute
    
    // Rate limiting - 1 minute
    RateLimitTTL = time.Minute
    
    // Hysteria configs - 2 hours
    ConfigTTL = 2 * time.Hour
)
```

## REST API Endpoints Structure

### Authentication Endpoints

```
POST   /api/v1/auth/register
POST   /api/v1/auth/login
POST   /api/v1/auth/refresh
POST   /api/v1/auth/logout
GET    /api/v1/auth/me
POST   /api/v1/auth/forgot-password
POST   /api/v1/auth/reset-password
```

### User Management Endpoints

```
GET    /api/v1/users                    # List users (paginated)
POST   /api/v1/users                    # Create user
GET    /api/v1/users/{id}               # Get user details
PUT    /api/v1/users/{id}               # Update user
DELETE /api/v1/users/{id}               # Delete user
POST   /api/v1/users/{id}/suspend       # Suspend user
POST   /api/v1/users/{id}/activate      # Activate user
GET    /api/v1/users/{id}/traffic       # Get user traffic stats
POST   /api/v1/users/{id}/reset-data    # Reset user data usage
```

### Device Management Endpoints

```
GET    /api/v1/users/{user_id}/devices  # List user devices
POST   /api/v1/users/{user_id}/devices  # Register new device
GET    /api/v1/devices/{id}             # Get device details
PUT    /api/v1/devices/{id}             # Update device
DELETE /api/v1/devices/{id}             # Remove device
POST   /api/v1/devices/{id}/block       # Block device
POST   /api/v1/devices/{id}/unblock     # Unblock device
```

### Traffic & Statistics Endpoints

```
GET    /api/v1/traffic/users/{user_id}  # User traffic stats
GET    /api/v1/traffic/devices/{id}     # Device traffic stats
GET    /api/v1/traffic/summary          # Global traffic summary
GET    /api/v1/traffic/export           # Export traffic data
```

### Hysteria2 Configuration Endpoints

```
GET    /api/v1/hysteria/config/{user_id}/{device_id}
POST   /api/v1/hysteria/config/{user_id}/{device_id}
PUT    /api/v1/hysteria/config/{user_id}/{device_id}
DELETE /api/v1/hysteria/config/{user_id}/{device_id}
POST   /api/v1/hysteria/reload          # Reload Hysteria2 config
GET    /api/v1/hysteria/status          # Hysteria2 service status
```

### Admin Endpoints

```
GET    /api/v1/admin/dashboard          # Dashboard stats
GET    /api/v1/admin/users              # User management
GET    /api/v1/admin/traffic            # Traffic analytics
POST   /api/v1/admin/maintenance        # Maintenance mode
GET    /api/v1/admin/logs               # System logs
```

## Web Panel Technology Stack

### Frontend Architecture

```typescript
// Component structure
src/
├── components/
│   ├── common/           # Reusable components
│   ├── layout/           # Layout components
│   ├── users/            # User management components
│   ├── devices/          # Device management components
│   ├── traffic/          # Traffic visualization
│   └── admin/            # Admin panel components
├── pages/
│   ├── Dashboard.tsx
│   ├── Users.tsx
│   ├── Devices.tsx
│   ├── Traffic.tsx
│   ├── Settings.tsx
│   └── Login.tsx
├── hooks/
│   ├── useAuth.ts
│   ├── useUsers.ts
│   ├── useDevices.ts
│   └── useWebSocket.ts
├── services/
│   ├── api.ts
│   ├── auth.ts
│   ├── users.ts
│   └── websocket.ts
├── types/
│   ├── user.ts
│   ├── device.ts
│   ├── traffic.ts
│   └── api.ts
└── utils/
    ├── constants.ts
    ├── helpers.ts
    └── validators.ts
```

### Key Dependencies

```json
{
  "dependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "react-router-dom": "^6.8.0",
    "antd": "^5.2.0",
    "@ant-design/icons": "^5.0.0",
    "@tanstack/react-query": "^4.24.0",
    "axios": "^1.3.0",
    "recharts": "^2.5.0",
    "dayjs": "^1.11.0",
    "socket.io-client": "^4.6.0"
  },
  "devDependencies": {
    "@types/react": "^18.0.0",
    "@types/react-dom": "^18.0.0",
    "typescript": "^4.9.0",
    "vite": "^4.1.0",
    "eslint": "^8.34.0",
    "@typescript-eslint/eslint-plugin": "^5.52.0"
  }
}
```

## Hysteria2 Configuration Management

### Config Generator

```go
type HysteriaConfig struct {
    Server   string                 `json:"server"`
    Auth     string                 `json:"auth"`
    Bandwidth *BandwidthConfig      `json:"bandwidth"`
    SOCKS5   *SOCKS5Config          `json:"socks5"`
    HTTP     *HTTPConfig            `json:"http"`
    QUIC     *QUICConfig            `json:"quic"`
    Lazy     bool                   `json:"lazy"`
    ACL      *ACLConfig             `json:"acl"`
}

type BandwidthConfig struct {
    Up   int `json:"up"`
    Down int `json:"down"`
}

type UserConfig struct {
    ID       string `json:"id"`
    Password string `json:"password"`
    Upload   int64  `json:"upload"`
    Download int64  `json:"download"`
    Total    int64  `json:"total"`
    Expires  int64  `json:"expires,omitempty"`
}
```

### Config Management Service

```go
type HysteriaService interface {
    GenerateUserConfig(userID, deviceID string) (*HysteriaConfig, error)
    UpdateUserConfig(userID, deviceID string, config *HysteriaConfig) error
    ReloadConfiguration() error
    GetActiveConnections() ([]Connection, error)
    DisconnectUser(userID, deviceID string) error
}
```

## Security and Authentication

### JWT Implementation

```go
type JWTClaims struct {
    UserID   string `json:"user_id"`
    Username string `json:"username"`
    Role     string `json:"role"`
    jwt.RegisteredClaims
}

type AuthService interface {
    GenerateTokens(user *User) (*TokenPair, error)
    ValidateToken(token string) (*JWTClaims, error)
    RefreshTokens(refreshToken string) (*TokenPair, error)
    Logout(token string) error
}
```

### Security Measures

1. **Password Security**: bcrypt with cost 12
2. **JWT Tokens**: RS256 signing, 15-minute access tokens
3. **Rate Limiting**: IP-based and user-based limits
4. **CORS**: Configured for production domains
5. **Input Validation**: Comprehensive validation for all inputs
6. **SQL Injection Prevention**: Parameterized queries via GORM
7. **XSS Protection**: Input sanitization and output encoding
8. **CSRF Protection**: Double-submit cookie pattern

## Performance Optimization

### Database Optimization

1. **Connection Pooling**: GORM with configured pool size
2. **Indexing Strategy**: Proper indexes on frequently queried columns
3. **Query Optimization**: N+1 query prevention with eager loading
4. **Partitioning**: Time-based partitioning for traffic stats

### Caching Strategy

1. **Multi-level Caching**: Memory + Redis
2. **Cache Invalidation**: Event-driven cache updates
3. **Cache Warming**: Preload frequently accessed data
4. **Compression**: Compress large cached objects

### API Optimization

1. **Pagination**: Cursor-based pagination for large datasets
2. **Compression**: Gzip response compression
3. **Field Selection**: GraphQL-like field selection
4. **Batch Operations**: Bulk endpoints for mass operations

### Monitoring and Metrics

```go
type Metrics struct {
    // Request metrics
    RequestCount     prometheus.Counter
    RequestDuration  prometheus.Histogram
    ErrorCount       prometheus.Counter
    
    // User metrics
    ActiveUsers      prometheus.Gauge
    UserRegistrations prometheus.Counter
    
    // Traffic metrics
    DataTransferred  prometheus.Counter
    ActiveConnections prometheus.Gauge
    
    // Database metrics
    DBConnections    prometheus.Gauge
    DBQueryDuration  prometheus.Histogram
}
```

## Key Dependencies (Go)

```go
// go.mod
module hysteria2-user-manager

go 1.21

require (
    github.com/gin-gonic/gin v1.9.1
    github.com/golang-jwt/jwt/v5 v5.0.0
    github.com/redis/go-redis/v9 v9.0.5
    github.com/sirupsen/logrus v1.9.3
    github.com/spf13/viper v1.16.0
    github.com/swaggo/gin-swagger v1.6.0
    gorm.io/driver/postgres v1.5.2
    gorm.io/gorm v1.25.4
    github.com/google/uuid v1.3.0
    github.com/go-playground/validator/v10 v10.15.3
    github.com/prometheus/client_golang v1.16.0
    github.com/gorilla/websocket v1.5.0
    golang.org/x/crypto v0.12.0
)
```

## Implementation Patterns

### Repository Pattern

```go
type UserRepository interface {
    Create(user *User) error
    GetByID(id string) (*User, error)
    GetByEmail(email string) (*User, error)
    Update(user *User) error
    Delete(id string) error
    List(offset, limit int) ([]*User, error)
    Search(query string, offset, limit int) ([]*User, error)
}
```

### Service Layer Pattern

```go
type UserService struct {
    userRepo UserRepository
    cache    CacheInterface
    logger   Logger
    events   EventBus
}

func (s *UserService) CreateUser(req *CreateUserRequest) (*User, error) {
    // Validation
    if err := s.validator.Struct(req); err != nil {
        return nil, err
    }
    
    // Business logic
    user := &User{
        Username: req.Username,
        Email:    req.Email,
        // ... other fields
    }
    
    // Persistence
    if err := s.userRepo.Create(user); err != nil {
        return nil, err
    }
    
    // Cache update
    s.cache.Set(fmt.Sprintf("user:%s", user.ID), user, UserDataTTL)
    
    // Event publishing
    s.events.Publish(UserCreatedEvent{User: user})
    
    return user, nil
}
```

### Dependency Injection

```go
// Container setup
func setupContainer() *Container {
    return &Container{
        Database:    setupDatabase(),
        Redis:       setupRedis(),
        Logger:      setupLogger(),
        
        Repositories: &Repositories{
            UserRepo:    repositories.NewUserRepository(db),
            DeviceRepo:  repositories.NewDeviceRepository(db),
            TrafficRepo: repositories.NewTrafficRepository(db),
        },
        
        Services: &Services{
            UserService:    services.NewUserService(userRepo, cache, logger),
            AuthService:    services.NewAuthService(userRepo, jwt, logger),
            HysteriaService: services.NewHysteriaService(config, logger),
        },
        
        Handlers: &Handlers{
            UserHandler: handlers.NewUserHandler(userService, logger),
            AuthHandler: handlers.NewAuthHandler(authService, logger),
        },
    }
}
```

This architecture provides a solid foundation for a scalable, maintainable Hysteria2 user management system with clean separation of concerns, proper security measures, and excellent performance characteristics.