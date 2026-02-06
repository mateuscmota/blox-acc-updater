using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RBX_Alt_Manager.Classes
{
    public class TwofaSyncService : IDisposable
    {
        private static TwofaSyncService _instance;
        public static TwofaSyncService Instance => _instance ?? (_instance = new TwofaSyncService());

        private System.Windows.Forms.Timer _pollTimer;
        private DateTime _lastCheckUtc = DateTime.UtcNow;
        private bool _isPolling = false;
        private const int POLL_INTERVAL_MS = 5000;

        public event EventHandler<List<TwofaUpdate>> TwofaSecretChanged;

        private readonly HashSet<string> _recentLocalUpdates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _recentLock = new object();

        private TwofaSyncService() { }

        public void Start()
        {
            if (_pollTimer != null) return;

            _lastCheckUtc = DateTime.UtcNow;
            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = POLL_INTERVAL_MS;
            _pollTimer.Tick += async (s, e) => await PollForChangesAsync();
            _pollTimer.Start();

            if (AccountManager.DebugModeAtivo)
                Console.WriteLine("[TwofaSync] Polling iniciado (5s)");
        }

        public void Stop()
        {
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        public void MarkLocalUpdate(string username)
        {
            lock (_recentLock)
            {
                _recentLocalUpdates.Add(username);
            }
            Task.Delay(5000).ContinueWith(_ =>
            {
                lock (_recentLock)
                {
                    _recentLocalUpdates.Remove(username);
                }
            });
        }

        private async Task PollForChangesAsync()
        {
            if (_isPolling) return;
            _isPolling = true;

            try
            {
                DateTime pollTime = DateTime.UtcNow;
                var changed = await SupabaseManager.Instance.GetAccountsTwofaUpdatedSinceAsync(_lastCheckUtc);

                if (changed != null && changed.Count > 0)
                {
                    List<TwofaUpdate> external;
                    lock (_recentLock)
                    {
                        external = changed
                            .Where(c => !_recentLocalUpdates.Contains(c.Username))
                            .Select(c => new TwofaUpdate { Username = c.Username, Secret = c.TwofaSecret })
                            .ToList();
                    }

                    if (external.Count > 0)
                    {
                        if (AccountManager.DebugModeAtivo)
                            Console.WriteLine($"[TwofaSync] {external.Count} alterações externas de 2FA detectadas");

                        TwofaSecretChanged?.Invoke(this, external);
                    }

                    _lastCheckUtc = changed.Max(c => c.UpdatedAt);
                }
                else
                {
                    _lastCheckUtc = pollTime;
                }
            }
            catch (Exception ex)
            {
                if (AccountManager.DebugModeAtivo)
                    Console.WriteLine($"[TwofaSync] Erro poll: {ex.Message}");
            }
            finally
            {
                _isPolling = false;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public class TwofaUpdate
    {
        public string Username { get; set; }
        public string Secret { get; set; }
    }
}
