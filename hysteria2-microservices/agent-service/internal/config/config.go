package config

import (
	"fmt"
	"os"
	"strconv"

	"github.com/spf13/viper"
)

type Config struct {
	MasterServer string          `mapstructure:"master_server"`
	Node         NodeConfig      `mapstructure:"node"`
	Metrics      MetricsConfig   `mapstructure:"metrics"`
	Logging      LoggingConfig   `mapstructure:"logging"`
	Network      NetworkConfig   `mapstructure:"network"`
	Hysteria2    Hysteria2Config `mapstructure:"hysteria2"`
}

type NodeConfig struct {
	ID           string            `mapstructure:"id"`
	Name         string            `mapstructure:"name"`
	Hostname     string            `mapstructure:"hostname"`
	IPAddress    string            `mapstructure:"ip_address"`
	Location     string            `mapstructure:"location"`
	Country      string            `mapstructure:"country"`
	GRPCPort     int               `mapstructure:"grpc_port"`
	Capabilities map[string]string `mapstructure:"capabilities"`
	Metadata     map[string]string `mapstructure:"metadata"`
}

type MetricsConfig struct {
	CollectInterval int `mapstructure:"collect_interval"` // seconds
	ReportInterval  int `mapstructure:"report_interval"`  // seconds
}

type LoggingConfig struct {
	Level  string `mapstructure:"level"`
	Format string `mapstructure:"format"` // json, text
}

type NetworkConfig struct {
	EnableMasquerading bool   `mapstructure:"enable_masquerading"`
	DefaultInterface   string `mapstructure:"default_interface"`
}

type Hysteria2Config struct {
	EnableBBR          bool   `mapstructure:"enable_bbr"`
	EnableSystemd      bool   `mapstructure:"enable_systemd"`
	PortHopping        bool   `mapstructure:"port_hopping"`
	HopInterval        int    `mapstructure:"hop_interval"` // seconds
	HopStartPort       int    `mapstructure:"hop_start_port"`
	HopEndPort         int    `mapstructure:"hop_end_port"`
	SalamanderEnabled  bool   `mapstructure:"salamander_enabled"`
	SalamanderPassword string `mapstructure:"salamander_password"`
	ObfsType           string `mapstructure:"obfs_type"`     // "salamander" or others
	CustomConfig       string `mapstructure:"custom_config"` // JSON string for custom settings
	ListenPorts        []int  `mapstructure:"listen_ports"`
	DefaultListenPort  int    `mapstructure:"default_listen_port"`
	AuthType           string `mapstructure:"auth_type"` // "password", "userpass", etc.
	AuthPassword       string `mapstructure:"auth_password"`
	UpMbps             int    `mapstructure:"up_mbps"`
	DownMbps           int    `mapstructure:"down_mbps"`
}

func LoadConfig() (*Config, error) {
	setDefaults()

	viper.AutomaticEnv()
	bindEnvVars()

	// Load config file if exists
	viper.SetConfigName("agent")
	viper.SetConfigType("yaml")
	viper.AddConfigPath("./configs")
	viper.AddConfigPath("../configs")

	if err := viper.ReadInConfig(); err != nil {
		if _, ok := err.(viper.ConfigFileNotFoundError); ok {
			fmt.Println("Config file not found, using environment variables and defaults")
		} else {
			return nil, fmt.Errorf("error reading config file: %w", err)
		}
	}

	var config Config
	if err := viper.Unmarshal(&config); err != nil {
		return nil, fmt.Errorf("error unmarshaling config: %w", err)
	}

	return &config, nil
}

func setDefaults() {
	viper.SetDefault("master_server", "")
	viper.SetDefault("node.grpc_port", 50051)
	viper.SetDefault("metrics.collect_interval", 30)
	viper.SetDefault("metrics.report_interval", 60)
	viper.SetDefault("logging.level", "info")
	viper.SetDefault("logging.format", "text")
	viper.SetDefault("network.enable_masquerading", false)
	viper.SetDefault("network.default_interface", "eth0")
	viper.SetDefault("hysteria2.enable_bbr", true)
	viper.SetDefault("hysteria2.enable_systemd", true)
	viper.SetDefault("hysteria2.port_hopping", false)
	viper.SetDefault("hysteria2.hop_interval", 30)
	viper.SetDefault("hysteria2.hop_start_port", 10000)
	viper.SetDefault("hysteria2.hop_end_port", 20000)
	viper.SetDefault("hysteria2.salamander_enabled", false)
	viper.SetDefault("hysteria2.obfs_type", "")
	viper.SetDefault("hysteria2.default_listen_port", 8080)
	viper.SetDefault("hysteria2.auth_type", "password")
	viper.SetDefault("hysteria2.up_mbps", 100)
	viper.SetDefault("hysteria2.down_mbps", 100)
}

func bindEnvVars() {
	viper.BindEnv("master_server", "MASTER_SERVER")
	viper.BindEnv("node.id", "NODE_ID")
	viper.BindEnv("node.name", "NODE_NAME")
	viper.BindEnv("node.hostname", "NODE_HOSTNAME")
	viper.BindEnv("node.ip_address", "NODE_IP_ADDRESS")
	viper.BindEnv("node.location", "NODE_LOCATION")
	viper.BindEnv("node.country", "NODE_COUNTRY")
	viper.BindEnv("node.grpc_port", "NODE_GRPC_PORT")
	viper.BindEnv("logging.level", "LOG_LEVEL")
	viper.BindEnv("logging.format", "LOG_FORMAT")
}

func GetEnvString(key, defaultValue string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return defaultValue
}

func GetEnvInt(key string, defaultValue int) int {
	if value := os.Getenv(key); value != "" {
		if intValue, err := strconv.Atoi(value); err == nil {
			return intValue
		}
	}
	return defaultValue
}
