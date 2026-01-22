package handlers

import (
	"context"
	"fmt"
	"os"

	"github.com/sirupsen/logrus"
	"hysteria2-microservices/agent-service/internal/services"
	pb "hysteria2-microservices/proto"
)

// NodeManagerHandler implements the NodeManager gRPC service
type NodeManagerHandler struct {
	pb.UnimplementedNodeManagerServer
	localServices *services.LocalServices
	logger        *logrus.Logger
}

// NewNodeManagerHandler creates a new NodeManagerHandler
func NewNodeManagerHandler(localServices *services.LocalServices, logger *logrus.Logger) *NodeManagerHandler {
	return &NodeManagerHandler{
		localServices: localServices,
		logger:        logger,
	}
}

// EnableMasquerading enables IP masquerading on the specified interface
func (h *NodeManagerHandler) EnableMasquerading(ctx context.Context, req *pb.EnableMasqueradingRequest) (*pb.EnableMasqueradingResponse, error) {
	h.logger.Infof("EnableMasquerading called for interface: %s", req.InterfaceName)

	err := h.localServices.NetworkManager.EnableMasquerading(req.InterfaceName)
	if err != nil {
		h.logger.Errorf("Failed to enable masquerading: %v", err)
		return &pb.EnableMasqueradingResponse{
			Success: false,
			Message: fmt.Sprintf("Failed to enable masquerading: %v", err),
		}, nil
	}

	return &pb.EnableMasqueradingResponse{
		Success: true,
		Message: "Masquerading enabled successfully",
	}, nil
}

// DisableMasquerading disables IP masquerading on the specified interface
func (h *NodeManagerHandler) DisableMasquerading(ctx context.Context, req *pb.DisableMasqueradingRequest) (*pb.DisableMasqueradingResponse, error) {
	h.logger.Infof("DisableMasquerading called for interface: %s", req.InterfaceName)

	err := h.localServices.NetworkManager.DisableMasquerading(req.InterfaceName)
	if err != nil {
		h.logger.Errorf("Failed to disable masquerading: %v", err)
		return &pb.DisableMasqueradingResponse{
			Success: false,
			Message: fmt.Sprintf("Failed to disable masquerading: %v", err),
		}, nil
	}

	return &pb.DisableMasqueradingResponse{
		Success: true,
		Message: "Masquerading disabled successfully",
	}, nil
}

// GetNetworkInterfaces returns available network interfaces
func (h *NodeManagerHandler) GetNetworkInterfaces(ctx context.Context, req *pb.GetNetworkInterfacesRequest) (*pb.GetNetworkInterfacesResponse, error) {
	h.logger.Info("GetNetworkInterfaces called")

	interfaces, err := h.localServices.NetworkManager.GetNetworkInterfaces()
	if err != nil {
		h.logger.Errorf("Failed to get network interfaces: %v", err)
		return nil, fmt.Errorf("failed to get network interfaces: %w", err)
	}

	// For now, assume eth0 as default if available
	defaultInterface := "eth0"
	for _, iface := range interfaces {
		if iface == "eth0" {
			defaultInterface = "eth0"
			break
		}
	}

	return &pb.GetNetworkInterfacesResponse{
		Interfaces:       interfaces,
		DefaultInterface: defaultInterface,
	}, nil
}

// IsMasqueradingEnabled checks if masquerading is enabled on the interface
func (h *NodeManagerHandler) IsMasqueradingEnabled(ctx context.Context, req *pb.IsMasqueradingEnabledRequest) (*pb.IsMasqueradingEnabledResponse, error) {
	h.logger.Infof("IsMasqueradingEnabled called for interface: %s", req.InterfaceName)

	enabled, err := h.localServices.NetworkManager.IsMasqueradingEnabled(req.InterfaceName)
	if err != nil {
		h.logger.Errorf("Failed to check masquerading status: %v", err)
		return nil, fmt.Errorf("failed to check masquerading status: %w", err)
	}

	return &pb.IsMasqueradingEnabledResponse{
		Enabled: enabled,
	}, nil
}

