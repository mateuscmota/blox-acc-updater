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
        private DateTime _lastInventoryCheckUtc = DateTime.UtcNow;
        private DateTime _lastGameItemsCheckUtc = DateTime.UtcNow;
        private bool _isPolling = false;
        private const int POLL_INTERVAL_MS = 3000;
        private int _gameItemsPollCounter = 0;

        private readonly HttpClient _client;
        private readonly string _supabaseUrl;
        private readonly string _supabaseKey;

        public event EventHandler<List<SupabaseInventoryEntry>> InventoryEntriesChanged;
        public event EventHandler<List<SupabaseGameItem>> GameItemsChanged;

        // IDs de inventário que ESTA instância atualizou recentemente (evita echo)
        private readonly HashSet<int> _recentLocalUpdates = new HashSet<int>();
        private readonly HashSet<int> _recentLocalItemUpdates = new HashSet<int>();
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

            _lastInventoryCheckUtc = DateTime.UtcNow;
            _lastGameItemsCheckUtc = DateTime.UtcNow;
            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = POLL_INTERVAL_MS;
            _pollTimer.Tick += async (s, e) => await PollForChangesAsync();
            _pollTimer.Start();

            if (AccountManager.DebugModeAtivo)
                Console.WriteLine("[InventorySync] Polling iniciado (3s inventory, 9s game_items)");
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

        /// <summary>
        /// Marca um game_item como atualizado localmente (anti-echo, expira em 10s)
        /// </summary>
        public void MarkLocalItemUpdate(int itemId)
        {
            lock (_recentLock)
            {
                _recentLocalItemUpdates.Add(itemId);
            }
            Task.Delay(10000).ContinueWith(_ =>
            {
                lock (_recentLock)
                {
                    _recentLocalItemUpdates.Remove(itemId);
                }
            });
        }

        private async Task PollForChangesAsync()
        {
            if (_isPolling) return;
            _isPolling = true;

            try
            {
                // Poll inventory a cada tick (3s)
                await PollInventoryAsync();

                // Poll game_items a cada 3 ticks (9s) para economizar requests
                _gameItemsPollCounter++;
                if (_gameItemsPollCounter >= 3)
                {
                    _gameItemsPollCounter = 0;
                    await PollGameItemsAsync();
                }
            }
            finally
            {
                _isPolling = false;
            }
        }

        private async Task PollInventoryAsync()
        {
            try
            {
                string lastCheckStr = _lastInventoryCheckUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
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

                        _lastInventoryCheckUtc = changed.Max(c => c.UpdatedAt).ToUniversalTime();
                    }
                    else
                    {
                        _lastInventoryCheckUtc = pollTime;
                    }
                }
            }
            catch (Exception ex)
            {
                if (AccountManager.DebugModeAtivo)
                    Console.WriteLine($"[InventorySync] Erro poll inventory: {ex.Message}");
            }
        }

        private async Task PollGameItemsAsync()
        {
            try
            {
                var changed = await SupabaseManager.Instance.GetGameItemsUpdatedSinceAsync(_lastGameItemsCheckUtc);

                if (changed != null && changed.Count > 0)
                {
                    List<SupabaseGameItem> external;
                    lock (_recentLock)
                    {
                        external = changed
                            .Where(c => !_recentLocalItemUpdates.Contains(c.Id))
                            .ToList();
                    }

                    if (external.Count > 0)
                    {
                        if (AccountManager.DebugModeAtivo)
                            Console.WriteLine($"[InventorySync] {external.Count} alterações externas de game_items detectadas");

                        GameItemsChanged?.Invoke(this, external);
                    }

                    _lastGameItemsCheckUtc = changed.Max(c => c.UpdatedAt).ToUniversalTime();
                }
            }
            catch (Exception ex)
            {
                if (AccountManager.DebugModeAtivo)
                    Console.WriteLine($"[InventorySync] Erro poll game_items: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _client?.Dispose();
        }
    }
}
