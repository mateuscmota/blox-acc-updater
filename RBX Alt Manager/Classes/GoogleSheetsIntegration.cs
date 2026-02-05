using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace RBX_Alt_Manager.Classes
{
    public class GoogleSheetsIntegration
    {
        private readonly string _spreadsheetId;
        
        // HttpClient otimizado com connection pooling
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            MaxConnectionsPerServer = 20,
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0" } }
        };

        // GIDs das abas - carregado do arquivo de configuração
        public static Dictionary<string, long> GameSheets
        {
            get { return GamesConfig.Instance.GetGamesDictionary(); }
        }

        private Dictionary<long, List<SheetProduct>> _cache = new Dictionary<long, List<SheetProduct>>();
        private Dictionary<long, DateTime> _cacheTime = new Dictionary<long, DateTime>();
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(10);
        
        // Lock para evitar requisições duplicadas simultâneas
        private HashSet<long> _loadingGids = new HashSet<long>();
        private readonly object _loadingLock = new object();

        public string LastError { get; private set; }

        public GoogleSheetsIntegration(string spreadsheetId)
        {
            _spreadsheetId = spreadsheetId;
        }

        /// <summary>
        /// Pré-carrega todos os jogos em paralelo (chamar no startup)
        /// </summary>
        public async Task PreloadAllGamesAsync()
        {
            var tasks = GameSheets.Values.Select(gid => RefreshCacheAsync(gid));
            await Task.WhenAll(tasks);
        }

        public async Task<List<GameProducts>> GetAllProductsForUserAsync(string username)
        {
            var allProducts = new List<GameProducts>();

            // Executar todas as requisições em paralelo
            var tasks = GameSheets.Select(async game =>
            {
                try
                {
                    var products = await GetProductsAsync(username, game.Value);
                    if (products.Count > 0)
                    {
                        return new GameProducts
                        {
                            GameName = game.Key,
                            Gid = game.Value,
                            Products = products
                        };
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Sheets] Erro {game.Key}: {ex.Message}");
                }
                return null;
            }).ToList();

            var results = await Task.WhenAll(tasks);
            allProducts.AddRange(results.Where(r => r != null));

            return allProducts;
        }

        public async Task<List<SheetProduct>> GetProductsAsync(string username, long gid)
        {
            try
            {
                await EnsureCacheAsync(gid);
                if (!_cache.ContainsKey(gid)) return new List<SheetProduct>();

                return _cache[gid]
                    .Where(p => p.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return new List<SheetProduct>();
            }
        }

        public async Task<List<string>> GetUniqueItemsForGameAsync(long gid)
        {
            try
            {
                await EnsureCacheAsync(gid);
                if (!_cache.ContainsKey(gid)) return new List<string>();

                return _cache[gid]
                    .Where(p => !string.IsNullOrEmpty(p.Product) && p.QuantityInt > 0)
                    .Select(p => p.Product)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return new List<string>();
            }
        }

        public async Task<List<SheetProduct>> GetAccountsWithItemAsync(long gid, string product)
        {
            try
            {
                await EnsureCacheAsync(gid);
                if (!_cache.ContainsKey(gid)) return new List<SheetProduct>();

                return _cache[gid]
                    .Where(p => p.Product.Equals(product, StringComparison.OrdinalIgnoreCase) && p.QuantityInt > 0)
                    .OrderByDescending(p => p.QuantityInt)
                    .ToList();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return new List<SheetProduct>();
            }
        }

        public async Task<List<SheetProduct>> GetAllAccountsWithItemAsync(long gid, string product)
        {
            try
            {
                await EnsureCacheAsync(gid);
                if (!_cache.ContainsKey(gid)) return new List<SheetProduct>();

                return _cache[gid]
                    .Where(p => p.Product.Equals(product, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.QuantityInt)
                    .ToList();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return new List<SheetProduct>();
            }
        }

        /// <summary>
        /// Retorna contas que existem na planilha do jogo mas NÃO têm o item especificado
        /// </summary>
        public async Task<List<string>> GetAccountsWithoutItemAsync(long gid, string product)
        {
            try
            {
                await EnsureCacheAsync(gid);
                if (!_cache.ContainsKey(gid)) return new List<string>();

                // Pegar todas as contas únicas da planilha
                var allAccounts = _cache[gid]
                    .Select(p => p.Username)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Pegar contas que já têm esse item
                var accountsWithItem = _cache[gid]
                    .Where(p => p.Product.Equals(product, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Username)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Retornar contas que NÃO têm o item
                return allAccounts
                    .Where(a => !accountsWithItem.Contains(a))
                    .OrderBy(a => a)
                    .ToList();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return new List<string>();
            }
        }

        /// <summary>
        /// Busca a melhor conta disponível para adicionar um item, com priorização:
        /// 1. Contas completamente vazias (sem nenhum item com quantidade > 0)
        /// 2. Contas com estoque 0 em outro item (para reutilizar a linha)
        /// 3. Contas que não têm esse item específico
        /// </summary>
        public async Task<(string Username, SheetProduct ExistingProduct, int Priority)> GetBestAvailableAccountAsync(long gid, string targetProduct)
        {
            try
            {
                await EnsureCacheAsync(gid);
                if (!_cache.ContainsKey(gid)) return (null, null, 0);

                var allProducts = _cache[gid];
                
                // Agrupar por username
                var accountGroups = allProducts
                    .GroupBy(p => p.Username, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Contas que já têm o item alvo
                var accountsWithTargetItem = allProducts
                    .Where(p => p.Product.Equals(targetProduct, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Username)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // PRIORIDADE 1: Contas completamente vazias (todos os itens com quantidade 0)
                foreach (var group in accountGroups)
                {
                    if (accountsWithTargetItem.Contains(group.Key)) continue;
                    
                    // Verificar se TODOS os itens dessa conta têm quantidade 0
                    bool allZero = group.All(p => p.QuantityInt == 0);
                    if (allZero)
                    {
                        // Retornar o primeiro produto com quantidade 0 para reutilizar
                        var emptyProduct = group.First();
                        return (group.Key, emptyProduct, 1);
                    }
                }

                // PRIORIDADE 2: Contas com pelo menos um item com estoque 0
                foreach (var group in accountGroups)
                {
                    if (accountsWithTargetItem.Contains(group.Key)) continue;
                    
                    // Buscar um item com quantidade 0 para reutilizar
                    var zeroProduct = group.FirstOrDefault(p => p.QuantityInt == 0);
                    if (zeroProduct != null)
                    {
                        return (group.Key, zeroProduct, 2);
                    }
                }

                // PRIORIDADE 3: Contas que não têm o item (precisará adicionar nova linha)
                foreach (var group in accountGroups)
                {
                    if (!accountsWithTargetItem.Contains(group.Key))
                    {
                        return (group.Key, null, 3);
                    }
                }

                return (null, null, 0); // Todas as contas já têm o item
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return (null, null, 0);
            }
        }

        public async Task<Dictionary<string, int>> GetProductsWithTotalQuantityAsync(long gid)
        {
            try
            {
                await EnsureCacheAsync(gid);
                if (!_cache.ContainsKey(gid)) return new Dictionary<string, int>();

                return _cache[gid]
                    .Where(p => !string.IsNullOrEmpty(p.Product))
                    .GroupBy(p => p.Product)
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.QuantityInt));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Garante que o cache está atualizado (evita requisições duplicadas)
        /// </summary>
        private async Task EnsureCacheAsync(long gid)
        {
            if (_cacheTime.ContainsKey(gid) && DateTime.Now - _cacheTime[gid] < _cacheExpiration)
                return;

            // Evitar requisições duplicadas simultâneas
            lock (_loadingLock)
            {
                if (_loadingGids.Contains(gid))
                    return;
                _loadingGids.Add(gid);
            }

            try
            {
                await RefreshCacheAsync(gid);
            }
            finally
            {
                lock (_loadingLock)
                {
                    _loadingGids.Remove(gid);
                }
            }
        }

        public async Task RefreshCacheAsync(long gid)
        {
            try
            {
                string url = $"https://docs.google.com/spreadsheets/d/{_spreadsheetId}/export?format=csv&gid={gid}";
                var csv = await _httpClient.GetStringAsync(url);
                
                AccountManager.IncrementRequestCount();

                if (string.IsNullOrEmpty(csv)) return;

                _cache[gid] = ParseCsv(csv, gid);
                _cacheTime[gid] = DateTime.Now;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        public void InvalidateCache()
        {
            _cache.Clear();
            _cacheTime.Clear();
        }
        
        public void InvalidateCache(long gid)
        {
            if (_cache.ContainsKey(gid)) _cache.Remove(gid);
            if (_cacheTime.ContainsKey(gid)) _cacheTime.Remove(gid);
        }

        private List<SheetProduct> ParseCsv(string csv, long gid)
        {
            var products = new List<SheetProduct>();
            var lines = csv.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < lines.Length; i++)
            {
                try
                {
                    var cols = ParseCsvLine(lines[i]);
                    if (cols.Count < 4) continue;

                    string cookie = cols[0]?.Trim() ?? "";
                    string username = cols[1]?.Trim() ?? "";
                    string product = cols[2]?.Trim() ?? "";
                    string quantity = cols[3]?.Trim() ?? "";
                    string twoFASecret = cols.Count > 4 ? cols[4]?.Trim() ?? "" : "";
                    string itemType = cols.Count > 5 ? cols[5]?.Trim() ?? "" : product;

                    if (username.Contains(":"))
                        username = username.Split(':')[0];

                    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(product)) continue;
                    if (username.ToLower() == "usuário" || product.ToLower() == "produto") continue;

                    int qty = 0;
                    string cleanQty = System.Text.RegularExpressions.Regex.Replace(quantity, "[^0-9]", "");
                    int.TryParse(cleanQty, out qty);

                    if (string.IsNullOrEmpty(itemType)) itemType = product;

                    products.Add(new SheetProduct
                    {
                        Cookie = cookie,
                        Username = username,
                        Product = product,
                        Quantity = quantity,
                        QuantityInt = qty,
                        RowIndex = i + 1,
                        Gid = gid,
                        ItemType = itemType,
                        TwoFASecret = twoFASecret
                    });
                }
                catch { }
            }

            return products;
        }

        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            string current = "";

            foreach (char c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(current); current = ""; }
                else current += c;
            }
            result.Add(current);
            return result;
        }
    }

    public class SheetProduct
    {
        public string Cookie { get; set; }
        public string Username { get; set; }
        public string Product { get; set; }
        public string Quantity { get; set; }
        public int QuantityInt { get; set; }
        public int RowIndex { get; set; }
        public long Gid { get; set; }
        public string ItemType { get; set; }
        public string TwoFASecret { get; set; }
    }

    public class GameProducts
    {
        public string GameName { get; set; }
        public long Gid { get; set; }
        public List<SheetProduct> Products { get; set; } = new List<SheetProduct>();
    }
}
