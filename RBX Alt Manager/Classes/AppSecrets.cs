using System;
using System.IO;
using Newtonsoft.Json;

namespace RBX_Alt_Manager.Classes
{
    public static class AppSecrets
    {
        public static string SupabaseUrl { get; private set; }
        public static string SupabaseKey { get; private set; }
        public static string GoogleClientId { get; private set; }
        public static string GoogleClientSecret { get; private set; }

        static AppSecrets()
        {
            Load();
        }

        private static void Load()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "secrets.json");

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Arquivo secrets.json não encontrado. Copie secrets.example.json para secrets.json e preencha suas credenciais.");

            var json = File.ReadAllText(path);
            var secrets = JsonConvert.DeserializeObject<SecretsData>(json);

            SupabaseUrl = secrets.SupabaseUrl;
            SupabaseKey = secrets.SupabaseKey;
            GoogleClientId = secrets.GoogleClientId;
            GoogleClientSecret = secrets.GoogleClientSecret;
        }

        private class SecretsData
        {
            [JsonProperty("SupabaseUrl")]
            public string SupabaseUrl { get; set; }

            [JsonProperty("SupabaseKey")]
            public string SupabaseKey { get; set; }

            [JsonProperty("GoogleClientId")]
            public string GoogleClientId { get; set; }

            [JsonProperty("GoogleClientSecret")]
            public string GoogleClientSecret { get; set; }
        }
    }
}
