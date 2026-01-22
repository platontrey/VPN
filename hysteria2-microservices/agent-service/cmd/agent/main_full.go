package main

import (
	"context"
	"fmt"
	"log"
	"net"
	"os"
	"os/signal"
	"syscall"

	"github.com/sirupsen/logrus"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
	"hysteria2-microservices/agent-service/internal/config"
	"hysteria2-microservices/agent-service/internal/handlers"
	"hysteria2-microservices/agent-service/internal/services"
	pb "hysteria2-microservices/proto"
)

func main() {
	// Load configuration
	cfg, err := config.LoadConfig()
	if err != nil {
		log.Fatalf("Failed to load config: %v", err)
	}

	// Setup logger
	logger := setupLogger(cfg.Logging)

	// Initialize services
	localServices := setupLocalServices(cfg, logger)

	// Setup gRPC client to master server
	masterClient, err := setupMasterClient(cfg, logger)
	if err != nil {
		logger.Fatalf("Failed to connect to master server: %v", err)
	}
	defer func() {
		if masterClient != nil {
			masterClient.Close()
		}
	}()

	// Setup gRPC server for master commands
	grpcServer := setupGRPCServer(localServices, masterClient, logger)

	// Start agent
	agent := handlers.NewAgent(localServices, masterClient, cfg, logger)

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	// Start agent in background
	go agent.Start(ctx)

	// Start gRPC server
	go startGRPCServer(grpcServer, cfg, logger)

	// Wait for interrupt signal
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit

	logger.Info("Shutting down agent...")
	cancel()
	grpcServer.GracefulStop()
	logger.Info("Agent stopped")
}

func setupLogger(cfg config.LoggingConfig) *logrus.Logger {
	logger := logrus.New()

	level, err := logrus.ParseLevel(cfg.Level)
	if err != nil {
		level = logrus.InfoLevel
	}
	logger.SetLevel(level)

	if cfg.Format == "json" {
		logger.SetFormatter(&logrus.JSONFormatter{})
	} else {
		logger.SetFormatter(&logrus.TextFormatter{
			FullTimestamp: true,
		})
	}

	return logger
}

func setupLocalServices(cfg *config.Config, logger *logrus.Logger) *services.LocalServices {
	return &services.LocalServices{
		ConfigManager:    services.NewConfigManager(logger),
		MetricsCollector: services.NewMetricsCollector(cfg, logger),
		SystemManager:    services.NewSystemManager(logger),
		NetworkManager:   services.NewNetworkManager(logger),
		HysteriaManager:  services.NewHysteriaManager(logger, cfg),
	}
}

func setupMasterClient(cfg *config.Config, logger *logrus.Logger) (pb.MasterServiceClient, error) {
	if cfg.MasterServer == "" {
		logger.Warn("No master server configured, running in standalone mode")
		return nil, nil
	}

	conn, err := grpc.Dial(cfg.MasterServer, grpc.WithTransportCredentials(insecure.NewCredentials()))
	if err != nil {
		return nil, fmt.Errorf("failed to connect to master server: %w", err)
	}

	client := pb.NewMasterServiceClient(conn)
	logger.Infof("Connected to master server: %s", cfg.MasterServer)

	return client, nil
}

func setupGRPCServer(localServices *services.LocalServices, masterClient pb.MasterServiceClient, logger *logrus.Logger) *grpc.Server {
	s := grpc.NewServer()

	// Register node manager service
	pb.RegisterNodeManagerServer(s, handlers.NewNodeManagerHandler(localServices, logger))

	return s
}

func startGRPCServer(s *grpc.Server, cfg *config.Config, logger *logrus.Logger) {
	addr := fmt.Sprintf(":%d", cfg.Node.GRPCPort)
	lis, err := net.Listen("tcp", addr)
	if err != nil {
		logger.Fatalf("Failed to listen on %s: %v", addr, err)
	}

	logger.Infof("Starting gRPC server on %s", addr)
	if err := s.Serve(lis); err != nil {
		logger.Fatalf("Failed to start gRPC server: %v", err)
	}
}
