package services

import (
	"encoding/json"
	"fmt"
	"os/exec"
	"strings"

	"github.com/sirupsen/logrus"
	"hysteria2-microservices/agent-service/internal/config"
)

// HysteriaManager handles Hysteria2 VPN server management
type HysteriaManager interface {
	InstallHysteria2() error
	IsHysteria2Installed() bool
	GenerateConfig(configTemplate string) (string, error)
	StartHysteria2(configPath string) error
	StopHysteria2() error
	RestartHysteria2(configPath string) error
	GetHysteria2Status() (map[string]interface{}, error)
	EnablePortHopping(startPort, endPort, interval int) error
	DisablePortHopping() error
	EnableSalamander(password string) error
	DisableSalamander() error
}

type HysteriaManagerImpl struct {
	logger *logrus.Logger
	config *config.Config
}

// NewHysteriaManager creates a new HysteriaManager
func NewHysteriaManager(logger *logrus.Logger, cfg *config.Config) HysteriaManager {
	return &HysteriaManagerImpl{
		logger: logger,
		config: cfg,
	}
}

// InstallHysteria2 installs Hysteria2 using the official script
func (hm *HysteriaManagerImpl) InstallHysteria2() error {
	hm.logger.Info("Installing Hysteria2...")

	// Use the official install script
	cmd := exec.Command("bash", "-c", "bash <(curl -fsSL https://get.hy2.sh/)")
	output, err := cmd.CombinedOutput()
	if err != nil {
		hm.logger.Errorf("Failed to install Hysteria2: %v, output: %s", err, string(output))
		return fmt.Errorf("failed to install Hysteria2: %w", err)
	}

	hm.logger.Info("Hysteria2 installed successfully")
	return nil
}

// IsHysteria2Installed checks if Hysteria2 is installed
func (hm *HysteriaManagerImpl) IsHysteria2Installed() bool {
	cmd := exec.Command("which", "hysteria")
	err := cmd.Run()
	return err == nil
}

// GenerateConfig generates Hysteria2 configuration based on template
func (hm *HysteriaManagerImpl) GenerateConfig(configTemplate string) (string, error) {
	hm.logger.Info("Generating Hysteria2 configuration")

	var hysteriaConfig map[string]interface{}

	if configTemplate != "" {
		// Parse custom config template
		err := json.Unmarshal([]byte(configTemplate), &hysteriaConfig)
		if err != nil {
			return "", fmt.Errorf("invalid config template: %w", err)
		}
	} else {
		// Generate default config
		hysteriaConfig = hm.generateDefaultConfig()
	}

	// Apply configuration options
	hm.applyConfigOptions(hysteriaConfig)

	// Convert to JSON
	configJSON, err := json.MarshalIndent(hysteriaConfig, "", "  ")
	if err != nil {
		return "", fmt.Errorf("failed to marshal config: %w", err)
	}

	return string(configJSON), nil
}

func (hm *HysteriaManagerImpl) generateDefaultConfig() map[string]interface{} {
	return map[string]interface{}{
		"listen": fmt.Sprintf(":%d", hm.config.Hysteria2.DefaultListenPort),
		"tls": map[string]interface{}{
			"cert": "/etc/hysteria/cert.pem",
			"key":  "/etc/hysteria/key.pem",
		},
		"auth": map[string]interface{}{
			"type":     hm.config.Hysteria2.AuthType,
			"password": hm.config.Hysteria2.AuthPassword,
		},
		"bandwidth": map[string]interface{}{
			"up":   fmt.Sprintf("%d mbps", hm.config.Hysteria2.UpMbps),
			"down": fmt.Sprintf("%d mbps", hm.config.Hysteria2.DownMbps),
		},
	}
}

func (hm *HysteriaManagerImpl) applyConfigOptions(config map[string]interface{}) {
	// Apply Salamander obfuscation
	if hm.config.Hysteria2.SalamanderEnabled {
		config["obfs"] = map[string]interface{}{
			"type":     "salamander",
			"password": hm.config.Hysteria2.SalamanderPassword,
		}
	}

	// Apply Port Hopping
	if hm.config.Hysteria2.PortHopping {
		config["hopping"] = map[string]interface{}{
			"interval": hm.config.Hysteria2.HopInterval,
			"start":    hm.config.Hysteria2.HopStartPort,
			"end":      hm.config.Hysteria2.HopEndPort,
		}
	}
}

// StartHysteria2 starts the Hysteria2 service
func (hm *HysteriaManagerImpl) StartHysteria2(configPath string) error {
	hm.logger.Infof("Starting Hysteria2 with config: %s", configPath)

	if hm.config.Hysteria2.EnableSystemd {
		return hm.startWithSystemd(configPath)
	}

	// Start directly
	cmd := exec.Command("hysteria", "server", "-c", configPath)
	err := cmd.Start()
	if err != nil {
		return fmt.Errorf("failed to start Hysteria2: %w", err)
	}

	hm.logger.Info("Hysteria2 started successfully")
	return nil
}