// Other methods (placeholders for now)
func (h *NodeManagerHandler) UpdateConfig(ctx context.Context, req *pb.ConfigUpdateRequest) (*pb.ConfigUpdateResponse, error) {
	return &pb.ConfigUpdateResponse{Success: false, Message: "Not implemented"}, nil
}

func (h *NodeManagerHandler) ReloadConfig(ctx context.Context, req *pb.ReloadRequest) (*pb.ReloadResponse, error) {
	return &pb.ReloadResponse{Success: false, Message: "Not implemented"}, nil
}

func (h *NodeManagerHandler) GetStatus(ctx context.Context, req *pb.StatusRequest) (*pb.StatusResponse, error) {
	return &pb.StatusResponse{}, nil
}

func (h *NodeManagerHandler) AddUser(ctx context.Context, req *pb.AddUserRequest) (*pb.AddUserResponse, error) {
	return &pb.AddUserResponse{Success: false, Message: "Not implemented"}, nil
}

func (h *NodeManagerHandler) RemoveUser(ctx context.Context, req *pb.RemoveUserRequest) (*pb.RemoveUserResponse, error) {
	return &pb.RemoveUserResponse{Success: false, Message: "Not implemented"}, nil
}

func (h *NodeManagerHandler) UpdateUser(ctx context.Context, req *pb.UpdateUserRequest) (*pb.UpdateUserResponse, error) {
	return &pb.UpdateUserResponse{Success: false, Message: "Not implemented"}, nil
}

func (h *NodeManagerHandler) GetMetrics(ctx context.Context, req *pb.MetricsRequest) (*pb.MetricsResponse, error) {
	return &pb.MetricsResponse{}, nil
}

func (h *NodeManagerHandler) StreamMetrics(req *pb.StreamMetricsRequest, stream pb.NodeManager_StreamMetricsServer) error {
	return fmt.Errorf("not implemented")
}

func (h *NodeManagerHandler) RestartServer(ctx context.Context, req *pb.RestartRequest) (*pb.RestartResponse, error) {
	return &pb.RestartResponse{Success: false, Message: "Not implemented"}, nil
}

func (h *NodeManagerHandler) GetLogs(ctx context.Context, req *pb.LogRequest) (*pb.LogResponse, error) {
	return &pb.LogResponse{Success: false, Logs: []string{"Not implemented"}}, nil
}

// Hysteria2 management methods

// InstallHysteria2 installs Hysteria2
func (h *NodeManagerHandler) InstallHysteria2(ctx context.Context, req *pb.InstallHysteria2Request) (*pb.InstallHysteria2Response, error) {
	h.logger.Info("InstallHysteria2 called")

	err := h.localServices.HysteriaManager.InstallHysteria2()
	if err != nil {
		h.logger.Errorf("Failed to install Hysteria2: %v", err)
		return &pb.InstallHysteria2Response{
			Success: false,
			Message: fmt.Sprintf("Failed to install Hysteria2: %v", err),
		}, nil
	}

	return &pb.InstallHysteria2Response{
		Success: true,
		Message: "Hysteria2 installed successfully",
	}, nil
}

// ConfigureHysteria2 configures Hysteria2 with given options
func (h *NodeManagerHandler) ConfigureHysteria2(ctx context.Context, req *pb.ConfigureHysteria2Request) (*pb.ConfigureHysteria2Response, error) {
	h.logger.Info("ConfigureHysteria2 called")

	config, err := h.localServices.HysteriaManager.GenerateConfig(req.ConfigTemplate)
	if err != nil {
		h.logger.Errorf("Failed to generate Hysteria2 config: %v", err)
		return &pb.ConfigureHysteria2Response{
			Success: false,
			Message: fmt.Sprintf("Failed to generate config: %v", err),
		}, nil
	}

	// Save config to file
	configPath := "/etc/hysteria/config.json"
	err = os.WriteFile(configPath, []byte(config), 0644)
	if err != nil {
		h.logger.Errorf("Failed to save config: %v", err)
		return &pb.ConfigureHysteria2Response{
			Success: false,
			Message: fmt.Sprintf("Failed to save config: %v", err),
		}, nil
	}

	return &pb.ConfigureHysteria2Response{
		Success:         true,
		Message:         "Hysteria2 configured successfully",
		ConfigPath:      configPath,
		GeneratedConfig: config,
	}, nil
}

