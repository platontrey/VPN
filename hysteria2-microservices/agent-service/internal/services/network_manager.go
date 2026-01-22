package services

import (
	"fmt"
	"os/exec"
	"strings"

	"github.com/sirupsen/logrus"
)

// NetworkManagerImpl implements NetworkManager interface
type NetworkManagerImpl struct {
	logger *logrus.Logger
}

// NewNetworkManager creates a new NetworkManager
func NewNetworkManager(logger *logrus.Logger) NetworkManager {
	return &NetworkManagerImpl{
		logger: logger,
	}
}

// EnableMasquerading enables IP masquerading on the specified interface
func (nm *NetworkManagerImpl) EnableMasquerading(interfaceName string) error {
	nm.logger.Infof("Enabling masquerading on interface: %s", interfaceName)

	// Enable IP forwarding
	if err := nm.runCommand("sysctl", "-w", "net.ipv4.ip_forward=1"); err != nil {
		nm.logger.Errorf("Failed to enable IP forwarding: %v", err)
		return fmt.Errorf("failed to enable IP forwarding: %w", err)
	}

	// Add iptables rule for masquerading
	cmd := fmt.Sprintf("-t nat -A POSTROUTING -o %s -j MASQUERADE", interfaceName)
	if err := nm.runCommand("iptables", strings.Fields(cmd)...); err != nil {
		nm.logger.Errorf("Failed to add masquerading rule: %v", err)
		return fmt.Errorf("failed to add masquerading rule: %w", err)
	}

	nm.logger.Infof("Masquerading enabled successfully on interface: %s", interfaceName)
	return nil
}

// DisableMasquerading disables IP masquerading on the specified interface
func (nm *NetworkManagerImpl) DisableMasquerading(interfaceName string) error {
	nm.logger.Infof("Disabling masquerading on interface: %s", interfaceName)

	// Remove iptables rule for masquerading
	cmd := fmt.Sprintf("-t nat -D POSTROUTING -o %s -j MASQUERADE", interfaceName)
	if err := nm.runCommand("iptables", strings.Fields(cmd)...); err != nil {
		nm.logger.Errorf("Failed to remove masquerading rule: %v", err)
		return fmt.Errorf("failed to remove masquerading rule: %w", err)
	}

	nm.logger.Infof("Masquerading disabled successfully on interface: %s", interfaceName)
	return nil
}

// IsMasqueradingEnabled checks if masquerading is enabled on the specified interface
func (nm *NetworkManagerImpl) IsMasqueradingEnabled(interfaceName string) (bool, error) {
	cmd := fmt.Sprintf("-t nat -C POSTROUTING -o %s -j MASQUERADE", interfaceName)
	err := nm.runCommand("iptables", strings.Fields(cmd)...)
	if err != nil {
		// If the rule doesn't exist, iptables -C returns exit code 1
		return false, nil
	}
	return true, nil
}

// GetNetworkInterfaces returns a list of available network interfaces
func (nm *NetworkManagerImpl) GetNetworkInterfaces() ([]string, error) {
	output, err := nm.runCommandWithOutput("ip", "link", "show")
	if err != nil {
		nm.logger.Errorf("Failed to get network interfaces: %v", err)
		return nil, fmt.Errorf("failed to get network interfaces: %w", err)
	}

	var interfaces []string
	lines := strings.Split(output, "\n")
	for _, line := range lines {
		if strings.Contains(line, ": ") {
			parts := strings.Split(line, ": ")
			if len(parts) >= 2 {
				ifaceName := strings.TrimSpace(parts[1])
				if ifaceName != "" && ifaceName != "lo" {
					interfaces = append(interfaces, ifaceName)
				}
			}
		}
	}

	return interfaces, nil
}

// runCommand executes a system command and returns error if any
func (nm *NetworkManagerImpl) runCommand(name string, args ...string) error {
	cmd := exec.Command(name, args...)
	nm.logger.Debugf("Running command: %s %v", name, args)
	return cmd.Run()
}

// runCommandWithOutput executes a system command and returns its output
func (nm *NetworkManagerImpl) runCommandWithOutput(name string, args ...string) (string, error) {
	cmd := exec.Command(name, args...)
	nm.logger.Debugf("Running command with output: %s %v", name, args)
	output, err := cmd.Output()
	return string(output), err
}
