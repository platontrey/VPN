package services

import (
	"context"
	"time"

	"hysteria2-microservices/api-service/internal/models"
	repoInterfaces "hysteria2-microservices/api-service/internal/repositories/interfaces"
	serviceInterfaces "hysteria2-microservices/api-service/internal/services/interfaces"
	"hysteria2-microservices/api-service/pkg/cache"

	"github.com/google/uuid"
)

type trafficService struct {
	trafficRepo      repoInterfaces.TrafficRepository
	redis            *cache.RedisClient
	webSocketService serviceInterfaces.WebSocketService
}

func NewTrafficService(trafficRepo repoInterfaces.TrafficRepository, redis *cache.RedisClient, wsService serviceInterfaces.WebSocketService) serviceInterfaces.TrafficService {
	return &trafficService{
		trafficRepo:      trafficRepo,
		redis:            redis,
		webSocketService: wsService,
	}
}

func (s *trafficService) RecordTraffic(ctx context.Context, stats *models.TrafficStats) error {
	// Record traffic in database
	if err := s.trafficRepo.Create(ctx, stats); err != nil {
		return err
	}

	// Send real-time update via WebSocket
	if s.webSocketService != nil && s.webSocketService.IsUserConnected(stats.UserID) {
		go s.webSocketService.BroadcastTrafficUpdate(stats.UserID, stats)
	}

	return nil
}

func (s *trafficService) GetUserTraffic(ctx context.Context, userID uuid.UUID, from, to time.Time) ([]*models.TrafficStats, error) {
	return s.trafficRepo.GetByUserID(ctx, userID, from, to)
}

func (s *trafficService) GetTrafficSummary(ctx context.Context, from, to time.Time) (*models.TrafficSummary, error) {
	return s.trafficRepo.GetSummary(ctx, from, to)
}

func (s *trafficService) UpdateUserTraffic(ctx context.Context, userID uuid.UUID, upload, download int64) error {
	return s.trafficRepo.UpdateUserTraffic(ctx, userID, upload, download)
}

func (s *trafficService) UpdateDeviceTraffic(ctx context.Context, deviceID uuid.UUID, upload, download int64) error {
	return s.trafficRepo.UpdateDeviceTraffic(ctx, deviceID, upload, download)
}
