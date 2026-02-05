using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RBX_Alt_Manager.Classes
{
    public class GamesConfig
    {
        private static GamesConfig _instance;
        private static readonly object _lock = new object();
        private static string _configPath;

        public string SpreadsheetId { get; set; }
        public string AppsScriptUrl { get; set; }
        public PusherConfig Pusher { get; set; }
        public List<GameConfig> Games { get; set; }

        public class PusherConfig
        {
            public string AppId { get; set; }
            public string Key { get; set; }
            public string Secret { get; set; }
            public string Cluster { get; set; }
        }

        public class GameConfig
        {
            public string Name { get; set; }
            public long Gid { get; set; }
            public string PlaceId { get; set; }
        }

        /// <summary>
        /// Obtém a instância singleton da configuração
        /// </summary>
        public static GamesConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            Load();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Carrega a configuração do arquivo JSON
        /// </summary>
        public static void Load()
        {
            try
            {
                // Tentar múltiplos caminhos
                var possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games_config.json"),
                    Path.Combine(Environment.CurrentDirectory, "games_config.json"),
                    "games_config.json"
                };

                _configPath = null;
                foreach (var path in possiblePaths)
                {
                    System.Diagnostics.Debug.WriteLine($"[Config] Tentando: {path}");
                    if (File.Exists(path))
                    {
                        _configPath = path;
                        System.Diagnostics.Debug.WriteLine($"[Config] Encontrado: {path}");
                        break;
                    }
                }

                if (_configPath != null && File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    _instance = JsonConvert.DeserializeObject<GamesConfig>(json);
                    System.Diagnostics.Debug.WriteLine($"[Config] Carregado: {_instance?.Games?.Count ?? 0} jogos de {_configPath}");
                }
                else
                {
                    // Criar configuração padrão
                    System.Diagnostics.Debug.WriteLine("[Config] Arquivo não encontrado, usando padrão");
                    _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games_config.json");
                    _instance = CreateDefault();
                    Save();
                    System.Diagnostics.Debug.WriteLine($"[Config] Arquivo criado: {_configPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config] Erro ao carregar: {ex.Message}");
                _instance = CreateDefault();
            }
        }

        /// <summary>
        /// Salva a configuração no arquivo JSON
        /// </summary>
        public static void Save()
        {
            try
            {
                if (_instance == null) return;
                
                _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games_config.json");
                string json = JsonConvert.SerializeObject(_instance, Formatting.Indented);
                File.WriteAllText(_configPath, json);
                System.Diagnostics.Debug.WriteLine("[Config] Salvo com sucesso");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config] Erro ao salvar: {ex.Message}");
            }
        }

        /// <summary>
        /// Recarrega a configuração do arquivo
        /// </summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _instance = null;
                Load();
            }
        }

        /// <summary>
        /// Retorna os jogos como Dictionary (compatível com código existente)
        /// </summary>
        public Dictionary<string, long> GetGamesDictionary()
        {
            if (Games == null || Games.Count == 0)
                return new Dictionary<string, long>();

            return Games.ToDictionary(g => g.Name, g => g.Gid);
        }

        /// <summary>
        /// Adiciona um novo jogo
        /// </summary>
        public void AddGame(string name, long gid)
        {
            if (Games == null)
                Games = new List<GameConfig>();

            // Verificar se já existe
            if (Games.Any(g => g.Gid == gid || g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return;

            Games.Add(new GameConfig { Name = name, Gid = gid });
            Save();
        }

        /// <summary>
        /// Remove um jogo
        /// </summary>
        public void RemoveGame(long gid)
        {
            if (Games == null) return;
            Games.RemoveAll(g => g.Gid == gid);
            Save();
        }

        /// <summary>
        /// Cria configuração padrão
        /// </summary>
        private static GamesConfig CreateDefault()
        {
            return new GamesConfig
            {
                SpreadsheetId = "1__lG_K1GbwNYjdrt1DAgkw5DX_vsnM62ohMLkFEw-CM",
                AppsScriptUrl = "https://script.google.com/macros/s/AKfycbzMPPx6Nnbjnh0s8LY7XhK4u5S7TwdVOUdHhZn3vY9ShdHv83e0JEncUEVi8e_NWMEp/exec",
                Pusher = new PusherConfig
                {
                    AppId = "2109797",
                    Key = "57bd748f4c7f9a3be25b",
                    Secret = "3dd8ccfd8be167d0cc2d",
                    Cluster = "sa1"
                },
                Games = new List<GameConfig>
                {
                    new GameConfig { Name = "Steal A Brainrot", Gid = 1637218332 },
                    new GameConfig { Name = "Escape Tsunami", Gid = 1036319857 },
                    new GameConfig { Name = "Blox Fruits", Gid = 329604601 },
                    new GameConfig { Name = "Murder Mystery 2", Gid = 1699388936 },
                    new GameConfig { Name = "Grow A Garden", Gid = 47237033 },
                    new GameConfig { Name = "Plants vs Brainrots", Gid = 1554191605 },
                    new GameConfig { Name = "Levantar Animais", Gid = 750499672 }
                }
            };
        }
    }
}
