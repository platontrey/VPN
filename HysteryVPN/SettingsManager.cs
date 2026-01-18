using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HysteryVPN
{
    public class SettingsManager
    {
        private readonly string SettingsFile;
        private Dictionary<string, object> _settings = new Dictionary<string, object>();
        private readonly Logger _logger;

        public SettingsManager(Logger logger)
        {
            _logger = logger;
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "HysteryVPN");
            Directory.CreateDirectory(appFolder);
            SettingsFile = Path.Combine(appFolder, "settings.json");
            LoadSettings();
        }

        public void LoadSettings()
        {
            if (File.Exists(SettingsFile))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFile);
                    _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                    _logger.Log("Settings loaded successfully.");
                    _logger.Log($"Loaded SavedLink: '{(_settings.ContainsKey("SavedLink") ? _settings["SavedLink"] : "not found")}'");
                }
                catch (Exception ex)
                {
                    _settings = new Dictionary<string, object>();
                    _logger.Log($"Error loading settings: {ex.Message}");
                }
            }
            else
            {
                // Try to migrate from old location
                string oldFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                _logger.Log($"Checking for old settings.json at: {oldFile}");
                if (File.Exists(oldFile))
                {
                    try
                    {
                        File.Copy(oldFile, SettingsFile, true);
                        _logger.Log("Migrated settings from old location.");
                        LoadSettings(); // Reload after migration
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"Error migrating settings: {ex.Message}");
                    }
                }
                else
                {
                    // Try to migrate from saved_link.txt
                    string txtFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saved_link.txt");
                    _logger.Log($"Checking for saved_link.txt at: {txtFile}");
                    if (File.Exists(txtFile))
                    {
                        try
                        {
                            string link = File.ReadAllText(txtFile).Trim();
                            _logger.Log($"Read link from txt: '{link}'");
                            if (!string.IsNullOrEmpty(link))
                            {
                                _settings["SavedLink"] = link;
                                SaveSettings();
                                _logger.Log("Migrated link from saved_link.txt.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Log($"Error migrating from txt: {ex.Message}");
                        }
                    }
                }
                _logger.Log("Settings file not found, using defaults.");
            }
        }

        public void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
                _logger.Log("Settings saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Error saving settings: {ex.Message}");
            }
        }

        public T GetSetting<T>(string key, T defaultValue = default!)
        {
            if (_settings.TryGetValue(key, out object? value) && value != null)
            {
                _logger.Log($"GetSetting: key={key}, value type={value.GetType()}, value='{value}'");
                try
                {
                    if (value is JsonElement je)
                    {
                        if (typeof(T) == typeof(string))
                        {
                            return (T)(object)(je.GetString() ?? "");
                        }
                        else if (typeof(T) == typeof(bool))
                        {
                            return (T)(object)je.GetBoolean();
                        }
                        else
                        {
                            return (T)Convert.ChangeType(je.ToString(), typeof(T));
                        }
                    }
                    else
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"Convert error: {ex.Message}");
                    return defaultValue;
                }
            }
            _logger.Log($"GetSetting: key={key} not found or null");
            return defaultValue;
        }

        public void SetSetting(string key, object value)
        {
            _settings[key] = value;
            SaveSettings();
        }
    }
}