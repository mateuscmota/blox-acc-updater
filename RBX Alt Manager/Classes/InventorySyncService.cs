using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RBX_Alt_Manager.Classes
{
    public class InventorySyncService : IDisposable
    {
        private static InventorySyncService _instance;
        public static InventorySyncService Instance => _instance ?? (_instance = new InventorySyncService());

        private System.Windows.Forms.Timer _pollTimer;
        private DateTime _lastCheckUtc = DateTime.UtcNow;
        private bool _isPolling = false;
        private const int POLL_INTERVAL_MS = 3000;

        private readonly HttpClient _client;
        private readonly string _supabaseUrl;
        private readonly string _supabaseKey;

        public event EventHandler<List<SupabaseInventoryEntry>> InventoryEntriesChanged;

        // IDs de inventário que ESTA instância atualizou recentemente (evita echo)
        private readonly HashSet<int> _recentLocalUpdates = new HashSet<int>();
        private readonly object _recentLock = new object();

        private InventorySyncService()
        {
            _supabaseUrl = AppSecrets.SupabaseUrl;
            _supabaseKey = AppSecrets.SupabaseKey;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("apikey", _supabaseKey);
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_supabaseKey}");
            _client.Timeout = TimeSpan.FromSeconds(10);
        }

        public void Start()
        {
            if (_pollTimer != null) return;

            _lastCheckUtc = DateTime.UtcNow;
            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = POLL_INTERVAL_MS;
            _pollTimer.Tick += async (s, e) => await PollForChangesAsync();
            _pollTimer.Start();

            if (AccountManager.DebugModeAtivo)
                Console.WriteLine("[InventorySync] Polling iniciado (3s)");
        }

        public void Stop()
        {
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        /// <summary>
        /// Marca um inventoryId como atualizado localmente (anti-echo, expira em 5s)
        /// </summary>
        public void MarkLocalUpdate(int inventoryId)
        {
            lock (_recentLock)
            {
                _recentLocalUpdates.Add(inventoryId);
            }
            Task.Delay(5000).ContinueWith(_ =>
            {
                lock (_recentLock)
                {
                    _recentLocalUpdates.Remove(inventoryId);
                }
            });
        }

        private async Task PollForChangesAsync()
        {
            if (_isPolling) return;
            _isPolling = true;

            try
            {
                string lastCheckStr = _lastCheckUtc.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
                DateTime pollTime = DateTime.UtcNow;

                var response = await _client.GetAsync(
                    $"{_supabaseUrl}/rest/v1/inventory" +
                    $"?updated_at=gt.{Uri.EscapeDataString(lastCheckStr)}" +
                    $"&select=id,username,item_id,quantity,updated_at" +
                    $"&order=updated_at.asc" +
                    $"&limit=500");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var changed = JsonConvert.DeserializeObject<List<SupabaseInventoryEntry>>(json);

                    if (changed != null && changed.Count > 0)
                    {
                        List<SupabaseInventoryEntry> external;
                        lock (_recentLock)
                        {
                            external = changed
                                .Where(c => !_recentLocalUpdates.Contains(c.Id))
                                .ToList();
                        }

                        if (external.Count > 0)
                        {
                            if (AccountManager.DebugModeAtivo)
                                Console.WriteLine($"[InventorySync] {external.Count} alterações externas detectadas");

                            InventoryEntriesChanged?.Invoke(this, external);
                        }

                        _lastCheckUtc = changed.Max(c => c.UpdatedAt);
                    }
                    else
                    {
                        _lastCheckUtc = pollTime;
                    }
                }
            }
            catch (Exception ex)
            {
                if (AccountManager.DebugModeAtivo)
                    Console.WriteLine($"[InventorySync] Erro poll: {ex.Message}");
            }
            finally
            {
                _isPolling = false;
            }
        }

        public void Dispose()
        {
            Stop();
            _client?.Dispose();
        }
    }
}
