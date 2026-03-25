using Newtonsoft.Json;
using RadioLogger.Models;
using Serilog;
using System;
using System.IO;

namespace RadioLogger.Services
{
    public class ConfigManager
    {
        private const string ConfigFileName = "settings.json";
        private readonly string _configPath;

        public AppSettings CurrentSettings { get; private set; } = new AppSettings();

        public ConfigManager()
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
            Load();
        }

        public void Load()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    CurrentSettings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Archivo de configuración corrupto, usando valores por defecto");
                    CurrentSettings = new AppSettings();
                }
            }
            else
            {
                CurrentSettings = new AppSettings();
                Save();
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(CurrentSettings, Formatting.Indented);
                File.WriteAllText(_configPath, json);
                Log.Debug("Configuración guardada");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error guardando configuración");
            }
        }
    }
}
