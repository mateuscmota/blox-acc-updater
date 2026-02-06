using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RBX_Alt_Manager.Classes
{
    /// <summary>
    /// Gerencia todas as operações com o Supabase
    /// </summary>
    public class SupabaseManager
    {
        private static SupabaseManager _instance;
        public static SupabaseManager Instance => _instance ?? (_instance = new SupabaseManager());

        private static readonly string SUPABASE_URL = AppSecrets.SupabaseUrl;
        private static readonly string SUPABASE_KEY = AppSecrets.SupabaseKey;

        private readonly HttpClient _client;

        // Cache para reduzir chamadas API redundantes
        private List<SupabaseGame> _cachedGames;
        private List<SupabaseGameItem> _cachedAllItems;
        private DateTime _gamesCacheTime = DateTime.MinValue;
        private DateTime _itemsCacheTime = DateTime.MinValue;
        private const int CACHE_TTL_SECONDS = 300; // 5 minutos

        public void InvalidateGamesCache() { _cachedGames = null; _gamesCacheTime = DateTime.MinValue; }
        public void InvalidateItemsCache() { _cachedAllItems = null; _itemsCacheTime = DateTime.MinValue; }

        // Propriedades de autenticação
        public bool IsAuthenticated { get; private set; }
        public SupabaseUser CurrentUser { get; private set; }

        // Evento para notificar mudanças de autenticação
        public event EventHandler<bool> AuthenticationChanged;

        private SupabaseManager()
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("apikey", SUPABASE_KEY);
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {SUPABASE_KEY}");
            _client.Timeout = TimeSpan.FromSeconds(15);
        }

        #region Authentication

        /// <summary>
        /// Faz login com username e senha
        /// </summary>
        public async Task<(bool success, string message)> LoginAsync(string username, string password)
        {
            try
            {
                // Buscar usuário pelo username
                var response = await _client.GetAsync(
                    $"{SUPABASE_URL}/rest/v1/users?username=eq.{Uri.EscapeDataString(username)}&select=*");
                
                if (!response.IsSuccessStatusCode)
                {
                    return (false, "Erro ao conectar com o servidor");
                }

                var json = await response.Content.ReadAsStringAsync();
                var users = JsonConvert.DeserializeObject<List<SupabaseUser>>(json);

                if (users == null || users.Count == 0)
                {
                    return (false, "Usuário não encontrado");
                }

                var user = users[0];

                // Verificar senha (comparação simples - em produção usar hash)
                if (!VerifyPassword(password, user.PasswordHash))
                {
                    return (false, "Senha incorreta");
                }

                // Verificar se usuário está ativo
                if (!user.IsActive)
                {
                    return (false, "Usuário desativado. Contate o administrador.");
                }

                // Login bem-sucedido
                IsAuthenticated = true;
                CurrentUser = user;

                // Atualizar último login
                _ = UpdateLastLoginAsync(user.Id);

                AuthenticationChanged?.Invoke(this, true);

                return (true, $"Bem-vindo, {user.DisplayName ?? user.Username}!");
            }
            catch (Exception ex)
            {
                return (false, $"Erro: {ex.Message}");
            }
        }

        /// <summary>
        /// Faz logout
        /// </summary>
        public void Logout()
        {
            IsAuthenticated = false;
            CurrentUser = null;
            AuthenticationChanged?.Invoke(this, false);
        }

        /// <summary>
        /// Verifica a senha (comparação simples ou com hash)
        /// </summary>
        private bool VerifyPassword(string inputPassword, string storedPassword)
        {
            // Se a senha armazenada começa com $, assume que é um hash
            // Caso contrário, faz comparação direta (para simplicidade inicial)
            if (string.IsNullOrEmpty(storedPassword))
                return false;

            // Comparação simples (em produção, usar BCrypt ou similar)
            return inputPassword == storedPassword;
        }

        /// <summary>
        /// Atualiza o último login do usuário
        /// </summary>
        private async Task UpdateLastLoginAsync(int userId)
        {
            try
            {
                var data = new { last_login = DateTime.UtcNow };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/users?id=eq.{userId}", content);
                await _client.SendAsync(request);
            }
            catch { /* Ignora erro ao atualizar último login */ }
        }

        /// <summary>
        /// Verifica se o usuário atual tem determinada permissão
        /// </summary>
        public bool HasPermission(string permission)
        {
            if (!IsAuthenticated || CurrentUser == null)
                return false;

            // Admin tem todas as permissões
            if (CurrentUser.Role == "admin")
                return true;

            // Verificar permissões específicas
            if (CurrentUser.Permissions != null)
            {
                return CurrentUser.Permissions.Contains(permission);
            }

            return false;
        }

        /// <summary>
        /// Registra um novo usuário (apenas admin pode fazer isso)
        /// </summary>
        public async Task<(bool success, string message)> RegisterUserAsync(string username, string password, string displayName, string role = "user")
        {
            if (!IsAuthenticated || CurrentUser?.Role != "admin")
            {
                return (false, "Apenas administradores podem registrar novos usuários");
            }

            try
            {
                // Verificar se usuário já existe
                var checkResponse = await _client.GetAsync(
                    $"{SUPABASE_URL}/rest/v1/users?username=eq.{Uri.EscapeDataString(username)}&select=id");
                var checkJson = await checkResponse.Content.ReadAsStringAsync();
                var existing = JsonConvert.DeserializeObject<List<dynamic>>(checkJson);

                if (existing != null && existing.Count > 0)
                {
                    return (false, "Usuário já existe");
                }

                // Criar novo usuário
                var data = new
                {
                    username = username,
                    password_hash = password, // Em produção, fazer hash da senha
                    display_name = displayName,
                    role = role,
                    is_active = true
                };

                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync($"{SUPABASE_URL}/rest/v1/users", content);

                if (response.IsSuccessStatusCode)
                {
                    return (true, "Usuário criado com sucesso");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return (false, $"Erro ao criar usuário: {error}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Erro: {ex.Message}");
            }
        }

        /// <summary>
        /// Altera a senha do usuário atual
        /// </summary>
        public async Task<(bool success, string message)> ChangePasswordAsync(string currentPassword, string newPassword)
        {
            if (!IsAuthenticated || CurrentUser == null)
            {
                return (false, "Usuário não autenticado");
            }

            // Verificar senha atual
            if (!VerifyPassword(currentPassword, CurrentUser.PasswordHash))
            {
                return (false, "Senha atual incorreta");
            }

            try
            {
                var data = new { password_hash = newPassword }; // Em produção, fazer hash
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/users?id=eq.{CurrentUser.Id}", content);
                var response = await _client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    CurrentUser.PasswordHash = newPassword;
                    return (true, "Senha alterada com sucesso");
                }
                else
                {
                    return (false, "Erro ao alterar senha");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Erro: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifica se o email está na lista de emails permitidos no Supabase
        /// </summary>
        public async Task<bool> CheckAllowedEmailAsync(string email)
        {
            try
            {
                var response = await _client.GetAsync(
                    $"{SUPABASE_URL}/rest/v1/allowed_emails?email=eq.{Uri.EscapeDataString(email.ToLower())}&select=id");

                if (!response.IsSuccessStatusCode)
                    return false;

                var json = await response.Content.ReadAsStringAsync();
                var results = JsonConvert.DeserializeObject<List<dynamic>>(json);
                return results != null && results.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Faz login via Google OAuth. Busca usuário por email ou cria um novo.
        /// </summary>
        public async Task<(bool success, string message)> LoginWithGoogleAsync(string email, string displayName)
        {
            try
            {
                // Verificar se email está na lista de permitidos
                bool allowed = await CheckAllowedEmailAsync(email);
                if (!allowed)
                {
                    return (false, "Email não autorizado. Contate o administrador.");
                }

                // Buscar usuário pelo email
                var response = await _client.GetAsync(
                    $"{SUPABASE_URL}/rest/v1/users?email=eq.{Uri.EscapeDataString(email)}&select=*");

                if (!response.IsSuccessStatusCode)
                {
                    return (false, "Erro ao conectar com o servidor");
                }

                var json = await response.Content.ReadAsStringAsync();
                var users = JsonConvert.DeserializeObject<List<SupabaseUser>>(json);

                SupabaseUser user;

                if (users != null && users.Count > 0)
                {
                    user = users[0];
                }
                else
                {
                    // Criar novo usuário com dados do Google
                    var newUser = new
                    {
                        username = email.Split('@')[0] + "_google",
                        password_hash = Guid.NewGuid().ToString(),
                        display_name = displayName,
                        email = email,
                        role = "user",
                        is_active = true
                    };

                    var createJson = JsonConvert.SerializeObject(newUser);
                    var createContent = new StringContent(createJson, Encoding.UTF8, "application/json");

                    var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/rest/v1/users")
                    {
                        Content = createContent
                    };
                    createRequest.Headers.Add("Prefer", "return=representation");

                    var createResponse = await _client.SendAsync(createRequest);

                    if (!createResponse.IsSuccessStatusCode)
                    {
                        var error = await createResponse.Content.ReadAsStringAsync();
                        return (false, $"Erro ao criar usuário: {error}");
                    }

                    var createdJson = await createResponse.Content.ReadAsStringAsync();
                    var createdUsers = JsonConvert.DeserializeObject<List<SupabaseUser>>(createdJson);

                    if (createdUsers == null || createdUsers.Count == 0)
                    {
                        return (false, "Erro ao criar usuário");
                    }

                    user = createdUsers[0];
                }

                // Verificar se usuário está ativo
                if (!user.IsActive)
                {
                    return (false, "Usuário desativado. Contate o administrador.");
                }

                // Login bem-sucedido
                IsAuthenticated = true;
                CurrentUser = user;

                _ = UpdateLastLoginAsync(user.Id);

                AuthenticationChanged?.Invoke(this, true);

                return (true, $"Bem-vindo, {user.DisplayName ?? user.Email}!");
            }
            catch (Exception ex)
            {
                return (false, $"Erro: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Cria um HttpRequestMessage com método PATCH (compatível com .NET Framework)
        /// </summary>
        private HttpRequestMessage CreatePatchRequest(string url, HttpContent content)
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
            request.Content = content;
            return request;
        }

        #region Games

        /// <summary>
        /// Busca todos os jogos (exceto arquivados)
        /// </summary>
        public async Task<List<SupabaseGame>> GetGamesAsync()
        {
            // Retornar cache se válido
            if (_cachedGames != null && (DateTime.UtcNow - _gamesCacheTime).TotalSeconds < CACHE_TTL_SECONDS)
                return _cachedGames;

            try
            {
                var response = await _client.GetAsync($"{SUPABASE_URL}/rest/v1/games?select=*&order=name&limit=1000");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var games = JsonConvert.DeserializeObject<List<SupabaseGame>>(json) ?? new List<SupabaseGame>();
                    _cachedGames = games.Where(g => !g.Archived).ToList();
                    _gamesCacheTime = DateTime.UtcNow;
                    return _cachedGames;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar jogos: {ex.Message}");
            }
            return _cachedGames ?? new List<SupabaseGame>();
        }

        /// <summary>
        /// Adiciona um novo jogo
        /// </summary>
        public async Task<SupabaseGame> AddGameAsync(string name, string imageUrl = null)
        {
            try
            {
                var data = new { name = name, image_url = imageUrl };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/rest/v1/games");
                request.Content = content;
                request.Headers.Add("Prefer", "return=representation");

                var response = await _client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    InvalidateGamesCache();
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var games = JsonConvert.DeserializeObject<List<SupabaseGame>>(responseJson);
                    return games?.Count > 0 ? games[0] : null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao adicionar jogo: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Atualiza um jogo
        /// </summary>
        public async Task<bool> UpdateGameAsync(int gameId, string name, string imageUrl = null)
        {
            try
            {
                var data = new { name = name, image_url = imageUrl };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/games?id=eq.{gameId}", content);

                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar jogo: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Remove um jogo (cascade remove itens e inventário)
        /// </summary>
        public async Task<bool> DeleteGameAsync(int gameId)
        {
            try
            {
                var response = await _client.DeleteAsync($"{SUPABASE_URL}/rest/v1/games?id=eq.{gameId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao remover jogo: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Atualiza apenas o nome do jogo
        /// </summary>
        public async Task<bool> UpdateGameNameAsync(int gameId, string name)
        {
            try
            {
                var data = new { name = name, updated_at = DateTime.UtcNow };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/games?id=eq.{gameId}", content);

                var response = await _client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Erro Supabase rename game: {response.StatusCode} - {errorContent}");
                }
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao renomear jogo: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Atualiza nome e place_id do jogo
        /// </summary>
        public async Task<bool> UpdateGameDetailsAsync(int gameId, string name, long? placeId)
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["updated_at"] = DateTime.UtcNow
                };
                if (placeId.HasValue && placeId.Value > 0)
                    data["place_id"] = placeId.Value;
                else
                    data["place_id"] = null;

                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/games?id=eq.{gameId}", content);

                var response = await _client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Erro Supabase update game details: {response.StatusCode} - {errorContent}");
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar detalhes do jogo: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Arquiva um jogo (soft delete)
        /// </summary>
        public async Task<bool> ArchiveGameAsync(int gameId, bool archived = true)
        {
            try
            {
                var data = new { archived = archived, updated_at = DateTime.UtcNow };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/games?id=eq.{gameId}", content);

                var response = await _client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Erro Supabase archive game: {response.StatusCode} - {errorContent}");
                }

                if (response.IsSuccessStatusCode)
                {
                    InvalidateGamesCache();
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao arquivar jogo: {ex.Message}");
            }
            return false;
        }

        #endregion

        #region Game Items

        /// <summary>
        /// Busca todos os itens de um jogo (exceto arquivados)
        /// </summary>
        public async Task<List<SupabaseGameItem>> GetGameItemsAsync(int gameId)
        {
            // Usar cache global se disponível (filtrando por gameId)
            var allItems = await GetAllGameItemsAsync();
            return allItems.Where(i => i.GameId == gameId).ToList();
        }

        /// <summary>
        /// Busca TODOS os itens de todos os jogos (exceto arquivados) em uma única chamada
        /// </summary>
        public async Task<List<SupabaseGameItem>> GetAllGameItemsAsync()
        {
            // Retornar cache se válido
            if (_cachedAllItems != null && (DateTime.UtcNow - _itemsCacheTime).TotalSeconds < CACHE_TTL_SECONDS)
                return _cachedAllItems;

            try
            {
                var response = await _client.GetAsync($"{SUPABASE_URL}/rest/v1/game_items?select=*&order=name");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var items = JsonConvert.DeserializeObject<List<SupabaseGameItem>>(json) ?? new List<SupabaseGameItem>();
                    _cachedAllItems = items.Where(i => !i.Archived).ToList();
                    _itemsCacheTime = DateTime.UtcNow;
                    return _cachedAllItems;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar todos os itens: {ex.Message}");
            }
            return _cachedAllItems ?? new List<SupabaseGameItem>();
        }

        /// <summary>
        /// Adiciona um novo item ao jogo
        /// </summary>
        public async Task<SupabaseGameItem> AddGameItemAsync(int gameId, string name)
        {
            try
            {
                var data = new { game_id = gameId, name = name };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/rest/v1/game_items");
                request.Content = content;
                request.Headers.Add("Prefer", "return=representation");

                var response = await _client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    InvalidateItemsCache();
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var items = JsonConvert.DeserializeObject<List<SupabaseGameItem>>(responseJson);
                    return items?.Count > 0 ? items[0] : null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao adicionar item: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Atualiza um item
        /// </summary>
        public async Task<bool> UpdateGameItemAsync(int itemId, string name)
        {
            try
            {
                var data = new { name = name };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/game_items?id=eq.{itemId}", content);

                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar item: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Remove um item (cascade remove inventário)
        /// </summary>
        public async Task<bool> DeleteGameItemAsync(int itemId)
        {
            try
            {
                var response = await _client.DeleteAsync($"{SUPABASE_URL}/rest/v1/game_items?id=eq.{itemId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao remover item: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Atualiza apenas o nome do item
        /// </summary>
        public async Task<bool> UpdateGameItemNameAsync(int itemId, string name)
        {
            try
            {
                var data = new { name = name };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/game_items?id=eq.{itemId}", content);

                var response = await _client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Erro Supabase rename item: {response.StatusCode} - {errorContent}");
                }
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao renomear item: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Arquiva um item (soft delete)
        /// </summary>
        public async Task<bool> ArchiveGameItemAsync(int itemId, bool archived = true)
        {
            try
            {
                var data = new { archived = archived };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/game_items?id=eq.{itemId}", content);

                var response = await _client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Erro Supabase archive item: {response.StatusCode} - {errorContent}");
                }

                if (response.IsSuccessStatusCode)
                {
                    InvalidateItemsCache();
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao arquivar item: {ex.Message}");
            }
            return false;
        }

        #endregion

        #region Inventory

        /// <summary>
        /// Busca inventário de um item (todas as contas)
        /// </summary>
        /// <summary>
        /// Busca inventário de um usuário específico
        /// </summary>
        public async Task<List<SupabaseInventoryEntry>> GetInventoryByUsernameAsync(string username)
        {
            try
            {
                var response = await _client.GetAsync($"{SUPABASE_URL}/rest/v1/inventory?username=eq.{Uri.EscapeDataString(username)}&select=*&order=item_id");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<SupabaseInventoryEntry>>(json) ?? new List<SupabaseInventoryEntry>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar inventário por usuário: {ex.Message}");
            }
            return new List<SupabaseInventoryEntry>();
        }

        public async Task<List<SupabaseInventoryEntry>> GetInventoryByItemAsync(int itemId)
        {
            try
            {
                var response = await _client.GetAsync($"{SUPABASE_URL}/rest/v1/inventory?item_id=eq.{itemId}&select=*&order=username");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<SupabaseInventoryEntry>>(json) ?? new List<SupabaseInventoryEntry>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar inventário: {ex.Message}");
            }
            return new List<SupabaseInventoryEntry>();
        }

        /// <summary>
        /// Busca inventário de múltiplos itens em uma única requisição (OTIMIZADO)
        /// </summary>
        public async Task<Dictionary<int, List<SupabaseInventoryEntry>>> GetInventoryByItemIdsAsync(List<int> itemIds)
        {
            var result = new Dictionary<int, List<SupabaseInventoryEntry>>();
            
            if (itemIds == null || itemIds.Count == 0)
                return result;

            try
            {
                // Buscar todo inventário dos itens em uma única requisição
                var idsParam = string.Join(",", itemIds);
                var response = await _client.GetAsync($"{SUPABASE_URL}/rest/v1/inventory?item_id=in.({idsParam})&select=*&order=username");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var allInventory = JsonConvert.DeserializeObject<List<SupabaseInventoryEntry>>(json) ?? new List<SupabaseInventoryEntry>();
                    
                    // Inicializar dicionário com listas vazias
                    foreach (var itemId in itemIds)
                    {
                        result[itemId] = new List<SupabaseInventoryEntry>();
                    }
                    
                    // Agrupar por item_id
                    foreach (var inv in allInventory)
                    {
                        if (result.ContainsKey(inv.ItemId))
                        {
                            result[inv.ItemId].Add(inv);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar inventário em lote: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Busca TODO o inventário de um jogo usando foreign key filter (ULTRA OTIMIZADO - 1 requisição)
        /// </summary>
        public async Task<List<SupabaseInventoryEntry>> GetInventoryByGameIdAsync(int gameId)
        {
            try
            {
                // Usar foreign key para filtrar diretamente pelo game_id do item
                // Isso faz JOIN automático e filtra em uma única requisição
                var response = await _client.GetAsync(
                    $"{SUPABASE_URL}/rest/v1/inventory?select=*,game_items!inner(game_id)&game_items.game_id=eq.{gameId}&order=username&limit=50000");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<SupabaseInventoryEntry>>(json) ?? new List<SupabaseInventoryEntry>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar inventário por jogo: {ex.Message}");
            }
            return new List<SupabaseInventoryEntry>();
        }

        /// <summary>
        /// Busca inventário completo de um jogo (todos itens, todas contas)
        /// </summary>
        public async Task<List<SupabaseInventoryWithItem>> GetFullInventoryByGameAsync(int gameId)
        {
            try
            {
                // Join inventory com game_items
                var response = await _client.GetAsync(
                    $"{SUPABASE_URL}/rest/v1/inventory?select=*,game_items!inner(id,name,game_id)&game_items.game_id=eq.{gameId}");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<SupabaseInventoryWithItem>>(json) ?? new List<SupabaseInventoryWithItem>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar inventário completo: {ex.Message}");
            }
            return new List<SupabaseInventoryWithItem>();
        }

        /// <summary>
        /// Adiciona ou atualiza quantidade no inventário (UPSERT)
        /// </summary>
        public async Task<bool> UpsertInventoryAsync(string username, int itemId, long quantity)
        {
            try
            {
                var data = new { username = username, item_id = itemId, quantity = quantity, updated_at = DateTime.UtcNow };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/rest/v1/inventory?on_conflict=username,item_id");
                request.Content = content;
                request.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");

                var response = await _client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var responseJson = await response.Content.ReadAsStringAsync();
                        var entries = JsonConvert.DeserializeObject<List<SupabaseInventoryEntry>>(responseJson);
                        if (entries?.Count > 0)
                            InventorySyncService.Instance.MarkLocalUpdate(entries[0].Id);
                    }
                    catch { }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Erro Supabase UPSERT inventory: {response.StatusCode} - {errorContent}");
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao upsert inventário: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Atualiza apenas a quantidade de um registro específico
        /// </summary>
        public async Task<bool> UpdateInventoryQuantityAsync(int inventoryId, long quantity)
        {
            try
            {
                var data = new { quantity = quantity, updated_at = DateTime.UtcNow };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest($"{SUPABASE_URL}/rest/v1/inventory?id=eq.{inventoryId}", content);

                var response = await _client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    InventorySyncService.Instance.MarkLocalUpdate(inventoryId);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar quantidade: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Remove uma conta de um item
        /// </summary>
        public async Task<bool> DeleteInventoryAsync(int inventoryId)
        {
            try
            {
                var response = await _client.DeleteAsync($"{SUPABASE_URL}/rest/v1/inventory?id=eq.{inventoryId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao remover do inventário: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Remove uma conta de um item pelo username e item_id
        /// </summary>
        public async Task<bool> DeleteInventoryByUsernameAsync(string username, int itemId)
        {
            try
            {
                var response = await _client.DeleteAsync(
                    $"{SUPABASE_URL}/rest/v1/inventory?username=eq.{Uri.EscapeDataString(username)}&item_id=eq.{itemId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao remover do inventário: {ex.Message}");
            }
            return false;
        }

        #endregion

        #region Accounts

        /// <summary>
        /// Busca todas as contas
        /// </summary>
        public async Task<List<SupabaseAccount>> GetAccountsAsync(bool includeArchived = true)
        {
            try
            {
                // Adiciona limit=10000 para garantir que busca todas as contas
                var url = $"{SUPABASE_URL}/rest/v1/accounts?select=*&order=username&limit=10000";
                
                // Filtrar arquivadas apenas se a coluna existir e includeArchived for false
                // Por enquanto, busca todas as contas para evitar erro se coluna não existir
                // Depois de adicionar a coluna 'archived' no Supabase, descomente:
                // if (!includeArchived)
                // {
                //     url += "&or=(archived.is.null,archived.eq.false)";
                // }
                
                var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var accounts = JsonConvert.DeserializeObject<List<SupabaseAccount>>(json) ?? new List<SupabaseAccount>();
                    
                    // Filtrar localmente por enquanto (funciona mesmo sem coluna no banco)
                    if (!includeArchived)
                    {
                        accounts = accounts.Where(a => !a.Archived).ToList();
                    }
                    
                    return accounts;
                }
                else
                {
                    var errorJson = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Erro ao buscar contas ({response.StatusCode}): {errorJson}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar contas: {ex.Message}");
            }
            return new List<SupabaseAccount>();
        }

        /// <summary>
        /// Busca uma conta pelo username
        /// </summary>
        public async Task<SupabaseAccount> GetAccountByUsernameAsync(string username)
        {
            try
            {
                var response = await _client.GetAsync($"{SUPABASE_URL}/rest/v1/accounts?username=eq.{Uri.EscapeDataString(username)}&select=*");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var accounts = JsonConvert.DeserializeObject<List<SupabaseAccount>>(json);
                    return accounts?.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao buscar conta: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Adiciona ou atualiza uma conta (UPSERT)
        /// </summary>
        public async Task<SupabaseAccount> UpsertAccountAsync(string username, string cookie, long? userId = null)
        {
            try
            {
                var data = new { 
                    username = username, 
                    cookie = cookie, 
                    user_id = userId,
                    updated_at = DateTime.UtcNow.ToString("o")
                };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // UPSERT usando on_conflict no header Prefer
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/rest/v1/accounts?on_conflict=username");
                request.Content = content;
                request.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");

                var response = await _client.SendAsync(request);
                var responseJson = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    var accounts = JsonConvert.DeserializeObject<List<SupabaseAccount>>(responseJson);
                    return accounts?.FirstOrDefault();
                }
                else
                {
                    Console.WriteLine($"Erro ao upsert conta ({response.StatusCode}): {responseJson}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao adicionar/atualizar conta: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Atualiza o cookie de uma conta
        /// </summary>
        public async Task<bool> UpdateAccountCookieAsync(string username, string cookie)
        {
            try
            {
                var data = new { cookie = cookie, updated_at = DateTime.UtcNow };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest(
                    $"{SUPABASE_URL}/rest/v1/accounts?username=eq.{Uri.EscapeDataString(username)}", content);

                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar cookie: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Remove uma conta
        /// </summary>
        public async Task<bool> DeleteAccountAsync(string username)
        {
            try
            {
                var response = await _client.DeleteAsync(
                    $"{SUPABASE_URL}/rest/v1/accounts?username=eq.{Uri.EscapeDataString(username)}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao remover conta: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Arquiva ou desarquiva uma conta
        /// </summary>
        public async Task<bool> ArchiveAccountAsync(string username, bool archived = true)
        {
            try
            {
                var data = new { archived = archived, updated_at = DateTime.UtcNow };
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreatePatchRequest(
                    $"{SUPABASE_URL}/rest/v1/accounts?username=eq.{Uri.EscapeDataString(username)}", content);

                var response = await _client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Erro Supabase archive: {response.StatusCode} - {errorContent}");
                    
                    // Se o erro for por causa da coluna 'archived' não existir, informar
                    if (errorContent.Contains("archived") || errorContent.Contains("column"))
                    {
                        Console.WriteLine("DICA: Verifique se a coluna 'archived' (boolean, default false) existe na tabela 'accounts' do Supabase");
                    }
                }
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao arquivar conta: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Sincroniza contas locais com o Supabase (upload)
        /// </summary>
        public async Task<int> SyncAccountsToCloudAsync(List<(string username, string cookie, long? userId)> localAccounts)
        {
            int synced = 0;
            var semaphore = new SemaphoreSlim(5);
            var tasks = localAccounts.Select(async account =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await UpsertAccountAsync(account.username, account.cookie, account.userId);
                    if (result != null) Interlocked.Increment(ref synced);
                }
                finally { semaphore.Release(); }
            });
            await Task.WhenAll(tasks);
            return synced;
        }

        /// <summary>
        /// Busca contas que não estão na lista local
        /// </summary>
        public async Task<List<SupabaseAccount>> GetNewAccountsFromCloudAsync(List<string> localUsernames)
        {
            var allAccounts = await GetAccountsAsync();
            var localSet = new HashSet<string>(localUsernames, StringComparer.OrdinalIgnoreCase);
            return allAccounts.Where(a => !localSet.Contains(a.Username)).ToList();
        }

        #endregion

        #region Account History

        /// <summary>
        /// Busca o nome do jogo via API pública do Roblox a partir do PlaceID.
        /// </summary>
        // HttpClient sem headers do Supabase para chamadas à API Roblox
        private static readonly HttpClient _robloxClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public async Task<string> GetGameNameByPlaceIdAsync(long placeId)
        {
            try
            {
                // 1. Obter universeId a partir do placeId
                var universeResponse = await _robloxClient.GetStringAsync(
                    $"https://apis.roblox.com/universes/v1/places/{placeId}/universe");
                var universeData = JObject.Parse(universeResponse);
                long universeId = universeData["universeId"]?.Value<long>() ?? 0;
                if (universeId == 0)
                {
                    if (AccountManager.DebugModeAtivo)
                        AccountManager.AddLog($"⚠️ [GameName] universeId=0 para placeId={placeId}, response: {universeResponse.Substring(0, Math.Min(universeResponse.Length, 200))}");
                    return null;
                }

                // 2. Obter detalhes do jogo a partir do universeId
                var gameResponse = await _robloxClient.GetStringAsync(
                    $"https://games.roblox.com/v1/games?universeIds={universeId}");
                var gameData = JObject.Parse(gameResponse);
                var gameName = gameData["data"]?[0]?["name"]?.Value<string>();

                if (AccountManager.DebugModeAtivo)
                    AccountManager.AddLog($"🔍 [GameName] placeId={placeId} → universeId={universeId} → \"{gameName}\"");

                return gameName;
            }
            catch (Exception ex)
            {
                if (AccountManager.DebugModeAtivo)
                    AccountManager.AddLog($"❌ [GameName] Erro para placeId={placeId}: {ex.Message}");
                return null;
            }
        }

        public async Task LogAccountAccessAsync(string accountUsername, long placeId = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(accountUsername)) return;

                var email = CurrentUser?.Email ?? CurrentUser?.Username ?? Environment.MachineName;
                var displayName = CurrentUser?.DisplayName ?? CurrentUser?.Username ?? Environment.MachineName;

                // Buscar nome do jogo via API Roblox
                string gameName = null;
                if (placeId > 0)
                {
                    try { gameName = await GetGameNameByPlaceIdAsync(placeId); } catch { }
                }

                // Tentar INSERT com game_name; se falhar (coluna pode não existir), tentar sem
                bool success = await TryInsertHistoryAsync(accountUsername, email, displayName, placeId, gameName);
                if (!success)
                {
                    // Retry sem game_name caso a coluna não exista no Supabase
                    success = await TryInsertHistoryAsync(accountUsername, email, displayName, placeId, null);
                }

                AccountManager.AddLog($"📝 [History] {accountUsername}: placeId={placeId}, game={gameName ?? "—"}, saved={success}");
            }
            catch (Exception ex)
            {
                AccountManager.AddLog($"❌ [History] Erro: {ex.Message}");
            }
        }

        private async Task<bool> TryInsertHistoryAsync(string accountUsername, string email, string displayName, long placeId, string gameName)
        {
            var dataDict = new Dictionary<string, object>
            {
                ["account_username"] = accountUsername,
                ["user_email"] = email,
                ["user_display_name"] = displayName,
                ["action"] = "launch",
                ["place_id"] = placeId > 0 ? (object)placeId : null
            };

            if (gameName != null)
                dataDict["game_name"] = gameName;

            var json = JsonConvert.SerializeObject(dataDict);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/rest/v1/account_history");
            request.Headers.Add("apikey", SUPABASE_KEY);
            request.Headers.Add("Authorization", $"Bearer {SUPABASE_KEY}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                if (AccountManager.DebugModeAtivo)
                    AccountManager.AddLog($"⚠️ [History] POST falhou [{(int)response.StatusCode}]: {errBody}");
            }

            return response.IsSuccessStatusCode;
        }

        public async Task<List<AccountHistoryEntry>> GetAccountHistoryAsync(string accountUsername)
        {
            try
            {
                var encodedUsername = Uri.EscapeDataString(accountUsername);
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{SUPABASE_URL}/rest/v1/account_history?account_username=eq.{encodedUsername}&order=created_at.desc&limit=50");
                request.Headers.Add("apikey", SUPABASE_KEY);
                request.Headers.Add("Authorization", $"Bearer {SUPABASE_KEY}");

                var response = await _client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<AccountHistoryEntry>>(content) ?? new List<AccountHistoryEntry>();
                }
            }
            catch { }

            return new List<AccountHistoryEntry>();
        }

        #endregion

        #region Shared Config (app_config table)

        /// <summary>
        /// Busca valor de configuração compartilhada do Supabase
        /// </summary>
        public async Task<string> GetSharedConfigAsync(string key)
        {
            try
            {
                var response = await _client.GetAsync(
                    $"{SUPABASE_URL}/rest/v1/app_config?key=eq.{Uri.EscapeDataString(key)}&select=value");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var entries = JsonConvert.DeserializeObject<List<AppConfigEntry>>(json);
                    return entries?.FirstOrDefault()?.Value;
                }
            }
            catch (Exception ex)
            {
                if (AccountManager.DebugModeAtivo)
                    System.Diagnostics.Debug.WriteLine($"[Supabase] GetSharedConfig error: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Salva/atualiza configuração compartilhada no Supabase (upsert)
        /// </summary>
        public async Task<bool> SetSharedConfigAsync(string key, string value)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new
                {
                    key = key,
                    value = value,
                    updated_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")
                });

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SUPABASE_URL}/rest/v1/app_config")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Prefer", "resolution=merge-duplicates");

                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                if (AccountManager.DebugModeAtivo)
                    System.Diagnostics.Debug.WriteLine($"[Supabase] SetSharedConfig error: {ex.Message}");
                return false;
            }
        }

        #endregion
    }

    #region Models

    public class SupabaseGame
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("image_url")]
        public string ImageUrl { get; set; }

        [JsonProperty("place_id")]
        public long? PlaceId { get; set; }

        [JsonProperty("archived")]
        public bool Archived { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    public class SupabaseGameItem
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("game_id")]
        public int GameId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("archived")]
        public bool Archived { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    public class SupabaseInventoryEntry
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("item_id")]
        public int ItemId { get; set; }

        [JsonProperty("quantity")]
        public long Quantity { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    public class SupabaseInventoryWithItem
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("item_id")]
        public int ItemId { get; set; }

        [JsonProperty("quantity")]
        public long Quantity { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("game_items")]
        public SupabaseGameItem GameItem { get; set; }
    }

    public class SupabaseAccount
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("cookie")]
        public string Cookie { get; set; }

        [JsonProperty("user_id")]
        public long? UserId { get; set; }

        [JsonProperty("archived")]
        public bool Archived { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Representa um usuário do sistema
    /// </summary>
    public class SupabaseUser
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("password_hash")]
        public string PasswordHash { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonProperty("role")]
        public string Role { get; set; } // "admin", "user", "viewer"

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("is_active")]
        public bool IsActive { get; set; }

        [JsonProperty("permissions")]
        public List<string> Permissions { get; set; }

        [JsonProperty("last_login")]
        public DateTime? LastLogin { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    public class AccountHistoryEntry
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("account_username")]
        public string AccountUsername { get; set; }

        [JsonProperty("user_email")]
        public string UserEmail { get; set; }

        [JsonProperty("user_display_name")]
        public string UserDisplayName { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("place_id")]
        public long? PlaceId { get; set; }

        [JsonProperty("game_name")]
        public string GameName { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    public class AppConfigEntry
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }

    #endregion
}