func (hm *HysteriaManagerImpl) startWithSystemd(configPath string) error {
	// Create systemd service file
	serviceContent := fmt.Sprintf(`[Unit]
Description=Hysteria2 VPN Server
After=network.target

[Service]
Type=simple
User=root
ExecStart=/usr/local/bin/hysteria server -c %s
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
`, configPath)

	err := hm.runCommand("echo", serviceContent, "|", "tee", "/etc/systemd/system/hysteria2.service")
	if err != nil {
		return fmt.Errorf("failed to create systemd service: %w", err)
	}

	// Reload systemd and start service
	if err := hm.runCommand("systemctl", "daemon-reload"); err != nil {
		return fmt.Errorf("failed to reload systemd: %w", err)
	}

	if err := hm.runCommand("systemctl", "enable", "hysteria2"); err != nil {
		return fmt.Errorf("failed to enable hysteria2 service: %w", err)
	}

	if err := hm.runCommand("systemctl", "start", "hysteria2"); err != nil {
		return fmt.Errorf("failed to start hysteria2 service: %w", err)
	}

	hm.logger.Info("Hysteria2 started with systemd")
	return nil
}

// StopHysteria2 stops the Hysteria2 service
func (hm *HysteriaManagerImpl) StopHysteria2() error {
	hm.logger.Info("Stopping Hysteria2")

	if hm.config.Hysteria2.EnableSystemd {
		return hm.runCommand("systemctl", "stop", "hysteria2")
	}

	// Kill process directly (not ideal, but for demo)
	return hm.runCommand("pkill", "-f", "hysteria")
}

// RestartHysteria2 restarts the Hysteria2 service
func (hm *HysteriaManagerImpl) RestartHysteria2(configPath string) error {
	hm.logger.Info("Restarting Hysteria2")

	if hm.config.Hysteria2.EnableSystemd {
		return hm.runCommand("systemctl", "restart", "hysteria2")
	}

	if err := hm.StopHysteria2(); err != nil {
		hm.logger.Warnf("Failed to stop Hysteria2: %v", err)
	}

	return hm.StartHysteria2(configPath)
}

// GetHysteria2Status returns Hysteria2 service status
func (hm *HysteriaManagerImpl) GetHysteria2Status() (map[string]interface{}, error) {
	status := map[string]interface{}{
		"installed": hm.IsHysteria2Installed(),
		"running":   false,
		"systemd":   hm.config.Hysteria2.EnableSystemd,
	}

	if hm.config.Hysteria2.EnableSystemd {
		// Check systemd status
		cmd := exec.Command("systemctl", "is-active", "hysteria2")
		output, err := cmd.Output()
		if err == nil && strings.TrimSpace(string(output)) == "active" {
			status["running"] = true
		}
	} else {
		// Check if process is running
		cmd := exec.Command("pgrep", "-f", "hysteria")
		err := cmd.Run()
		status["running"] = err == nil
	}

	return status, nil
}

// EnablePortHopping enables port hopping
func (hm *HysteriaManagerImpl) EnablePortHopping(startPort, endPort, interval int) error {
	hm.logger.Infof("Enabling port hopping: %d-%d every %d seconds", startPort, endPort, interval)

	// This would require updating the config and restarting
	// For now, just log
	hm.config.Hysteria2.PortHopping = true
	hm.config.Hysteria2.HopStartPort = startPort
	hm.config.Hysteria2.HopEndPort = endPort
	hm.config.Hysteria2.HopInterval = interval

	return nil
}

// DisablePortHopping disables port hopping
func (hm *HysteriaManagerImpl) DisablePortHopping() error {
	hm.logger.Info("Disabling port hopping")

	hm.config.Hysteria2.PortHopping = false
	return nil
}

// EnableSalamander enables Salamander obfuscation
func (hm *HysteriaManagerImpl) EnableSalamander(password string) error {
	hm.logger.Info("Enabling Salamander obfuscation")

	hm.config.Hysteria2.SalamanderEnabled = true
	hm.config.Hysteria2.SalamanderPassword = password
	return nil
}

// DisableSalamander disables Salamander obfuscation
func (hm *HysteriaManagerImpl) DisableSalamander() error {
	hm.logger.Info("Disabling Salamander obfuscation")

	hm.config.Hysteria2.SalamanderEnabled = false
	return nil
}

// runCommand executes a system command
func (hm *HysteriaManagerImpl) runCommand(name string, args ...string) error {
	cmd := exec.Command(name, args...)
	hm.logger.Debugf("Running command: %s %v", name, args)
	return cmd.Run()
}