// StartHysteria2 starts Hysteria2 service
func (h *NodeManagerHandler) StartHysteria2(ctx context.Context, req *pb.StartHysteria2Request) (*pb.StartHysteria2Response, error) {
	h.logger.Info("StartHysteria2 called")

	err := h.localServices.HysteriaManager.StartHysteria2(req.ConfigPath)
	if err != nil {
		h.logger.Errorf("Failed to start Hysteria2: %v", err)
		return &pb.StartHysteria2Response{
			Success: false,
			Message: fmt.Sprintf("Failed to start Hysteria2: %v", err),
		}, nil
	}

	return &pb.StartHysteria2Response{
		Success: true,
		Message: "Hysteria2 started successfully",
	}, nil
}

// StopHysteria2 stops Hysteria2 service
func (h *NodeManagerHandler) StopHysteria2(ctx context.Context, req *pb.StopHysteria2Request) (*pb.StopHysteria2Response, error) {
	h.logger.Info("StopHysteria2 called")

	err := h.localServices.HysteriaManager.StopHysteria2()
	if err != nil {
		h.logger.Errorf("Failed to stop Hysteria2: %v", err)
		return &pb.StopHysteria2Response{
			Success: false,
			Message: fmt.Sprintf("Failed to stop Hysteria2: %v", err),
		}, nil
	}

	return &pb.StopHysteria2Response{
		Success: true,
		Message: "Hysteria2 stopped successfully",
	}, nil
}

// GetHysteria2Status returns Hysteria2 status
func (h *NodeManagerHandler) GetHysteria2Status(ctx context.Context, req *pb.GetHysteria2StatusRequest) (*pb.GetHysteria2StatusResponse, error) {
	h.logger.Info("GetHysteria2Status called")

	status, err := h.localServices.HysteriaManager.GetHysteria2Status()
	if err != nil {
		h.logger.Errorf("Failed to get Hysteria2 status: %v", err)
		return nil, fmt.Errorf("failed to get Hysteria2 status: %w", err)
	}

	// Convert to protobuf types
	var statusMap map[string]string
	for k, v := range status {
		if str, ok := v.(string); ok {
			statusMap[k] = str
		} else if b, ok := v.(bool); ok {
			statusMap[k] = fmt.Sprintf("%t", b)
		}
	}

	return &pb.GetHysteria2StatusResponse{
		Status: statusMap,
	}, nil
}

// EnablePortHopping enables port hopping
func (h *NodeManagerHandler) EnablePortHopping(ctx context.Context, req *pb.EnablePortHoppingRequest) (*pb.EnablePortHoppingResponse, error) {
	h.logger.Infof("EnablePortHopping called: %d-%d every %d", req.StartPort, req.EndPort, req.Interval)

	err := h.localServices.HysteriaManager.EnablePortHopping(int(req.StartPort), int(req.EndPort), int(req.Interval))
	if err != nil {
		h.logger.Errorf("Failed to enable port hopping: %v", err)
		return &pb.EnablePortHoppingResponse{
			Success: false,
			Message: fmt.Sprintf("Failed to enable port hopping: %v", err),
		}, nil
	}

	return &pb.EnablePortHoppingResponse{
		Success: true,
		Message: "Port hopping enabled successfully",
	}, nil
}

// EnableSalamander enables Salamander obfuscation
func (h *NodeManagerHandler) EnableSalamander(ctx context.Context, req *pb.EnableSalamanderRequest) (*pb.EnableSalamanderResponse, error) {
	h.logger.Info("EnableSalamander called")

	err := h.localServices.HysteriaManager.EnableSalamander(req.Password)
	if err != nil {
		h.logger.Errorf("Failed to enable Salamander: %v", err)
		return &pb.EnableSalamanderResponse{
			Success: false,
			Message: fmt.Sprintf("Failed to enable Salamander: %v", err),
		}, nil
	}

	return &pb.EnableSalamanderResponse{
		Success: true,
		Message: "Salamander obfuscation enabled successfully",
	}, nil
}
