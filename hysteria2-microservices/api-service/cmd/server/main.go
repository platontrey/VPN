package main

import (
	"log"
	"os"
	"os/signal"
	"syscall"
	"time"

	"hysteria2-microservices/api-service/internal/config"
	"hysteria2-microservices/api-service/internal/database"
	"hysteria2-microservices/api-service/internal/handlers"
	"hysteria2-microservices/api-service/internal/middleware"
	"hysteria2-microservices/api-service/internal/repositories"
	"hysteria2-microservices/api-service/internal/services"
	"hysteria2-microservices/api-service/pkg/cache"
	"hysteria2-microservices/api-service/pkg/logger"

	"github.com/gofiber/fiber/v2"
	"github.com/gofiber/fiber/v2/middleware/cors"
	"github.com/gofiber/fiber/v2/middleware/recover"
)

func main() {
	// Load configuration
	cfg, err := config.Load()
	if err != nil {
		log.Fatalf("Failed to load config: %v", err)
	}

	// Initialize logger
	appLogger := logger.NewLogger(cfg.LogLevel)

	// Initialize database
	db, err := database.NewConnection(cfg.DatabaseURL)
	if err != nil {
		appLogger.Fatal("Failed to connect to database", "error", err)
	}

	// Initialize Redis cache
	redisClient := cache.NewRedisClient(cfg.RedisURL)
	defer redisClient.Close()

	// Initialize repositories
	userRepo := repositories.NewUserRepository(db)
	deviceRepo := repositories.NewDeviceRepository(db)
	sessionRepo := repositories.NewSessionRepository(db)
	trafficRepo := repositories.NewTrafficRepository(db)
	nodeRepo := repositories.NewNodeRepository(db)

	// Initialize services
	authService := services.NewAuthService(userRepo, sessionRepo, redisClient, cfg.JWTSecret, time.Hour*time.Duration(cfg.JWTExpiryHour))
	userService := services.NewUserService(userRepo, deviceRepo, redisClient)
	trafficService := services.NewTrafficService(trafficRepo, redisClient, wsHandler)
	nodeService := services.NewNodeService(nodeRepo, appLogger)

	// Initialize handlers
	authHandler := handlers.NewAuthHandler(authService, appLogger)
	userHandler := handlers.NewUserHandler(userService, appLogger)
	wsHandler := handlers.NewWebSocketHandler(trafficService, appLogger)
	trafficHandler := handlers.NewTrafficHandler(trafficService, appLogger)
	nodeHandler := handlers.NewNodeHandler(nodeService, appLogger)

	// Create Fiber app
	app := fiber.New(fiber.Config{
		ErrorHandler: func(c *fiber.Ctx, err error) error {
			code := fiber.StatusInternalServerError
			if e, ok := err.(*fiber.Error); ok {
				code = e.Code
			}
			return c.Status(code).JSON(fiber.Map{
				"error": err.Error(),
			})
		},
	})

	// Middleware
	app.Use(recover.New())
	app.Use(cors.New(cors.Config{
		AllowOrigins: cfg.AllowOrigins,
		AllowHeaders: "Origin, Content-Type, Accept, Authorization",
	}))
	app.Use(middleware.Logging(appLogger))

	// Health check
	app.Get("/health", func(c *fiber.Ctx) error {
		return c.JSON(fiber.Map{"status": "ok"})
	})

	// API routes
	api := app.Group("/api/v1")

	// Public routes
	auth := api.Group("/auth")
	auth.Post("/register", authHandler.Register)
	auth.Post("/login", authHandler.Login)
	auth.Post("/refresh", authHandler.RefreshToken)

	// Protected routes
	protected := api.Group("", middleware.JWTAuth(authService))

	// User routes
	users := protected.Group("/users")
	users.Get("", userHandler.GetUsers)
	users.Post("", userHandler.CreateUser)
	users.Get("/:id", userHandler.GetUser)
	users.Put("/:id", userHandler.UpdateUser)
	users.Delete("/:id", userHandler.DeleteUser)

	// Device routes
	users.Group("/:userId/devices").Get("", userHandler.GetUserDevices)

	// Node routes
	nodes := protected.Group("/nodes")
	nodes.Get("", nodeHandler.GetNodes)
	nodes.Post("", nodeHandler.CreateNode)
	nodes.Get("/:id", nodeHandler.GetNode)
	nodes.Put("/:id", nodeHandler.UpdateNode)
	nodes.Delete("/:id", nodeHandler.DeleteNode)
	nodes.Get("/:id/metrics", nodeHandler.GetNodeMetrics)
	nodes.Post("/:id/restart", nodeHandler.RestartNode)
	nodes.Get("/:id/logs", nodeHandler.GetNodeLogs)

	// Traffic routes
	traffic := protected.Group("/traffic")
	traffic.Get("/users/:userId", trafficHandler.GetUserTraffic)
	traffic.Get("/summary", trafficHandler.GetTrafficSummary)

	// WebSocket routes
	app.Get("/ws", middleware.JWTAuth(authService), wsHandler.WebSocketUpgrade())

	// Start server
	go func() {
		if err := app.Listen(":" + cfg.Port); err != nil {
			appLogger.Fatal("Failed to start server", "error", err)
		}
	}()

	appLogger.Info("Server started", "port", cfg.Port)

	// Wait for interrupt signal to gracefully shutdown the server
	c := make(chan os.Signal, 1)
	signal.Notify(c, os.Interrupt, syscall.SIGTERM)

	<-c
	appLogger.Info("Shutting down server...")

	if err := app.Shutdown(); err != nil {
		appLogger.Error("Server forced to shutdown", "error", err)
	}

	appLogger.Info("Server exited")
}
