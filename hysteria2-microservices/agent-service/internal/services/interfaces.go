package services

// ConfigManager handles configuration management
type ConfigManager interface {
	GetConfig() (map[string]interface{}, error)
	UpdateConfig(config map[string]interface{}) error
}

// MetricsCollector collects system metrics
type MetricsCollector interface {
	Collect() (map[string]interface{}, error)
	StartCollection() error
	StopCollection() error
}

// SystemManager handles system operations
type SystemManager interface {
	GetSystemInfo() (map[string]interface{}, error)
	RestartService() error
	UpdateSoftware() error
	IsBBREnabled() (bool, error)
	EnableBBR() error
	CheckAndEnableBBR() error
}

// NetworkManager handles network operations including masquerading
type NetworkManager interface {
	EnableMasquerading(interfaceName string) error
	DisableMasquerading(interfaceName string) error
	IsMasqueradingEnabled(interfaceName string) (bool, error)
	GetNetworkInterfaces() ([]string, error)
}

// LocalServices aggregates all local services
type LocalServices struct {
	ConfigManager    ConfigManager
	MetricsCollector MetricsCollector
	SystemManager    SystemManager
	NetworkManager   NetworkManager
	HysteriaManager  HysteriaManager
}
