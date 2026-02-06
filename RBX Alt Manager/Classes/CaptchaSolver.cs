using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using PuppeteerSharp;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;

namespace RBX_Alt_Manager.Classes
{
    public class CaptchaSolver
    {
        private readonly Account _account;
        private string _csrfToken;
        private string _challengeId;
        private string _challengeMetadata;
        private string _actionType = "Generic";
        private string _unifiedCaptchaId;
        
        // Token capturado
        private string _capturedToken = null;

        public CaptchaSolver(Account account)
        {
            _account = account;
        }

        /// <summary>
        /// Obtém o CSRF token necessário para requisições POST
        /// </summary>
        private async Task<string> GetCsrfTokenAsync()
        {
            try
            {
                var client = new RestClient("https://auth.roblox.com");
                var request = new RestRequest("/v2/logout", Method.Post);
                request.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");

                var response = await client.ExecuteAsync(request);
                
                if (response.Headers != null)
                {
                    foreach (var header in response.Headers)
                    {
                        if (header.Name.Equals("x-csrf-token", StringComparison.OrdinalIgnoreCase))
                        {
                            return header.Value?.ToString() ?? "";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao obter CSRF: {ex.Message}");
            }

            return "";
        }

        /// <summary>
        /// Faz requisição de join-game para capturar o challenge
        /// </summary>
        public async Task<CaptchaChallengeResult> RequestJoinGameAsync(long placeId, string jobId = null)
        {
            var result = new CaptchaChallengeResult();

            try
            {
                _csrfToken = await GetCsrfTokenAsync();

                var client = new RestClient("https://gamejoin.roblox.com");
                var request = new RestRequest("/v1/join-game-instance", Method.Post);
                
                request.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");
                request.AddHeader("x-csrf-token", _csrfToken);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Accept", "application/json");
                request.AddHeader("User-Agent", "Roblox/WinInet");
                request.AddHeader("Referer", "https://www.roblox.com/");

                var body = new JObject
                {
                    ["placeId"] = placeId
                };

                if (!string.IsNullOrEmpty(jobId))
                {
                    body["gameId"] = jobId;
                }

                request.AddJsonBody(body.ToString());

                Debug.WriteLine($"[JOIN-GAME] Fazendo requisição para placeId: {placeId}");
                var response = await client.ExecuteAsync(request);

                Debug.WriteLine($"[JOIN-GAME] Status: {response.StatusCode}");
                Debug.WriteLine($"[JOIN-GAME] Content: {response.Content?.Substring(0, Math.Min(200, response.Content?.Length ?? 0))}");

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    // Verificar headers de challenge
                    string challengeId = null;
                    string challengeType = null;
                    string challengeMetadataBase64 = null;
                    
                    // Capturar TODOS os headers para debug
                    var allHeaders = new StringBuilder();

                    if (response.Headers != null)
                    {
                        foreach (var header in response.Headers)
                        {
                            var name = header.Name?.ToLower() ?? "";
                            var value = header.Value?.ToString() ?? "";

                            allHeaders.AppendLine($"{header.Name}: {value}");
                            Debug.WriteLine($"[JOIN-GAME] Header: {name} = {value.Substring(0, Math.Min(50, value.Length))}...");

                            if (name == "rblx-challenge-id")
                                challengeId = value;
                            else if (name == "rblx-challenge-type")
                                challengeType = value;
                            else if (name == "rblx-challenge-metadata")
                                challengeMetadataBase64 = value;
                        }
                    }
                    
                    // Guardar todos os headers para debug
                    result.AllHeaders = allHeaders.ToString();

                    if (!string.IsNullOrEmpty(challengeId))
                    {
                        result.NeedsCaptcha = true;
                        result.ChallengeId = challengeId;
                        result.ChallengeType = challengeType;
                        result.ChallengeMetadataBase64 = challengeMetadataBase64;
                        
                        // Verificar se é Proof of Work
                        if (challengeType?.ToLower() == "proofofwork")
                        {
                            result.IsProofOfWork = true;
                            Debug.WriteLine("[JOIN-GAME] Challenge é Proof of Work!");
                        }
                        
                        // Decodificar o metadata para extrair actionType e outros dados
                        if (!string.IsNullOrEmpty(challengeMetadataBase64))
                        {
                            try
                            {
                                string metadataJson = Encoding.UTF8.GetString(Convert.FromBase64String(challengeMetadataBase64));
                                Debug.WriteLine($"[JOIN-GAME] Metadata decodificado: {metadataJson}");
                                
                                // Guardar JSON completo para debug
                                result.MetadataJson = metadataJson;
                                
                                var metadataObj = JObject.Parse(metadataJson);
                                result.ActionType = metadataObj["actionType"]?.ToString() ?? "Generic";
                                
                                // Tentar ambas variações: unifiedCaptchaId e UnifiedCaptchaId
                                result.UnifiedCaptchaId = metadataObj["unifiedCaptchaId"]?.ToString() 
                                    ?? metadataObj["UnifiedCaptchaId"]?.ToString();
                                result.DataExchangeBlob = metadataObj["dataExchangeBlob"]?.ToString();
                                
                                // Extrair dados do PoW se disponíveis
                                result.SessionId = metadataObj["sessionId"]?.ToString();
                                result.Artifacts = metadataObj["artifacts"]?.ToString();
                                
                                // Extrair genericChallengeId do sharedParameters
                                var sharedParams = metadataObj["sharedParameters"];
                                if (sharedParams != null)
                                {
                                    result.GenericChallengeId = sharedParams["genericChallengeId"]?.ToString();
                                }
                                
                                Debug.WriteLine($"[JOIN-GAME] ActionType: {result.ActionType}");
                                Debug.WriteLine($"[JOIN-GAME] UnifiedCaptchaId: {result.UnifiedCaptchaId}");
                                Debug.WriteLine($"[JOIN-GAME] SessionId: {result.SessionId}");
                                Debug.WriteLine($"[JOIN-GAME] GenericChallengeId: {result.GenericChallengeId}");
                                Debug.WriteLine($"[JOIN-GAME] Artifacts: {result.Artifacts?.Substring(0, Math.Min(100, result.Artifacts?.Length ?? 0))}...");
                                Debug.WriteLine($"[JOIN-GAME] DataExchangeBlob: {result.DataExchangeBlob?.Substring(0, Math.Min(50, result.DataExchangeBlob?.Length ?? 0))}...");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[JOIN-GAME] Erro ao decodificar metadata: {ex.Message}");
                                result.ActionType = "Generic";
                            }
                        }
                        
                        result.ChallengeUrl = BuildChallengeUrl(challengeId, challengeMetadataBase64);
                        result.Message = result.IsProofOfWork ? "Proof of Work necessário" : "Captcha necessário";
                        
                        // Salvar para uso posterior
                        _challengeId = challengeId;
                        _challengeMetadata = challengeMetadataBase64;
                        _actionType = result.ActionType;
                        _unifiedCaptchaId = result.UnifiedCaptchaId ?? challengeId;
                    }
                    else
                    {
                        result.Message = $"Acesso negado sem challenge: {response.Content}";
                    }
                }
                else if (response.StatusCode == HttpStatusCode.OK)
                {
                    result.Success = true;
                    result.Message = "Conta não precisa de captcha";
                }
                else
                {
                    result.Message = $"Erro {response.StatusCode}: {response.Content}";
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Erro: {ex.Message}";
                Debug.WriteLine($"[JOIN-GAME] Exceção: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Resolve o captcha de "loading bar" repetindo requisições até passar
        /// </summary>
        public async Task<bool> SolveLoadingBarCaptchaAsync(long placeId = 2753915549, IProgress<string> progress = null)
        {
            int maxAttempts = 12; // ~60 segundos total
            int delayBetweenAttempts = 5000; // 5 segundos

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                progress?.Report($"Tentativa {attempt}/{maxAttempts}...");
                Debug.WriteLine($"Loading bar captcha - Tentativa {attempt}/{maxAttempts}");

                try
                {
                    _csrfToken = await GetCsrfTokenAsync();

                    var client = new RestClient("https://gamejoin.roblox.com");
                    var request = new RestRequest("/v1/join-game-instance", Method.Post);

                    request.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");
                    request.AddHeader("x-csrf-token", _csrfToken);
                    request.AddHeader("Content-Type", "application/json");
                    request.AddHeader("Accept", "application/json");
                    request.AddHeader("User-Agent", "Roblox/WinInet");
                    request.AddHeader("Referer", "https://www.roblox.com/");

                    var body = new JObject
                    {
                        ["placeId"] = placeId
                    };

                    request.AddJsonBody(body.ToString());

                    var response = await client.ExecuteAsync(request);

                    Debug.WriteLine($"Resposta: {response.StatusCode} - {response.Content}");

                    // Se passou (200 OK), sucesso!
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        progress?.Report("✅ Captcha resolvido!");
                        return true;
                    }

                    // Se ainda tem challenge, verificar tipo
                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        string challengeType = null;
                        if (response.Headers != null)
                        {
                            foreach (var header in response.Headers)
                            {
                                if (header.Name?.ToLower() == "rblx-challenge-type")
                                {
                                    challengeType = header.Value?.ToString();
                                    break;
                                }
                            }
                        }

                        // Se for captcha visual (FunCaptcha), não dá pra resolver com retry
                        if (challengeType == "captcha")
                        {
                            progress?.Report("⚠️ FunCaptcha detectado - precisa extensão");
                            Debug.WriteLine("FunCaptcha detectado, retry não vai funcionar");
                            return false;
                        }
                    }

                    // Aguardar antes da próxima tentativa
                    if (attempt < maxAttempts)
                    {
                        progress?.Report($"Aguardando {delayBetweenAttempts / 1000}s...");
                        await Task.Delay(delayBetweenAttempts);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro na tentativa {attempt}: {ex.Message}");
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(delayBetweenAttempts);
                    }
                }
            }

            progress?.Report("❌ Tempo esgotado");
            return false;
        }

        /// <summary>
        /// Monta a URL do challenge
        /// </summary>
        private string BuildChallengeUrl(string challengeId, string metadata = null)
        {
            var sb = new StringBuilder();
            sb.Append("https://www.roblox.com/challenge/cdn/hybrid?");
            sb.Append("generic-challenge-type=captcha");
            sb.Append("&app-type=windows");
            sb.Append($"&generic-challenge-id={Uri.EscapeDataString(challengeId)}");
            sb.Append("&challenge-type=generic");
            sb.Append("&dark-mode=false");

            if (!string.IsNullOrEmpty(metadata))
            {
                sb.Append($"&challenge-metadata-json={Uri.EscapeDataString(metadata)}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Verifica se o captcha foi resolvido fazendo uma nova requisição join-game
        /// A página hybrid do Roblox já chama /continue automaticamente quando resolvido
        /// </summary>
        private async Task<bool> VerifyCaptchaSolvedAsync()
        {
            try
            {
                Debug.WriteLine("[VERIFY] Verificando se captcha foi resolvido...");
                
                // Obter CSRF token atualizado
                _csrfToken = await GetCsrfTokenAsync();

                var client = new RestClient("https://gamejoin.roblox.com");
                var request = new RestRequest("/v1/join-game-instance", Method.Post);

                request.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");
                request.AddHeader("x-csrf-token", _csrfToken);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Accept", "application/json");
                request.AddHeader("User-Agent", "Roblox/WinInet");
                request.AddHeader("Referer", "https://www.roblox.com/");

                var body = new JObject
                {
                    ["placeId"] = 2753915549 // Blox Fruits
                };

                request.AddJsonBody(body.ToString());

                var response = await client.ExecuteAsync(request);

                Debug.WriteLine($"[VERIFY] Status: {response.StatusCode}");
                Debug.WriteLine($"[VERIFY] Content: {response.Content?.Substring(0, Math.Min(200, response.Content?.Length ?? 0))}");

                // Se retornou 200 = conta liberada!
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    Debug.WriteLine("[VERIFY] SUCESSO! Conta não precisa mais de captcha!");
                    return true;
                }

                // Se retornou 403 com challenge = ainda precisa de captcha
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    bool hasChallenge = false;
                    if (response.Headers != null)
                    {
                        foreach (var header in response.Headers)
                        {
                            if (header.Name?.ToLower() == "rblx-challenge-id")
                            {
                                hasChallenge = true;
                                break;
                            }
                        }
                    }

                    if (hasChallenge)
                    {
                        Debug.WriteLine("[VERIFY] Ainda precisa de captcha");
                        return false;
                    }
                    else
                    {
                        // 403 sem challenge pode significar outro erro, mas não captcha
                        Debug.WriteLine("[VERIFY] 403 sem challenge - pode estar ok");
                        return true;
                    }
                }

                // Outros status = provavelmente ok
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VERIFY] Erro: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Envia o token de captcha resolvido para a API do Roblox
        /// Fluxo: 1) POST /continue, 2) Repetir join-game com headers de challenge
        /// </summary>
        private async Task<bool> SendContinueRequestAsync(string captchaToken)
        {
            try
            {
                if (string.IsNullOrEmpty(_challengeId))
                {
                    Debug.WriteLine("[ERRO] Challenge ID não disponível");
                    AccountManager.AddLog("Challenge ID não disponível!");
                    return false;
                }

                if (string.IsNullOrEmpty(captchaToken))
                {
                    Debug.WriteLine("[ERRO] Captcha token vazio");
                    AccountManager.AddLog("Captcha token vazio!");
                    return false;
                }

                // Obter CSRF token atualizado
                _csrfToken = await GetCsrfTokenAsync();
                Debug.WriteLine($"[INFO] CSRF Token: {_csrfToken?.Substring(0, Math.Min(20, _csrfToken?.Length ?? 0))}...");

                // ============ PASSO 1: POST /challenge/v1/continue ============
                var client = new RestClient("https://apis.roblox.com");
                var request = new RestRequest("/challenge/v1/continue", Method.Post);

                request.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");
                request.AddHeader("x-csrf-token", _csrfToken);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Accept", "application/json");
                request.AddHeader("User-Agent", "Roblox/WinInet");
                request.AddHeader("Referer", "https://www.roblox.com/");
                request.AddHeader("Origin", "https://www.roblox.com");

                // Construir o challengeMetadata com valores corretos do challenge original
                // IMPORTANTE: O concorrente usa "UnifiedCaptchaId" (U maiúsculo) e NÃO envia actionType!
                string unifiedCaptchaId = !string.IsNullOrEmpty(_unifiedCaptchaId) ? _unifiedCaptchaId : _challengeId;
                
                Debug.WriteLine($"[INFO] UnifiedCaptchaId: {unifiedCaptchaId}");
                
                // Formato correto: apenas UnifiedCaptchaId e captchaToken (sem actionType!)
                var challengeMetadataObj = new JObject
                {
                    ["UnifiedCaptchaId"] = unifiedCaptchaId,  // U maiúsculo!
                    ["captchaToken"] = captchaToken
                    // NÃO incluir actionType - o concorrente não envia!
                };

                string challengeMetadataStr = JsonConvert.SerializeObject(challengeMetadataObj);

                var payload = new JObject
                {
                    ["challengeId"] = _challengeId,
                    ["challengeType"] = "captcha",
                    ["challengeMetadata"] = challengeMetadataStr
                };

                string payloadStr = payload.ToString(Formatting.None);
                request.AddStringBody(payloadStr, ContentType.Json);

                Debug.WriteLine($"[INFO] PASSO 1: Enviando /continue...");
                Debug.WriteLine($"[INFO] Challenge ID: {_challengeId}");
                Debug.WriteLine($"[INFO] Token (início): {captchaToken.Substring(0, Math.Min(80, captchaToken.Length))}...");

                var response = await client.ExecuteAsync(request);

                Debug.WriteLine($"[RESULTADO] Continue Status: {response.StatusCode}");
                Debug.WriteLine($"[RESULTADO] Continue Content: {response.Content}");

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    AccountManager.AddLog($"Erro /continue: {response.StatusCode}");
                    return false;
                }

                // ============ PASSO 2: Repetir join-game com headers de challenge ============
                Debug.WriteLine($"[INFO] PASSO 2: Repetindo join-game com headers de challenge...");

                // Base64 encode do challengeMetadata para o header
                string challengeMetadataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(challengeMetadataStr));

                var joinClient = new RestClient("https://gamejoin.roblox.com");
                var joinRequest = new RestRequest("/v1/join-game-instance", Method.Post);

                joinRequest.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");
                joinRequest.AddHeader("x-csrf-token", _csrfToken);
                joinRequest.AddHeader("Content-Type", "application/json");
                joinRequest.AddHeader("Accept", "application/json");
                joinRequest.AddHeader("User-Agent", "Roblox/WinInet");
                joinRequest.AddHeader("Referer", "https://www.roblox.com/");
                
                // Headers especiais de challenge - ESSENCIAL!
                joinRequest.AddHeader("Rblx-Challenge-Id", _challengeId);
                joinRequest.AddHeader("Rblx-Challenge-Metadata", challengeMetadataBase64);
                joinRequest.AddHeader("Rblx-Challenge-Type", "captcha");

                var joinBody = new JObject
                {
                    ["placeId"] = 2753915549 // Blox Fruits - pode ser qualquer jogo
                };

                joinRequest.AddJsonBody(joinBody.ToString());

                var joinResponse = await joinClient.ExecuteAsync(joinRequest);

                Debug.WriteLine($"[RESULTADO] Join-game Status: {joinResponse.StatusCode}");
                Debug.WriteLine($"[RESULTADO] Join-game Content: {joinResponse.Content}");

                // 200 = sucesso, 403 ainda com challenge = falhou
                if (joinResponse.StatusCode == HttpStatusCode.OK)
                {
                    Debug.WriteLine("[SUCESSO] Captcha resolvido! Join-game retornou 200.");
                    return true;
                }
                else if (joinResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    // Verificar se ainda tem challenge
                    bool stillHasChallenge = false;
                    if (joinResponse.Headers != null)
                    {
                        foreach (var header in joinResponse.Headers)
                        {
                            if (header.Name?.ToLower() == "rblx-challenge-id")
                            {
                                stillHasChallenge = true;
                                break;
                            }
                        }
                    }

                    if (stillHasChallenge)
                    {
                        AccountManager.AddLog("Captcha enviado mas ainda há challenge");
                        return false;
                    }
                    else
                    {
                        // 403 sem challenge pode ser outro erro
                        AccountManager.AddLog("Join 403 sem challenge - pode ser OK");
                        // Pode ser sucesso parcial - o /continue funcionou
                        return true;
                    }
                }
                else
                {
                    AccountManager.AddLog($"Join retornou {joinResponse.StatusCode}");
                    // Se não é 403, provavelmente funcionou
                    return joinResponse.StatusCode != HttpStatusCode.Forbidden;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERRO] Exceção no continue: {ex.Message}");
                AccountManager.AddLog($"Erro continue: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resolve o Proof of Work e envia o resultado
        /// </summary>
        public async Task<bool> SolveProofOfWorkAsync(CaptchaChallengeResult challengeResult)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                AccountManager.AddLog("🔄 Iniciando Proof of Work...");

                string sessionId = challengeResult.SessionId;
                string genericChallengeId = challengeResult.GenericChallengeId;

                // ETAPA 1: Buscar puzzle
                AccountManager.AddLog("🔍 Buscando parâmetros do puzzle...");

                var powParams = await GetPoWParametersAsync(sessionId, challengeResult.ChallengeId, genericChallengeId);

                if (powParams == null)
                {
                    AccountManager.AddLog("❌ Não foi possível obter parâmetros do PoW");
                    return false;
                }

                string n = powParams.Item1;
                string a = powParams.Item2;
                int iterations = powParams.Item3;

                Debug.WriteLine($"[POW] Parâmetros: N.Length={n.Length}, A={a}, T={iterations}");

                // ETAPA 2: Calcular PoW
                string dllInfo = PoWSolver.IsNativeDllAvailable 
                    ? $"✅ {PoWSolver.NativeDllVersion}" 
                    : "⚠️ DLL não encontrada - usando C# LENTO!";
                
                AccountManager.AddLog($"🧮 Calculando PoW ({iterations:N0} iterações)... {dllInfo}");

                // Aviso se não tiver DLL nativa
                if (!PoWSolver.IsNativeDllAvailable)
                {
                    AccountManager.AddLog("⚠️ DLL não encontrada - cálculo será lento!");
                    AccountManager.AddLog(PoWSolver.GetInitLog());
                }

                // Calcular em thread separada
                string result = null;
                await Task.Run(() =>
                {
                    result = PoWSolver.Solve(n, a, iterations);
                });

                if (string.IsNullOrEmpty(result))
                {
                    AccountManager.AddLog("❌ Solver retornou resultado vazio!");
                    return false;
                }

                Debug.WriteLine($"[POW] Calculado em {stopwatch.Elapsed.TotalSeconds:F1}s");
                Debug.WriteLine($"[POW] Resultado: {result.Substring(0, Math.Min(50, result.Length))}...");

                // ETAPA 3: Enviar solução para obter redemptionToken
                AccountManager.AddLog($"📤 Enviando solução... (calculado em {stopwatch.Elapsed.TotalSeconds:F1}s)");

                string redemptionToken = await SubmitPoWSolutionAsync(sessionId, result);
                
                if (string.IsNullOrEmpty(redemptionToken))
                {
                    AccountManager.AddLog("❌ Falha ao obter redemptionToken");
                    return false;
                }

                // ETAPA 4: Enviar para /continue com redemptionToken
                AccountManager.AddLog("📤 Finalizando challenge...");

                bool success = await SendPoWContinueRequestAsync(challengeResult, sessionId, redemptionToken);

                if (success)
                {
                    AccountManager.AddLog($"✅ PoW OK! Tempo total: {stopwatch.Elapsed.TotalSeconds:F1}s");
                }
                else
                {
                    AccountManager.AddLog($"❌ PoW falhou ao enviar");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[POW] Erro: {ex.Message}");
                AccountManager.AddLog($"❌ Erro PoW: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tenta obter os parâmetros do PoW (n, a, iterations) via API
        /// </summary>
        private async Task<Tuple<string, string, int>> GetPoWParametersAsync(string sessionId, string challengeId, string genericChallengeId)
        {
            try
            {
                Debug.WriteLine($"[POW] Buscando parâmetros para sessionId: {sessionId}");

                // Obter CSRF token
                if (string.IsNullOrEmpty(_csrfToken))
                {
                    _csrfToken = await GetCsrfTokenAsync();
                }

                // Endpoint correto: GET /proof-of-work-service/v1/pow-puzzle?sessionID={sessionId}
                var client = new RestClient("https://apis.roblox.com");
                var request = new RestRequest($"/proof-of-work-service/v1/pow-puzzle", Method.Get);
                request.AddQueryParameter("sessionID", sessionId);
                request.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");
                request.AddHeader("x-csrf-token", _csrfToken);
                
                var response = await client.ExecuteAsync(request);
                Debug.WriteLine($"[POW] GET pow-puzzle: {response.StatusCode} - {response.Content}");

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    return ParsePoWParameters(response.Content);
                }

                AccountManager.AddLog($"Erro GET puzzle: {response.StatusCode}");

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[POW] Erro ao buscar parâmetros: {ex.Message}");
                AccountManager.AddLog($"Erro parâmetros: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parseia os parâmetros do PoW da resposta JSON
        /// Formato: {"puzzleType":"1","artifacts":"{\"N\":\"...\",\"A\":123,\"T\":10000000}"}
        /// </summary>
        private Tuple<string, string, int> ParsePoWParameters(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                
                // O artifacts é uma string JSON que precisa ser parseada novamente
                string artifactsJson = obj["artifacts"]?.ToString();
                
                if (string.IsNullOrEmpty(artifactsJson))
                {
                    Debug.WriteLine("[POW] artifacts vazio ou null");
                    return null;
                }

                Debug.WriteLine($"[POW] Artifacts JSON: {artifactsJson}");

                var artifacts = JObject.Parse(artifactsJson);
                
                // Formato: N = módulo, A = base, T = iterations
                string n = artifacts["N"]?.ToString();
                string a = artifacts["A"]?.ToString();
                int iterations = artifacts["T"]?.Value<int>() ?? 0;

                Debug.WriteLine($"[POW] N length: {n?.Length}, A: {a}, T: {iterations}");

                if (!string.IsNullOrEmpty(n) && !string.IsNullOrEmpty(a) && iterations > 0)
                {
                    return Tuple.Create(n, a, iterations);
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[POW] Erro ao parsear: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Envia a solução do PoW e obtém o redemptionToken
        /// </summary>
        private async Task<string> SubmitPoWSolutionAsync(string sessionId, string solution)
        {
            try
            {
                Debug.WriteLine("[POW] Enviando solução para /pow-puzzle...");

                // Obter CSRF token
                _csrfToken = await GetCsrfTokenAsync();

                var client = new RestClient("https://apis.roblox.com");
                
                // Primeira tentativa
                var response = await DoSubmitPoWRequest(client, sessionId, solution);
                
                // Se 403, o DoSubmitPoWRequest já atualizou o _csrfToken, tentar novamente
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    Debug.WriteLine("[POW] 403 no POST /pow-puzzle, tentando novamente com novo CSRF...");
                    response = await DoSubmitPoWRequest(client, sessionId, solution);
                }

                Debug.WriteLine($"[POW] POST /pow-puzzle Status: {response.StatusCode}");
                Debug.WriteLine($"[POW] POST /pow-puzzle Response: {response.Content}");

                // Log de debug
                AccountManager.AddLog($"POST /pow-puzzle: {response.StatusCode}");

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var result = JObject.Parse(response.Content);
                    bool answerCorrect = result["answerCorrect"]?.Value<bool>() ?? false;
                    string redemptionToken = result["redemptionToken"]?.ToString();

                    if (answerCorrect && !string.IsNullOrEmpty(redemptionToken))
                    {
                        AccountManager.AddLog($"Solução aceita! Token: {redemptionToken.Substring(0, 8)}...");
                        return redemptionToken;
                    }
                    else
                    {
                        AccountManager.AddLog($"Solução rejeitada: {response.Content?.Substring(0, Math.Min(50, response.Content?.Length ?? 0))}");
                    }
                }
                else
                {
                    AccountManager.AddLog($"Erro POST /pow-puzzle: {response.StatusCode}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[POW] Erro ao enviar solução: {ex.Message}");
                AccountManager.AddLog($"Exceção: {ex.Message}");
                return null;
            }
        }

        private class PoWResponse
        {
            public HttpStatusCode StatusCode { get; set; }
            public string Content { get; set; }
            public bool IsSuccessful { get; set; }
        }

        private async Task<PoWResponse> DoSubmitPoWRequest(RestClient client, string sessionId, string solution)
        {
            // Usar HttpClient diretamente para garantir que é POST
            using (var httpClient = new System.Net.Http.HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Origin", "https://www.roblox.com");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://www.roblox.com/");
                httpClient.DefaultRequestHeaders.Add("x-csrf-token", _csrfToken);
                httpClient.DefaultRequestHeaders.Add("Cookie", $".ROBLOSECURITY={_account.SecurityToken}");

                string jsonBody = $"{{\"sessionID\":\"{sessionId}\",\"solution\":\"{solution}\"}}";
                var content = new System.Net.Http.StringContent(jsonBody, Encoding.UTF8, "application/json");

                Debug.WriteLine($"[POW] POST /pow-puzzle via HttpClient");
                Debug.WriteLine($"[POW] Body: {jsonBody.Substring(0, Math.Min(100, jsonBody.Length))}...");

                try
                {
                    var httpResponse = await httpClient.PostAsync("https://apis.roblox.com/proof-of-work-service/v1/pow-puzzle", content);
                    var responseContent = await httpResponse.Content.ReadAsStringAsync();

                    Debug.WriteLine($"[POW] HttpClient Status: {httpResponse.StatusCode}");
                    Debug.WriteLine($"[POW] HttpClient Response: {responseContent}");

                    // Se 403, pegar novo CSRF token do header
                    if (httpResponse.StatusCode == HttpStatusCode.Forbidden)
                    {
                        if (httpResponse.Headers.TryGetValues("x-csrf-token", out var csrfValues))
                        {
                            _csrfToken = csrfValues.FirstOrDefault();
                            Debug.WriteLine($"[POW] Novo CSRF do 403: {_csrfToken}");
                        }
                    }

                    return new PoWResponse
                    {
                        StatusCode = httpResponse.StatusCode,
                        Content = responseContent,
                        IsSuccessful = httpResponse.IsSuccessStatusCode
                    };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[POW] HttpClient Exception: {ex.Message}");
                    return new PoWResponse
                    {
                        StatusCode = HttpStatusCode.InternalServerError,
                        Content = ex.Message,
                        IsSuccessful = false
                    };
                }
            }
        }

        /// <summary>
        /// Envia o resultado do PoW para /challenge/v1/continue
        /// </summary>
        private async Task<bool> SendPoWContinueRequestAsync(CaptchaChallengeResult challengeResult, string sessionId, string redemptionToken)
        {
            try
            {
                Debug.WriteLine("[POW] Enviando resultado para /continue...");

                // Obter novo CSRF token
                _csrfToken = await GetCsrfTokenAsync();

                var client = new RestClient("https://apis.roblox.com");
                var request = new RestRequest("/challenge/v1/continue", Method.Post);

                request.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");
                request.AddHeader("x-csrf-token", _csrfToken);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Accept", "application/json");
                request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36");
                request.AddHeader("Referer", "https://www.roblox.com/");
                request.AddHeader("Origin", "https://www.roblox.com");

                // Construir o challengeMetadata para PoW - usar redemptionToken do passo anterior
                var redemptionMetadata = new JObject
                {
                    ["redemptionToken"] = redemptionToken,
                    ["sessionId"] = sessionId
                };

                var payload = new JObject
                {
                    ["challengeId"] = challengeResult.ChallengeId,
                    ["challengeType"] = "proofofwork",
                    ["challengeMetadata"] = JsonConvert.SerializeObject(redemptionMetadata)
                };

                string payloadStr = payload.ToString(Formatting.None);
                request.AddStringBody(payloadStr, ContentType.Json);

                Debug.WriteLine($"[POW] Payload: {payloadStr}");

                var response = await client.ExecuteAsync(request);

                Debug.WriteLine($"[POW] Continue Status: {response.StatusCode}");
                Debug.WriteLine($"[POW] Continue Content: {response.Content}");

                // Se 403, tentar com novo CSRF
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    var newCsrf = response.Headers?.FirstOrDefault(h => h.Name?.ToLower() == "x-csrf-token")?.Value?.ToString();
                    if (!string.IsNullOrEmpty(newCsrf))
                    {
                        _csrfToken = newCsrf;
                        request = new RestRequest("/challenge/v1/continue", Method.Post);
                        request.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");
                        request.AddHeader("x-csrf-token", _csrfToken);
                        request.AddHeader("Content-Type", "application/json");
                        request.AddStringBody(payloadStr, ContentType.Json);
                        response = await client.ExecuteAsync(request);
                        Debug.WriteLine($"[POW] Continue retry Status: {response.StatusCode}");
                    }
                }

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    Debug.WriteLine("[POW] Continue bem sucedido!");
                    AccountManager.AddLog("/continue OK! Retentando join...");
                    
                    // Agora fazer a requisição original novamente com os headers de challenge
                    return await RetryJoinGameWithChallengeHeadersAsync(challengeResult, sessionId, redemptionToken);
                }
                else
                {
                    AccountManager.AddLog($"/continue falhou: {response.StatusCode}");
                    Debug.WriteLine($"[POW] Continue falhou: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[POW] Erro ao enviar continue: {ex.Message}");
                AccountManager.AddLog($"Exceção /continue: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Refaz a requisição join-game com os headers de challenge resolvido
        /// </summary>
        private async Task<bool> RetryJoinGameWithChallengeHeadersAsync(CaptchaChallengeResult challengeResult, string sessionId, string redemptionToken)
        {
            try
            {
                Debug.WriteLine("[POW] Refazendo join-game com headers de challenge...");

                var redemptionMetadata = new JObject
                {
                    ["redemptionToken"] = redemptionToken,
                    ["sessionId"] = sessionId
                };
                
                string challengeMetadataStr = JsonConvert.SerializeObject(redemptionMetadata);
                string challengeMetadataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(challengeMetadataStr));

                var joinClient = new RestClient("https://gamejoin.roblox.com");
                var joinRequest = new RestRequest("/v1/join-game-instance", Method.Post);

                joinRequest.AddCookie(".ROBLOSECURITY", _account.SecurityToken, "/", ".roblox.com");
                joinRequest.AddHeader("x-csrf-token", _csrfToken);
                joinRequest.AddHeader("Content-Type", "application/json");
                joinRequest.AddHeader("Accept", "application/json");
                joinRequest.AddHeader("User-Agent", "Roblox/WinInet");
                joinRequest.AddHeader("Referer", "https://www.roblox.com/");
                
                // Headers especiais de challenge
                joinRequest.AddHeader("Rblx-Challenge-Id", challengeResult.ChallengeId);
                joinRequest.AddHeader("Rblx-Challenge-Metadata", challengeMetadataBase64);
                joinRequest.AddHeader("Rblx-Challenge-Type", "proofofwork");

                var joinBody = new JObject
                {
                    ["placeId"] = 2753915549 // Blox Fruits
                };

                joinRequest.AddJsonBody(joinBody.ToString());

                var joinResponse = await joinClient.ExecuteAsync(joinRequest);

                Debug.WriteLine($"[POW] Join-game Status: {joinResponse.StatusCode}");
                Debug.WriteLine($"[POW] Join-game Content: {joinResponse.Content}");

                // Log do resultado
                AccountManager.AddLog($"Retry join: {joinResponse.StatusCode}");

                if (joinResponse.StatusCode == HttpStatusCode.OK)
                {
                    Debug.WriteLine("[POW] Join-game bem sucedido! PoW resolvido!");
                    AccountManager.AddLog("✅ Join OK! PoW resolvido!");
                    return true;
                }
                else if (joinResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    // Verificar se ainda tem challenge
                    bool stillHasChallenge = false;
                    if (joinResponse.Headers != null)
                    {
                        foreach (var header in joinResponse.Headers)
                        {
                            if (header.Name?.ToLower() == "rblx-challenge-id")
                            {
                                stillHasChallenge = true;
                                Debug.WriteLine($"[POW] Ainda tem challenge: {header.Value}");
                                break;
                            }
                        }
                    }

                    if (!stillHasChallenge)
                    {
                        // 403 sem challenge = provavelmente resolvido
                        Debug.WriteLine("[POW] 403 sem challenge - considerando resolvido");
                        AccountManager.AddLog("✅ 403 sem challenge - PoW OK!");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[POW] Erro ao refazer join-game: {ex.Message}");
                AccountManager.AddLog($"Exceção retry: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Abre o navegador com a extensão para resolver o captcha
        /// Usa monitoramento de arquivo para capturar o token
        /// </summary>
        public async Task<bool> SolveCaptchaAsync(string challengeUrl, string extensionPath = null)
        {
            Form progressForm = null;
            Label statusLabel = null;
            Process process = null;
            string userDataDir = null;
            string tokenFilePath = null;

            try
            {
                // Verificar se a extensão existe
                if (string.IsNullOrEmpty(extensionPath))
                {
                    extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions", "yescaptcha");
                }

                if (!Directory.Exists(extensionPath))
                {
                    extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions", "omocaptcha");
                }

                if (!Directory.Exists(extensionPath))
                {
                    MessageBox.Show(
                        "Extensão anti-captcha não encontrada!\n\n" +
                        "Coloque a pasta 'yescaptcha' ou 'omocaptcha' em:\n" +
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions"),
                        "Erro",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return false;
                }

                // Encontrar Chromium local
                string chromiumPath = FindChromiumPath();
                if (string.IsNullOrEmpty(chromiumPath))
                {
                    MessageBox.Show(
                        "Chromium não encontrado!\n\n" +
                        "Verifique se a pasta '.local-chromium' existe\n" +
                        "ou se o Google Chrome está instalado.",
                        "Erro",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return false;
                }

                // Criar perfil temporário para o Chromium
                userDataDir = Path.Combine(Path.GetTempPath(), "RobloxCaptchaSolver_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(userDataDir);

                // Criar arquivo para receber o token
                tokenFilePath = Path.Combine(userDataDir, "captcha_token.txt");
                File.WriteAllText(tokenFilePath, ""); // Criar vazio

                // Criar extensão auxiliar para capturar o token E injetar o cookie
                string helperExtPath = Path.Combine(userDataDir, "token_capture");
                CreateTokenCaptureExtensionWithCookie(helperExtPath, tokenFilePath, _account.SecurityToken);

                // Usar a URL do challenge que recebemos
                // A página hybrid vai carregar o captcha e a extensão vai resolver
                // Quando resolver, a página chama /continue automaticamente

                // Argumentos do Chromium - carregar AMBAS extensões
                var args = new StringBuilder();
                args.Append($"--load-extension=\"{extensionPath}\",\"{helperExtPath}\" ");
                args.Append($"--user-data-dir=\"{userDataDir}\" ");
                args.Append("--no-first-run ");
                args.Append("--no-default-browser-check ");
                args.Append("--disable-default-apps ");
                args.Append("--disable-background-networking ");
                args.Append("--disable-sync ");
                args.Append("--disable-translate ");
                args.Append("--disable-web-security ");
                args.Append("--allow-file-access-from-files ");
                args.Append($"\"{challengeUrl}\"");

                // Iniciar Chromium
                var startInfo = new ProcessStartInfo
                {
                    FileName = chromiumPath,
                    Arguments = args.ToString(),
                    UseShellExecute = false
                };

                process = Process.Start(startInfo);
                
                // Reset do token capturado
                _capturedToken = null;

                // Criar janela de progresso
                progressForm = new Form
                {
                    Text = "Resolvendo Captcha...",
                    Size = new System.Drawing.Size(450, 180),
                    StartPosition = FormStartPosition.CenterScreen,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ControlBox = false,
                    TopMost = true
                };

                statusLabel = new Label
                {
                    Text = "🔄 Aguardando extensão resolver o captcha...",
                    Location = new System.Drawing.Point(20, 30),
                    Size = new System.Drawing.Size(400, 50),
                    Font = new System.Drawing.Font("Segoe UI", 10F)
                };

                var cancelButton = new Button
                {
                    Text = "Cancelar",
                    Location = new System.Drawing.Point(170, 100),
                    Size = new System.Drawing.Size(100, 30)
                };

                bool cancelled = false;
                cancelButton.Click += (s, e) => { cancelled = true; progressForm.Close(); };

                progressForm.Controls.Add(statusLabel);
                progressForm.Controls.Add(cancelButton);
                progressForm.Show();
                progressForm.Refresh();

                // Aguardar até 180 segundos - verificar clipboard e API
                int maxWaitSeconds = 180;
                int elapsed = 0;
                _capturedToken = null;
                bool captchaSolved = false;

                // Aguardar 5 segundos para página carregar
                await Task.Delay(5000);
                elapsed = 5;

                while (elapsed < maxWaitSeconds && !cancelled && !captchaSolved)
                {
                    if (process != null && process.HasExited)
                    {
                        Debug.WriteLine("Navegador foi fechado pelo usuário");
                        // Verificar se captcha foi resolvido
                        statusLabel.Text = "🔄 Verificando se captcha foi resolvido...";
                        progressForm.Refresh();
                        await Task.Delay(2000);
                        captchaSolved = await VerifyCaptchaSolvedAsync();
                        break;
                    }

                    // Verificar clipboard por sucesso da extensão
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            string clipText = Clipboard.GetText();
                            if (clipText == "CAPTCHA_SOLVED_SUCCESS")
                            {
                                Debug.WriteLine("[CAPTCHA] Sucesso detectado via clipboard!");
                                captchaSolved = true;
                                statusLabel.Text = "✅ Captcha resolvido pela extensão!";
                                progressForm.Refresh();
                                
                                // Limpar clipboard
                                try { Clipboard.Clear(); } catch { }
                                break;
                            }
                        }
                    }
                    catch { }

                    // A cada 15 segundos, verificar via API se captcha foi resolvido
                    if (elapsed % 15 == 0 && elapsed > 0)
                    {
                        statusLabel.Text = $"🔄 Verificando status... ({elapsed}s)";
                        progressForm.Refresh();
                        
                        captchaSolved = await VerifyCaptchaSolvedAsync();
                        if (captchaSolved)
                        {
                            Debug.WriteLine("[CAPTCHA] Resolvido! Detectado via API.");
                            statusLabel.Text = "✅ Captcha resolvido!";
                            progressForm.Refresh();
                            break;
                        }
                    }

                    if (elapsed % 5 == 0)
                    {
                        statusLabel.Text = $"🔄 Aguardando extensão resolver... ({elapsed}s)\n" +
                            "A página vai chamar /continue automaticamente.";
                        progressForm.Refresh();
                    }

                    await Task.Delay(1000);
                    Application.DoEvents();
                    elapsed++;
                }

                // Fechar navegador se ainda estiver aberto
                try { if (process != null && !process.HasExited) process.Kill(); } catch { }

                // Fechar janela de progresso
                progressForm?.Close();
                progressForm?.Dispose();
                progressForm = null;

                if (cancelled)
                {
                    await CleanupTempDir(userDataDir);
                    return false;
                }

                // Verificação final
                if (!captchaSolved)
                {
                    captchaSolved = await VerifyCaptchaSolvedAsync();
                }

                if (captchaSolved)
                {
                    MessageBox.Show(
                        "✅ Captcha resolvido com sucesso!\n\n" +
                        "A conta agora pode entrar em jogos sem captcha.",
                        "Sucesso",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    await CleanupTempDir(userDataDir);
                    return true;
                }
                else
                {
                    MessageBox.Show(
                        "⏱️ Captcha não foi resolvido.\n\n" +
                        "Verifique se:\n" +
                        "• A extensão está configurada corretamente\n" +
                        "• Você tem créditos disponíveis\n" +
                        "• Clicou em 'Play' no jogo para triggar o captcha",
                        "Erro",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }

                await CleanupTempDir(userDataDir);
                return false;
            }
            catch (Exception ex)
            {
                progressForm?.Close();
                progressForm?.Dispose();
                try { if (process != null && !process.HasExited) process.Kill(); } catch { }
                MessageBox.Show($"Erro ao abrir solucionador: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Cria uma extensão auxiliar que captura o token do Arkose e salva em arquivo
        /// </summary>
        private void CreateTokenCaptureExtension(string extensionPath, string tokenFilePath)
        {
            Directory.CreateDirectory(extensionPath);

            // manifest.json
            var manifest = new JObject
            {
                ["manifest_version"] = 3,
                ["name"] = "Token Capture Helper",
                ["version"] = "1.0",
                ["permissions"] = new JArray { "storage", "activeTab" },
                ["host_permissions"] = new JArray { "<all_urls>" },
                ["content_scripts"] = new JArray
                {
                    new JObject
                    {
                        ["matches"] = new JArray { "*://*.roblox.com/*", "*://*.arkoselabs.com/*", "*://arkoselabs.roblox.com/*" },
                        ["js"] = new JArray { "content.js" },
                        ["run_at"] = "document_start",
                        ["all_frames"] = true
                    }
                },
                ["background"] = new JObject
                {
                    ["service_worker"] = "background.js"
                }
            };

            File.WriteAllText(Path.Combine(extensionPath, "manifest.json"), manifest.ToString());

            // content.js - intercepta mensagens postMessage que contém o token
            string contentJs = @"
(function() {
    'use strict';
    
    console.log('[TokenCapture] Content script loaded on:', window.location.href);
    
    // Interceptar postMessage
    const originalPostMessage = window.postMessage;
    window.postMessage = function(message, targetOrigin, transfer) {
        try {
            let msgStr = typeof message === 'string' ? message : JSON.stringify(message);
            if (msgStr && msgStr.includes('|r=') && msgStr.includes('|pk=')) {
                console.log('[TokenCapture] Token found in postMessage!');
                
                // Extrair o token
                let match = msgStr.match(/[a-f0-9]{10,}\.[0-9]+\|r=[^""'<>\s]+/i);
                if (match && match[0].length > 100) {
                    console.log('[TokenCapture] Extracted token length:', match[0].length);
                    chrome.runtime.sendMessage({type: 'TOKEN_CAPTURED', token: match[0]});
                }
            }
        } catch(e) {}
        return originalPostMessage.apply(this, arguments);
    };
    
    // Observar mudanças no DOM
    const observer = new MutationObserver(function(mutations) {
        mutations.forEach(function(mutation) {
            mutation.addedNodes.forEach(function(node) {
                if (node.nodeType === 1) {
                    let text = node.textContent || node.innerText || '';
                    if (text.includes('|r=') && text.includes('|pk=')) {
                        let match = text.match(/[a-f0-9]{10,}\.[0-9]+\|r=[^""'<>\s]+/i);
                        if (match && match[0].length > 100) {
                            console.log('[TokenCapture] Token found in DOM!');
                            chrome.runtime.sendMessage({type: 'TOKEN_CAPTURED', token: match[0]});
                        }
                    }
                }
            });
        });
    });
    
    observer.observe(document.documentElement || document.body || document, {
        childList: true,
        subtree: true,
        characterData: true
    });
    
    // Interceptar XMLHttpRequest
    const originalXHR = window.XMLHttpRequest;
    window.XMLHttpRequest = function() {
        const xhr = new originalXHR();
        const originalSend = xhr.send;
        xhr.send = function(body) {
            xhr.addEventListener('load', function() {
                try {
                    let response = xhr.responseText;
                    if (response && response.includes('|r=')) {
                        let match = response.match(/[a-f0-9]{10,}\.[0-9]+\|r=[^""'<>\s]+/i);
                        if (match && match[0].length > 100) {
                            console.log('[TokenCapture] Token found in XHR response!');
                            chrome.runtime.sendMessage({type: 'TOKEN_CAPTURED', token: match[0]});
                        }
                    }
                } catch(e) {}
            });
            return originalSend.apply(this, arguments);
        };
        return xhr;
    };
    
    // Interceptar fetch
    const originalFetch = window.fetch;
    window.fetch = function() {
        return originalFetch.apply(this, arguments).then(response => {
            const clone = response.clone();
            clone.text().then(text => {
                try {
                    if (text && text.includes('|r=')) {
                        let match = text.match(/[a-f0-9]{10,}\.[0-9]+\|r=[^""'<>\s]+/i);
                        if (match && match[0].length > 100) {
                            console.log('[TokenCapture] Token found in fetch response!');
                            chrome.runtime.sendMessage({type: 'TOKEN_CAPTURED', token: match[0]});
                        }
                    }
                } catch(e) {}
            }).catch(() => {});
            return response;
        });
    };
    
    console.log('[TokenCapture] All interceptors installed');
})();
";

            File.WriteAllText(Path.Combine(extensionPath, "content.js"), contentJs);

            // background.js - recebe o token e salva em storage (depois lemos via arquivo)
            // Como não podemos escrever arquivo diretamente, usamos downloads API
            string tokenFilePathEscaped = tokenFilePath.Replace("\\", "\\\\");
            string backgroundJs = $@"
let capturedToken = null;

chrome.runtime.onMessage.addListener(function(request, sender, sendResponse) {{
    if (request.type === 'TOKEN_CAPTURED' && request.token) {{
        console.log('[TokenCapture BG] Token received, length:', request.token.length);
        capturedToken = request.token;
        
        // Salvar no storage
        chrome.storage.local.set({{captchaToken: request.token}}, function() {{
            console.log('[TokenCapture BG] Token saved to storage');
        }});
        
        // Tentar copiar para clipboard
        navigator.clipboard.writeText(request.token).then(() => {{
            console.log('[TokenCapture BG] Token copied to clipboard');
        }}).catch(() => {{}});
    }}
}});

console.log('[TokenCapture BG] Background script loaded');
";

            File.WriteAllText(Path.Combine(extensionPath, "background.js"), backgroundJs);
        }

        /// <summary>
        /// Cria uma extensão auxiliar que captura o token E injeta o cookie da conta
        /// </summary>
        private void CreateTokenCaptureExtensionWithCookie(string extensionPath, string tokenFilePath, string roblosecurityCookie)
        {
            Directory.CreateDirectory(extensionPath);

            // manifest.json - com permissões de cookies e webRequest
            var manifest = new JObject
            {
                ["manifest_version"] = 3,
                ["name"] = "Token Capture Helper",
                ["version"] = "1.0",
                ["permissions"] = new JArray { "storage", "activeTab", "cookies", "webRequest", "declarativeNetRequest" },
                ["host_permissions"] = new JArray { "<all_urls>", "*://*.roblox.com/*", "*://apis.roblox.com/*" },
                ["content_scripts"] = new JArray
                {
                    new JObject
                    {
                        ["matches"] = new JArray { "*://*.roblox.com/*", "*://*.arkoselabs.com/*" },
                        ["js"] = new JArray { "content.js" },
                        ["run_at"] = "document_start",
                        ["all_frames"] = true
                    }
                },
                ["background"] = new JObject
                {
                    ["service_worker"] = "background.js"
                }
            };

            File.WriteAllText(Path.Combine(extensionPath, "manifest.json"), manifest.ToString());

            // content.js - intercepta tudo e monitora /continue
            string contentJs = @"
(function() {
    'use strict';
    
    console.log('[TokenCapture] Content script loaded on:', window.location.href);
    
    // Extrair challengeId da URL se estivermos na página hybrid
    const urlParams = new URLSearchParams(window.location.search);
    const challengeId = urlParams.get('generic-challenge-id');
    if (challengeId) {
        console.log('[TokenCapture] ChallengeId from URL:', challengeId);
        chrome.runtime.sendMessage({type: 'CHALLENGE_ID', challengeId: challengeId});
    }
    
    // Interceptar postMessage para capturar o token
    const originalPostMessage = window.postMessage;
    window.postMessage = function(message, targetOrigin, transfer) {
        try {
            let msgStr = typeof message === 'string' ? message : JSON.stringify(message);
            if (msgStr && msgStr.includes('|r=') && msgStr.includes('|pk=')) {
                console.log('[TokenCapture] Token found in postMessage!');
                let match = msgStr.match(/[a-f0-9]{10,}\.[0-9]+\|r=[^""'<>\s]+/i);
                if (match && match[0].length > 100) {
                    chrome.runtime.sendMessage({type: 'TOKEN_CAPTURED', token: match[0]});
                }
            }
        } catch(e) {}
        return originalPostMessage.apply(this, arguments);
    };
    
    // Interceptar XMLHttpRequest para detectar /continue
    const originalXHR = window.XMLHttpRequest;
    window.XMLHttpRequest = function() {
        const xhr = new originalXHR();
        const originalOpen = xhr.open;
        const originalSend = xhr.send;
        let requestUrl = '';
        let requestBody = '';
        
        xhr.open = function(method, url) {
            requestUrl = url;
            return originalOpen.apply(this, arguments);
        };
        
        xhr.send = function(body) {
            requestBody = body;
            
            // Se for requisição para /continue, capturar os dados
            if (requestUrl && requestUrl.includes('/challenge/v1/continue')) {
                console.log('[TokenCapture] Detected /continue request!');
                console.log('[TokenCapture] Body:', body);
                
                try {
                    const data = JSON.parse(body);
                    chrome.runtime.sendMessage({
                        type: 'CONTINUE_REQUEST',
                        challengeId: data.challengeId,
                        challengeMetadata: data.challengeMetadata,
                        challengeType: data.challengeType
                    });
                } catch(e) {}
            }
            
            xhr.addEventListener('load', function() {
                try {
                    // Se foi /continue e deu sucesso
                    if (requestUrl && requestUrl.includes('/challenge/v1/continue')) {
                        console.log('[TokenCapture] /continue response:', xhr.status, xhr.responseText);
                        chrome.runtime.sendMessage({
                            type: 'CONTINUE_RESPONSE',
                            status: xhr.status,
                            response: xhr.responseText
                        });
                    }
                    
                    // Capturar tokens em respostas
                    let response = xhr.responseText;
                    if (response && response.includes('|r=')) {
                        let match = response.match(/[a-f0-9]{10,}\.[0-9]+\|r=[^""'<>\s]+/i);
                        if (match && match[0].length > 100) {
                            chrome.runtime.sendMessage({type: 'TOKEN_CAPTURED', token: match[0]});
                        }
                    }
                } catch(e) {}
            });
            return originalSend.apply(this, arguments);
        };
        return xhr;
    };
    
    // Interceptar fetch
    const originalFetch = window.fetch;
    window.fetch = function(url, options) {
        const urlStr = typeof url === 'string' ? url : url.url;
        
        // Se for requisição para /continue
        if (urlStr && urlStr.includes('/challenge/v1/continue')) {
            console.log('[TokenCapture] Detected fetch /continue!');
            if (options && options.body) {
                console.log('[TokenCapture] Fetch body:', options.body);
                try {
                    const data = JSON.parse(options.body);
                    chrome.runtime.sendMessage({
                        type: 'CONTINUE_REQUEST',
                        challengeId: data.challengeId,
                        challengeMetadata: data.challengeMetadata,
                        challengeType: data.challengeType
                    });
                } catch(e) {}
            }
        }
        
        return originalFetch.apply(this, arguments).then(response => {
            const clone = response.clone();
            
            // Se foi /continue
            if (urlStr && urlStr.includes('/challenge/v1/continue')) {
                clone.text().then(text => {
                    console.log('[TokenCapture] Fetch /continue response:', response.status, text);
                    chrome.runtime.sendMessage({
                        type: 'CONTINUE_RESPONSE',
                        status: response.status,
                        response: text
                    });
                }).catch(() => {});
            }
            
            clone.text().then(text => {
                try {
                    if (text && text.includes('|r=')) {
                        let match = text.match(/[a-f0-9]{10,}\.[0-9]+\|r=[^""'<>\s]+/i);
                        if (match && match[0].length > 100) {
                            chrome.runtime.sendMessage({type: 'TOKEN_CAPTURED', token: match[0]});
                        }
                    }
                } catch(e) {}
            }).catch(() => {});
            return response;
        });
    };
    
    // Observar mensagens do iframe do captcha
    window.addEventListener('message', function(event) {
        try {
            let data = event.data;
            let dataStr = typeof data === 'string' ? data : JSON.stringify(data);
            
            // Verificar se é mensagem de sucesso do captcha
            if (dataStr && (dataStr.includes('challengeCompleted') || dataStr.includes('captchaSuccess') || dataStr.includes('passed'))) {
                console.log('[TokenCapture] Captcha success message detected!', dataStr);
                chrome.runtime.sendMessage({type: 'CAPTCHA_SUCCESS', data: dataStr});
            }
            
            // Capturar token
            if (dataStr && dataStr.includes('|r=') && dataStr.includes('|pk=')) {
                let match = dataStr.match(/[a-f0-9]{10,}\.[0-9]+\|r=[^""'<>\s]+/i);
                if (match && match[0].length > 100) {
                    chrome.runtime.sendMessage({type: 'TOKEN_CAPTURED', token: match[0]});
                }
            }
        } catch(e) {}
    }, false);
    
    console.log('[TokenCapture] All interceptors installed');
})();
";

            File.WriteAllText(Path.Combine(extensionPath, "content.js"), contentJs);

            // background.js - injeta o cookie e monitora tudo
            string cookieEscaped = roblosecurityCookie.Replace("\\", "\\\\").Replace("'", "\\'");
            string tokenFilePathEscaped = tokenFilePath.Replace("\\", "\\\\");
            
            string backgroundJs = $@"
let capturedData = {{
    challengeId: null,
    token: null,
    continueSuccess: false
}};

// Injetar cookie da conta
async function injectCookie() {{
    try {{
        await chrome.cookies.set({{
            url: 'https://www.roblox.com',
            name: '.ROBLOSECURITY',
            value: '{cookieEscaped}',
            domain: '.roblox.com',
            path: '/',
            secure: true,
            httpOnly: true,
            sameSite: 'no_restriction'
        }});
        console.log('[TokenCapture BG] Cookie injected');
    }} catch(e) {{
        console.error('[TokenCapture BG] Cookie injection failed:', e);
    }}
}}

// Injetar ao iniciar
injectCookie();
chrome.runtime.onInstalled.addListener(injectCookie);

// Escutar mensagens do content script
chrome.runtime.onMessage.addListener(function(request, sender, sendResponse) {{
    console.log('[TokenCapture BG] Message received:', request.type);
    
    if (request.type === 'CHALLENGE_ID') {{
        capturedData.challengeId = request.challengeId;
        console.log('[TokenCapture BG] ChallengeId saved:', request.challengeId);
    }}
    
    if (request.type === 'TOKEN_CAPTURED') {{
        capturedData.token = request.token;
        console.log('[TokenCapture BG] Token saved, length:', request.token.length);
        
        // Copiar para clipboard
        navigator.clipboard.writeText(request.token).catch(() => {{}});
        
        // Salvar no storage
        chrome.storage.local.set({{
            captchaToken: request.token,
            challengeId: capturedData.challengeId
        }});
    }}
    
    if (request.type === 'CONTINUE_REQUEST') {{
        console.log('[TokenCapture BG] /continue request detected');
        console.log('[TokenCapture BG] ChallengeId:', request.challengeId);
        console.log('[TokenCapture BG] Metadata:', request.challengeMetadata);
    }}
    
    if (request.type === 'CONTINUE_RESPONSE') {{
        console.log('[TokenCapture BG] /continue response:', request.status);
        if (request.status === 200) {{
            capturedData.continueSuccess = true;
            console.log('[TokenCapture BG] SUCCESS! Captcha resolved!');
            
            // Salvar sucesso
            chrome.storage.local.set({{continueSuccess: true}});
            
            // Copiar 'SUCCESS' para clipboard para o programa detectar
            navigator.clipboard.writeText('CAPTCHA_SOLVED_SUCCESS').catch(() => {{}});
        }}
    }}
    
    if (request.type === 'CAPTCHA_SUCCESS') {{
        console.log('[TokenCapture BG] Captcha success detected!');
    }}
}});

console.log('[TokenCapture BG] Background script loaded');
";

            File.WriteAllText(Path.Combine(extensionPath, "background.js"), backgroundJs);
        }

        /// <summary>
        /// Injeta o cookie .ROBLOSECURITY no perfil do Chrome
        /// </summary>
        private async Task InjectCookieAsync(string userDataDir)
        {
            try
            {
                // Criar pasta Default
                string defaultDir = Path.Combine(userDataDir, "Default");
                Directory.CreateDirectory(defaultDir);

                // Criar Preferences com configurações básicas
                var prefs = new JObject
                {
                    ["profile"] = new JObject
                    {
                        ["name"] = "Roblox Captcha Solver"
                    }
                };

                File.WriteAllText(
                    Path.Combine(defaultDir, "Preferences"),
                    prefs.ToString()
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao injetar cookie: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Limpa a pasta temporária
        /// </summary>
        private async Task CleanupTempDir(string userDataDir)
        {
            try
            {
                await Task.Delay(2000); // Aguardar processos liberarem arquivos
                if (Directory.Exists(userDataDir))
                {
                    Directory.Delete(userDataDir, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao limpar temp: {ex.Message}");
            }
        }

        /// <summary>
        /// Encontra o caminho do Chromium local ou Chrome instalado
        /// </summary>
        private string FindChromiumPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 1. Procurar na pasta .local-chromium (padrão do Puppeteer/Playwright)
            string localChromiumDir = Path.Combine(baseDir, ".local-chromium");
            if (Directory.Exists(localChromiumDir))
            {
                // Procurar em subpastas (win64-XXXXXX/chrome-win/chrome.exe)
                foreach (var versionDir in Directory.GetDirectories(localChromiumDir))
                {
                    string[] possiblePaths = new[]
                    {
                        Path.Combine(versionDir, "chrome-win", "chrome.exe"),
                        Path.Combine(versionDir, "chrome-win64", "chrome.exe"),
                        Path.Combine(versionDir, "chrome.exe"),
                    };

                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            Debug.WriteLine($"Chromium encontrado: {path}");
                            return path;
                        }
                    }
                }
            }

            // 2. Procurar Chromium direto na pasta do app
            string[] localPaths = new[]
            {
                Path.Combine(baseDir, "chromium", "chrome.exe"),
                Path.Combine(baseDir, "chrome", "chrome.exe"),
                Path.Combine(baseDir, "chrome.exe"),
            };

            foreach (var path in localPaths)
            {
                if (File.Exists(path))
                {
                    Debug.WriteLine($"Chromium encontrado: {path}");
                    return path;
                }
            }

            // 3. Chrome instalado no sistema (fallback)
            string[] systemPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
            };

            foreach (var path in systemPaths)
            {
                if (File.Exists(path))
                {
                    Debug.WriteLine($"Chrome do sistema encontrado: {path}");
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Fluxo completo: detectar challenge e resolver automaticamente
        /// Tenta primeiro o loading bar captcha (retry), depois FunCaptcha se necessário
        /// </summary>
        public async Task<bool> SolveAsync(long placeId = 2753915549, string extensionPath = null) // Blox Fruits por padrão
        {
            // 1. Fazer requisição para capturar challenge
            var challengeResult = await RequestJoinGameAsync(placeId);

            if (challengeResult.Success)
            {
                MessageBox.Show(
                    "✅ Esta conta não precisa de captcha!",
                    "Informação",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return true;
            }

            if (!challengeResult.NeedsCaptcha)
            {
                MessageBox.Show(
                    $"Erro ao verificar captcha:\n{challengeResult.Message}",
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }

            // 2. Primeiro tentar loading bar captcha (retry automático)
            // Mostrar janela de progresso
            Form progressForm = null;
            Label statusLabel = null;
            bool cancelled = false;

            try
            {
                progressForm = new Form
                {
                    Text = "Resolvendo Captcha...",
                    Size = new System.Drawing.Size(400, 150),
                    StartPosition = FormStartPosition.CenterScreen,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ControlBox = false,
                    TopMost = true
                };

                statusLabel = new Label
                {
                    Text = "🔄 Tentando resolver loading bar captcha...",
                    Location = new System.Drawing.Point(20, 25),
                    Size = new System.Drawing.Size(350, 40),
                    Font = new System.Drawing.Font("Segoe UI", 10F)
                };

                var cancelButton = new Button
                {
                    Text = "Cancelar",
                    Location = new System.Drawing.Point(150, 80),
                    Size = new System.Drawing.Size(100, 30)
                };
                cancelButton.Click += (s, e) => { cancelled = true; progressForm.Close(); };

                progressForm.Controls.Add(statusLabel);
                progressForm.Controls.Add(cancelButton);
                progressForm.Show();
                progressForm.Refresh();

                // Tentar loading bar com retry
                var progress = new Progress<string>(msg =>
                {
                    if (statusLabel != null && !statusLabel.IsDisposed)
                    {
                        statusLabel.Text = $"🔄 {msg}";
                        progressForm?.Refresh();
                    }
                });

                bool loadingBarSuccess = await SolveLoadingBarCaptchaAsync(placeId, progress);

                progressForm?.Close();
                progressForm?.Dispose();
                progressForm = null;

                if (cancelled)
                    return false;

                if (loadingBarSuccess)
                {
                    MessageBox.Show(
                        "✅ Captcha resolvido com sucesso!\n\n" +
                        "A conta agora pode entrar em jogos.",
                        "Sucesso",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no loading bar: {ex.Message}");
                progressForm?.Close();
                progressForm?.Dispose();
            }

            // 3. Se loading bar não funcionou, tentar FunCaptcha com extensão
            var result = MessageBox.Show(
                "Loading bar captcha não funcionou.\n\n" +
                "Deseja tentar com FunCaptcha (extensão)?\n\n" +
                "Isso abrirá o navegador com a extensão anti-captcha.",
                "FunCaptcha",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (result == DialogResult.Yes)
            {
                return await SolveCaptchaAsync(challengeResult.ChallengeUrl, extensionPath);
            }

            return false;
        }

        /// <summary>
        /// Resolve apenas FunCaptcha (pula loading bar)
        /// </summary>
        public async Task<bool> SolveFunCaptchaOnlyAsync(long placeId = 2753915549, string extensionPath = null)
        {
            var challengeResult = await RequestJoinGameAsync(placeId);

            if (challengeResult.Success)
            {
                MessageBox.Show(
                    "✅ Esta conta não precisa de captcha!",
                    "Informação",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return true;
            }

            if (!challengeResult.NeedsCaptcha)
            {
                MessageBox.Show(
                    $"Erro ao verificar captcha:\n{challengeResult.Message}",
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }

            return await SolveCaptchaAsync(challengeResult.ChallengeUrl, extensionPath);
        }

        /// <summary>
        /// Resolve captcha usando Puppeteer (como o concorrente faz)
        /// Injeta JavaScript para capturar o token quando resolvido
        /// Também resolve Proof of Work se detectado
        /// </summary>
        public async Task<bool> SolveCaptchaWithPuppeteerAsync(long placeId = 2753915549, string extensionPath = null)
        {
            Browser browser = null;
            Page page = null;

            try
            {
                // 1. Fazer requisição para capturar challenge
                Debug.WriteLine("[CAPTCHA] Iniciando verificação de challenge...");
                var challengeResult = await RequestJoinGameAsync(placeId);

                Debug.WriteLine($"[CAPTCHA] Success={challengeResult.Success}, NeedsCaptcha={challengeResult.NeedsCaptcha}, IsProofOfWork={challengeResult.IsProofOfWork}");
                Debug.WriteLine($"[CAPTCHA] ChallengeType={challengeResult.ChallengeType}");
                Debug.WriteLine($"[CAPTCHA] Message={challengeResult.Message}");

                if (challengeResult.Success)
                {
                    AccountManager.AddLog("✅ Conta não precisa de captcha!");
                    return true;
                }

                if (!challengeResult.NeedsCaptcha)
                {
                    AccountManager.AddLog($"❌ Erro: {challengeResult.Message}");
                    return false;
                }

                // 2. Verificar se é Proof of Work
                if (challengeResult.IsProofOfWork)
                {
                    AccountManager.AddLog($"🔧 PoW detectado! SessionId: {challengeResult.SessionId?.Substring(0, Math.Min(8, challengeResult.SessionId?.Length ?? 0))}...");
                    
                    bool powResult = await SolveProofOfWorkAsync(challengeResult);
                    
                    if (powResult)
                        AccountManager.AddLog("✅ PoW resolvido com sucesso!");
                    else
                        AccountManager.AddLog("❌ Falha ao resolver PoW");
                    
                    return powResult;
                }

                // Se chegou aqui, é FunCaptcha normal
                AccountManager.AddLog($"🎮 FunCaptcha detectado! Abrindo navegador...");

                Debug.WriteLine($"[PUPPETEER] Challenge ID: {_challengeId}");
                Debug.WriteLine($"[PUPPETEER] Unified Captcha ID: {_unifiedCaptchaId}");
                Debug.WriteLine($"[PUPPETEER] Challenge URL: {challengeResult.ChallengeUrl}");

                // 2. Encontrar extensão anti-captcha
                if (string.IsNullOrEmpty(extensionPath))
                {
                    extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions", "yescaptcha");
                }

                if (!Directory.Exists(extensionPath))
                {
                    extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions", "omocaptcha");
                }

                if (!Directory.Exists(extensionPath))
                {
                    Debug.WriteLine("[PUPPETEER] Extensão anti-captcha não encontrada!");
                    return false;
                }

                // 3. Configurar argumentos do Puppeteer com a extensão
                var args = new List<string>
                {
                    "--disable-web-security",
                    $@"--disable-extensions-except=""{extensionPath}""",
                    $@"--load-extension=""{extensionPath}""",
                    "--no-first-run",
                    "--disable-default-apps"
                };

                args.Add("--window-size=500,500");

                var options = new LaunchOptions
                {
                    Headless = false,
                    DefaultViewport = new ViewPortOptions { Width = 500, Height = 500 },
                    Args = args.ToArray(),
                    IgnoreHTTPSErrors = true
                };

                // 4. Baixar/verificar Chromium
                var fetcher = new BrowserFetcher(Product.Chrome);
                await fetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

                // 5. Lançar navegador com stealth plugin
                browser = (Browser)await new PuppeteerExtra().Use(new StealthPlugin()).LaunchAsync(options);
                page = (Page)(await browser.PagesAsync())[0];

                // 6. Injetar cookie da conta
                await page.SetCookieAsync(new CookieParam
                {
                    Name = ".ROBLOSECURITY",
                    Domain = ".roblox.com",
                    Expires = (DateTime.Now.AddYears(1) - DateTime.MinValue).TotalSeconds,
                    HttpOnly = true,
                    Secure = true,
                    Url = "https://roblox.com",
                    Value = _account.SecurityToken
                });

                // 7. Navegar para a página de challenge
                await page.GoToAsync(challengeResult.ChallengeUrl, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                    Timeout = 60000
                });

                Debug.WriteLine("[PUPPETEER] Página carregada, aguardando captcha ser resolvido...");

                // 8. Aguardar a extensão resolver
                // A página hybrid do Roblox chama /continue automaticamente quando resolvido
                
                bool captchaSolved = false;
                int maxWaitSeconds = 120;
                int elapsed = 0;

                while (elapsed < maxWaitSeconds && !captchaSolved)
                {
                    // Verificar se o navegador ainda está aberto
                    try
                    {
                        var pages = await browser.PagesAsync();
                        if (pages == null || pages.Length == 0)
                        {
                            Debug.WriteLine("[PUPPETEER] Navegador fechado pelo usuário");
                            break;
                        }
                    }
                    catch
                    {
                        Debug.WriteLine("[PUPPETEER] Navegador fechado");
                        break;
                    }

                    // A cada 5 segundos, verificar se o captcha foi resolvido
                    if (elapsed % 5 == 0 && elapsed > 0)
                    {
                        captchaSolved = await VerifyCaptchaSolvedAsync();
                        if (captchaSolved)
                        {
                            Debug.WriteLine("[PUPPETEER] Captcha resolvido! Detectado via API.");
                            
                            // Aguardar 4 segundos antes de fechar
                            await Task.Delay(4000);
                            
                            // Fechar navegador
                            try { await browser.CloseAsync(); } catch { }
                            
                            return true;
                        }
                    }

                    await Task.Delay(1000);
                    elapsed++;
                }

                // Fechar navegador se ainda estiver aberto
                try { await browser.CloseAsync(); } catch { }

                // Verificação final
                if (!captchaSolved)
                {
                    await Task.Delay(2000);
                    captchaSolved = await VerifyCaptchaSolvedAsync();
                }

                if (captchaSolved)
                {
                    Debug.WriteLine("[PUPPETEER] Captcha resolvido com sucesso!");
                    return true;
                }
                else
                {
                    Debug.WriteLine("[PUPPETEER] Captcha não foi resolvido.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PUPPETEER] Erro: {ex.Message}");
                try { if (browser != null) await browser.CloseAsync(); } catch { }
                return false;
            }
        }
    }

    /// <summary>
    /// Resultado da verificação de challenge
    /// </summary>
    public class CaptchaChallengeResult
    {
        public bool Success { get; set; }
        public bool NeedsCaptcha { get; set; }
        public bool IsProofOfWork { get; set; }
        public string ChallengeId { get; set; }
        public string ChallengeType { get; set; }
        public string ChallengeMetadataBase64 { get; set; }
        public string ChallengeUrl { get; set; }
        public string Message { get; set; }
        
        // Dados extraídos do metadata decodificado
        public string ActionType { get; set; }
        public string UnifiedCaptchaId { get; set; }
        public string DataExchangeBlob { get; set; }
        
        // Dados do PoW
        public string SessionId { get; set; }
        public string Artifacts { get; set; }
        public string GenericChallengeId { get; set; }
        
        // JSON completo para debug
        public string MetadataJson { get; set; }
        
        // Todos os headers para debug
        public string AllHeaders { get; set; }
    }
}
