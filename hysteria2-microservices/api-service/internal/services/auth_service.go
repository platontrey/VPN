package services

import (
	"context"
	"crypto/rand"
	"crypto/subtle"
	"fmt"
	"time"

	"hysteria2-microservices/api-service/internal/models"
	"hysteria2-microservices/api-service/internal/repositories/interfaces"
	"hysteria2-microservices/api-service/internal/services/interfaces"
	"hysteria2-microservices/api-service/pkg/cache"

	"github.com/google/uuid"
	"golang.org/x/crypto/argon2"
)

type authService struct {
	userRepo    interfaces.UserRepository
	sessionRepo interfaces.SessionRepository
	redis       *cache.RedisClient
	jwtSecret   string
	jwtExpiry   time.Duration
}

func NewAuthService(userRepo interfaces.UserRepository, sessionRepo interfaces.SessionRepository, redis *cache.RedisClient, jwtSecret string, jwtExpiry time.Duration) interfaces.AuthService {
	return &authService{
		userRepo:    userRepo,
		sessionRepo: sessionRepo,
		redis:       redis,
		jwtSecret:   jwtSecret,
		jwtExpiry:   jwtExpiry,
	}
}

func (s *authService) Register(ctx context.Context, username, email, password string) (*models.User, error) {
	// Check if user already exists
	if _, err := s.userRepo.GetByUsername(ctx, username); err == nil {
		return nil, fmt.Errorf("username already exists")
	}
	if _, err := s.userRepo.GetByEmail(ctx, email); err == nil {
		return nil, fmt.Errorf("email already exists")
	}

	// Hash password
	hashedPassword, err := s.hashPassword(password)
	if err != nil {
		return nil, fmt.Errorf("failed to hash password: %w", err)
	}

	// Create user
	user := &models.User{
		Username: username,
		Email:    email,
		Password: hashedPassword,
		Status:   "active",
		Role:     "user",
	}

	if err := s.userRepo.Create(ctx, user); err != nil {
		return nil, fmt.Errorf("failed to create user: %w", err)
	}

	// Update last login
	if err := s.userRepo.UpdateLastLogin(ctx, user.ID); err != nil {
		// Log error but don't fail registration
		fmt.Printf("Failed to update last login: %v\n", err)
	}

	return user, nil
}

func (s *authService) Login(ctx context.Context, email, password string) (*models.User, error) {
	// Get user by email
	user, err := s.userRepo.GetByEmail(ctx, email)
	if err != nil {
		return nil, fmt.Errorf("invalid credentials")
	}

	// Check password
	if !s.verifyPassword(password, user.Password) {
		return nil, fmt.Errorf("invalid credentials")
	}

	// Check if user is active
	if user.Status != "active" {
		return nil, fmt.Errorf("account is not active")
	}

	// Update last login
	if err := s.userRepo.UpdateLastLogin(ctx, user.ID); err != nil {
		fmt.Printf("Failed to update last login: %v\n", err)
	}

	return user, nil
}

func (s *authService) GenerateTokenPair(userID uuid.UUID) (*interfaces.TokenPair, error) {
	// Generate access token
	accessToken, err := generateJWT(userID, s.jwtSecret, s.jwtExpiry)
	if err != nil {
		return nil, fmt.Errorf("failed to generate access token: %w", err)
	}

	// Generate refresh token (longer expiry)
	refreshToken, err := generateJWT(userID, s.jwtSecret, s.jwtExpiry*24)
	if err != nil {
		return nil, fmt.Errorf("failed to generate refresh token: %w", err)
	}

	return &interfaces.TokenPair{
		AccessToken:  accessToken,
		RefreshToken: refreshToken,
		ExpiresIn:    int64(s.jwtExpiry.Seconds()),
	}, nil
}

func (s *authService) ValidateToken(token string) (*interfaces.Claims, error) {
	return validateJWT(token, s.jwtSecret)
}

func (s *authService) RefreshToken(refreshToken string) (*interfaces.TokenPair, error) {
	// Validate refresh token
	claims, err := validateJWT(refreshToken, s.jwtSecret)
	if err != nil {
		return nil, fmt.Errorf("invalid refresh token: %w", err)
	}

	// Generate new token pair
	return s.GenerateTokenPair(uuid.MustParse(claims.UserID))
}

func (s *authService) InvalidateUserSessions(ctx context.Context, userID uuid.UUID) error {
	return s.sessionRepo.InvalidateUserSessions(ctx, userID)
}

func (s *authService) hashPassword(password string) (string, error) {
	// Generate salt
	salt := make([]byte, 32)
	if _, err := rand.Read(salt); err != nil {
		return "", err
	}

	// Hash password with Argon2
	hash := argon2.IDKey([]byte(password), salt, 1, 64*1024, 4, 32)

	// Combine salt and hash
	hashedPassword := make([]byte, 0, len(salt)+len(hash))
	hashedPassword = append(hashedPassword, salt...)
	hashedPassword = append(hashedPassword, hash...)

	return fmt.Sprintf("%x", hashedPassword), nil
}

func (s *authService) verifyPassword(password, hashedPassword string) bool {
	// Decode hex string
	hashBytes := make([]byte, len(hashedPassword)/2)
	if _, err := fmt.Sscanf(hashedPassword, "%x", &hashBytes); err != nil {
		return false
	}

	if len(hashBytes) < 32 {
		return false
	}

	// Extract salt and hash
	salt := hashBytes[:32]
	storedHash := hashBytes[32:]

	// Hash input password with same parameters
	computedHash := argon2.IDKey([]byte(password), salt, 1, 64*1024, 4, 32)

	// Compare hashes using constant time
	return subtle.ConstantTimeCompare(storedHash, computedHash) == 1
}
