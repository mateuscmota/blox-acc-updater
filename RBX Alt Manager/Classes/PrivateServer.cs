using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace RBX_Alt_Manager.Classes
{
    public class PrivateServer
    {
        public string Name { get; set; }
        public string Link { get; set; }

        public PrivateServer() { }

        public PrivateServer(string name, string link)
        {
            Name = name;
            Link = link;
        }
    }

    public static class PrivateServerManager
    {
        private static readonly string SaveFilePath = Path.Combine(Environment.CurrentDirectory, "PrivateServers.json");
        public static List<PrivateServer> Servers { get; private set; } = new List<PrivateServer>();

        public static void Load()
        {
            try
            {
                if (File.Exists(SaveFilePath))
                {
                    string json = File.ReadAllText(SaveFilePath);
                    Servers = JsonConvert.DeserializeObject<List<PrivateServer>>(json) ?? new List<PrivateServer>();
                }
            }
            catch (Exception ex)
            {
                Program.Logger.Error($"Failed to load private servers: {ex.Message}");
                Servers = new List<PrivateServer>();
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Servers, Formatting.Indented);
                File.WriteAllText(SaveFilePath, json);
            }
            catch (Exception ex)
            {
                Program.Logger.Error($"Failed to save private servers: {ex.Message}");
            }
        }

        public static void Add(string name, string link)
        {
            Servers.Add(new PrivateServer(name, link));
            Save();
        }

        public static void Remove(PrivateServer server)
        {
            Servers.Remove(server);
            Save();
        }

        public static void Remove(int index)
        {
            if (index >= 0 && index < Servers.Count)
            {
                Servers.RemoveAt(index);
                Save();
            }
        }

        public static void Update(int index, string name, string link)
        {
            if (index >= 0 && index < Servers.Count)
            {
                Servers[index].Name = name;
                Servers[index].Link = link;
                Save();
            }
        }
    }
}
