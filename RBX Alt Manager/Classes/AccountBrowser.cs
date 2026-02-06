using CefSharp;
using CefSharp.WinForms;
using Newtonsoft.Json.Linq;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebSocketSharp;
using Yove.Proxy;

namespace RBX_Alt_Manager.Classes
{
    internal class AccountBrowser
    {
        // Para converter caminhos longos em curtos (8.3) - evita problemas com espaços
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

        /// <summary>
        /// Converte um caminho longo para o formato curto 8.3 do Windows
        /// </summary>
        private static string GetShortPath(string longPath)
        {
            try
            {
                if (string.IsNullOrEmpty(longPath) || !longPath.Contains(" "))
                    return longPath;

                StringBuilder shortPath = new StringBuilder(260);
                int result = GetShortPathName(longPath, shortPath, shortPath.Capacity);
                
                if (result > 0)
                    return shortPath.ToString();
            }
            catch { }
            
            return longPath;
        }

        /// <summary>
        /// Copia um diretório recursivamente
        /// </summary>
        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        public static BrowserFetcher Fetcher = new BrowserFetcher(Product.Chrome);


        private static Dictionary<int, Vector2> ScreenGrid;

        private readonly Dictionary<string, string> Images = new Dictionary<string, string>();
        private readonly HashSet<string> Solved = new HashSet<string>();
        private ProxyClient Proxy = null;
        private string Password;
        private Account _linkedAccount;
        private DateTime _lastGameJoinLogTime = DateTime.MinValue;
        private long _lastVisitedPlaceId = 0;

        public Browser browser;
        public Page page;
        public Vector2 Size = new Vector2(880, 740), Position;
        public int Index = -1;

        public AccountBrowser() { }

        public AccountBrowser(Account account, string Url = null, string Script = null, Func<Page, Task> PostNavigation = null)
        {
            _linkedAccount = account;
            _ = LaunchBrowser(Url ?? string.Empty, Script: Script, PostNavigation: PostNavigation, PostPageCreation: () => page.SetCookieAsync(new CookieParam
            {
                Name = ".ROBLOSECURITY",
                Domain = ".roblox.com",
                Expires = (DateTime.Now.AddYears(1) - DateTime.MinValue).TotalSeconds,
                HttpOnly = true,
                Secure = true,
                Url = "https://roblox.com",
                Value = account.SecurityToken
            }));
        }

        public async Task LaunchBrowser(string Url = "https://roblox.com/", Func<Task> PostPageCreation = null, Func<Page, Task> PostNavigation = null, string Script = null, string[] Arguments = null)
        {
            if (string.IsNullOrEmpty(Url)) Url = "https://roblox.com/";

            if (Position != null && Index >= 0 && ScreenGrid != null && ScreenGrid.Count > 0)
                Position = ScreenGrid[Index % (ScreenGrid.Count - 1)];
            else
                Position = new Vector2(Screen.PrimaryScreen.WorkingArea.Width / 2 - (Size.X / 2), Screen.PrimaryScreen.WorkingArea.Height / 2 - (Size.Y / 2));

            List<string> Args = new List<string>(Arguments ?? new string[] { "--disable-web-security" });

            // Carregar extensão anti-captcha selecionada
            string selectedExtension = AccountManager.General.Exists("SelectedAntiCaptcha") ? AccountManager.General.Get("SelectedAntiCaptcha") : "none";
            string ExtensionPath = GetAntiCaptchaExtensionPath(selectedExtension);
            string ConfigPath = Path.Combine(Environment.CurrentDirectory, "BrowserConfig.json");
            BrowserConfig Config = null;

            // Log de debug para extensões
            System.Diagnostics.Debug.WriteLine($"[Browser] Selected Extension: {selectedExtension}");
            System.Diagnostics.Debug.WriteLine($"[Browser] Extension Path: {ExtensionPath}");
            System.Diagnostics.Debug.WriteLine($"[Browser] Extension Exists: {(!string.IsNullOrEmpty(ExtensionPath) && Directory.Exists(ExtensionPath))}");

            // NÃO adicionar extensões aqui para Chromium - será feito depois de verificar se é Chrome
            
            if (File.Exists(ConfigPath))
                File.ReadAllText(ConfigPath).TryParseJson(out Config);

            if (Config?.CustomArguments != null) Args.AddRange(Config.CustomArguments);

            // Não adicionar window-size/position aqui - será feito depois para evitar duplicação
            // if (Arguments == null)
            //     Args.AddRange(new string[] { $"--window-size={...}", $"--window-position={...}" });

            string ProxiesPath = Path.Combine(Environment.CurrentDirectory, "proxies.txt");
            string ProxyString = string.Empty, Username = string.Empty, Password = string.Empty;

            if (AccountManager.General.Get<bool>("UseProxies") && File.Exists(ProxiesPath))
            { // Format: [optional protocol://]ip:port@user:pass / socks5://1.2.3.4:999@user:pass
                Random Rng = new Random();
                int Timeout = AccountManager.General.Exists("ProxyTimeout") ? AccountManager.General.Get<int>("ProxyTimeout") : 3000;
                int Limit = AccountManager.General.Exists("ProxyTestLimit") ? AccountManager.General.Get<int>("ProxyTestLimit") : 10;
                List<string> Proxies = File.ReadAllLines(ProxiesPath).ToList();

                Proxies.OrderBy(x => Rng.Next());

                for (int i = 0; i < Limit; i++)
                {
                    if (i > Proxies.Count - 1) break;

                    ProxyString = Proxies[i];

                    string _Proxy = ProxyString;

                    if (_Proxy.Contains("://"))
                        _Proxy = _Proxy.Substring(_Proxy.IndexOf("://") + 3);

                    Uri ProxyUrl = new Uri($"http://{(_Proxy.Contains("@") ? _Proxy.Substring(0, _Proxy.IndexOf('@')) : _Proxy)}");

                    if (ProxyString.Contains("@") && ProxyString.Substring(ProxyString.IndexOf('@')).Contains(':'))
                    {
                        string Combo = ProxyString.Substring(ProxyString.IndexOf('@') + 1);

                        ProxyString = ProxyString.Substring(0, ProxyString.IndexOf('@'));
                        Username = Combo.Substring(0, Combo.IndexOf(':'));
                        Password = Combo.Substring(Combo.IndexOf(':') + 1);
                    }

                    ProxyType Protocol = ProxyType.Http;

                    if (ProxyString.StartsWith("socks5://"))
                        Protocol = ProxyType.Socks5;
                    else if (ProxyString.StartsWith("socks4://"))
                        Protocol = ProxyType.Socks4;

                    Proxy = new ProxyClient(ProxyUrl.Host, ProxyUrl.Port, Username, Password, Protocol);

                    using (var Handler = new HttpClientHandler() { Proxy = Proxy })
                    using (var Client = new HttpClient(Handler) { Timeout = TimeSpan.FromMilliseconds(Timeout) })
                        try { (await Client.GetAsync("https://auth.roblox.com/")).EnsureSuccessStatusCode(); } catch { ProxyString = string.Empty; }

                    if (!string.IsNullOrEmpty(ProxyString)) break;
                }

                if (!string.IsNullOrEmpty(ProxyString))
                {
                    ProxyString = Proxy?.GetProxy(null).Authority ?? ProxyString;

                    if (ProxyString.StartsWith("http://") || ProxyString.StartsWith("https://"))
                        ProxyString = ProxyString.Substring(ProxyString.IndexOf("://") + 3);

                    Args.Add($"--proxy-server={ProxyString}");
                }
            }

            var Options = new LaunchOptions { Headless = false, DefaultViewport = null, Args = Args.ToArray(), IgnoreHTTPSErrors = true };

            // Configurar extensões
            if (!string.IsNullOrEmpty(ExtensionPath) && Directory.Exists(ExtensionPath))
            {
                var finalArgs = new List<string>(Args);
                
                // Remover argumentos de window-size e window-position que têm aspas problemáticas
                finalArgs.RemoveAll(a => a.Contains("--window-size") || a.Contains("--window-position"));
                finalArgs.Add($"--window-size={(int)Size.X},{(int)Size.Y}");
                finalArgs.Add($"--window-position={(int)Position.X},{(int)Position.Y}");
                
                // Verificar se o caminho tem espaços - copiar para temp se necessário
                string extPathToUse = Path.GetFullPath(ExtensionPath);
                
                if (extPathToUse.Contains(" "))
                {
                    // Copiar extensão para pasta temporária sem espaços
                    string tempExtDir = Path.Combine(Path.GetTempPath(), "RAMExt_" + Path.GetFileName(ExtensionPath));
                    try
                    {
                        if (Directory.Exists(tempExtDir))
                            Directory.Delete(tempExtDir, true);
                        
                        CopyDirectory(ExtensionPath, tempExtDir);
                        extPathToUse = tempExtDir;
                    }
                    catch { /* Se falhar, usa o caminho original */ }
                }
                
                finalArgs.Add($"--load-extension={extPathToUse}");
                finalArgs.Add($"--disable-extensions-except={extPathToUse}");
                
                Options.Args = finalArgs.ToArray();
            }
            else
            {
                // Sem extensões - apenas corrigir argumentos de janela
                var finalArgs = new List<string>(Args);
                finalArgs.RemoveAll(a => a.Contains("--window-size") || a.Contains("--window-position"));
                finalArgs.Add($"--window-size={(int)Size.X},{(int)Size.Y}");
                finalArgs.Add($"--window-position={(int)Position.X},{(int)Position.Y}");
                Options.Args = finalArgs.ToArray();
            }

            await Fetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

            browser = (Browser)await new PuppeteerExtra().Use(new StealthPlugin()).LaunchAsync(Options);
            page = (Page)(await browser.PagesAsync())[0];

            if (Proxy != null) browser.Disconnected += (s, e) => Proxy.Dispose();

            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36");

            if (Config?.PreNavigateActions != null)
                foreach (var action in Config.PreNavigateActions)
                {
                    if (!Uri.IsWellFormedUriString(action.Key, UriKind.Absolute)) continue;

                    await page.GoToAsync(action.Key);
                    await page.EvaluateExpressionAsync(action.Value);
                }

            if (Config?.PreNavigateScripts != null)
                foreach (var script in Config.PreNavigateScripts)
                    await page.EvaluateExpressionAsync(script);

            if (PostPageCreation != null) try { await PostPageCreation(); } catch { }

            try { await page.GoToAsync(Url, new NavigationOptions { Referer = "https://google.com/", Timeout = 300000 }); } catch { }

            if (!string.IsNullOrEmpty(Script)) await page.EvaluateExpressionAsync(Script);

            page.FrameAttached += Page_FrameAttached;

            // Detectar game join via navegador para registrar histórico
            if (_linkedAccount != null)
            {
                page.FrameNavigated += Page_FrameNavigated;
                page.RequestFinished += Page_GameJoinRequested;
            }

            if (PostNavigation != null) try { await PostNavigation(page); } catch { }

            if (Config?.PostNavigateScripts != null)
                foreach (var script in Config.PostNavigateScripts)
                    await page.EvaluateExpressionAsync(script);
        }

        public async Task Login(string Username = "", string Password = "", string[] Arguments = null) => await LaunchBrowser("https://roblox.com/login", Arguments: Arguments, PostNavigation: async (page) => await LoginTask(page, Username, Password));

        public async Task LoginTask(Page page, string Username = "", string Password = "")
        {
            page.RequestFinished += Page_RequestFinished;

            await page.EvaluateExpressionAsync(@"document.body.classList.remove(""light-theme"");document.body.classList.add(""dark-theme"");");

            try
            {
                if (!string.IsNullOrEmpty(Username) && await page.WaitForSelectorAsync("#login-username", new WaitForSelectorOptions() { Timeout = 5000 }) != null)
                    await page.TypeAsync("#login-username", Username);
                if (!string.IsNullOrEmpty(Password) && await page.WaitForSelectorAsync("#login-password", new WaitForSelectorOptions() { Timeout = 5000 }) != null)
                    await page.TypeAsync("#login-password", Password);

                if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password)) await page.ClickAsync("#login-button");
            }
            catch (Exception ex) { Program.Logger.Error($"An exception was caught while trying to automatically log in: {ex}"); }
        }

        private async void Page_FrameAttached(object sender, FrameEventArgs e)
        {
            if (!AccountManager.General.Exists("NopechaKey")) return;

            string APIKey = AccountManager.General.Get<string>("NopechaKey");

            var Start = DateTime.Now;

            while (string.IsNullOrEmpty(e.Frame.Name) && (DateTime.Now - Start).TotalSeconds < 10)
                await Task.Delay(200);

            if ((DateTime.Now - Start).TotalSeconds > 3) return;

            if (e.Frame.Name == "CaptchaFrame" || e.Frame.Name == "CaptchaFrame2" || e.Frame.Name == "game-core-frame")
            {
                using HttpClient client = new HttpClient();
                try // :|
                {
                    while (browser.IsConnected && !page.IsClosed && !e.Frame.Detached)
                    {
                        await Task.Delay(500);

                        if (e.Frame.QuerySelectorAsync("#app") == null && e.Frame.QuerySelectorAsync("#root") == null) continue;

                        try // :|
                        {
                            var VerifyButton = await e.Frame.QuerySelectorAsync("#home_children_button");
                            var RetryButton = await e.Frame.QuerySelectorAsync("#wrong_children_button");

                            if (VerifyButton == null && await e.Frame.XPathAsync("//*[@id=\"root\"]/div/div[1]/button") is IElementHandle[] VB && VB.Length > 0) VerifyButton = VB[0];

                            if (VerifyButton != null) await VerifyButton.ClickAsync();
                            else if (RetryButton != null) await RetryButton.ClickAsync();

                            var CaptchaImage = await e.Frame.QuerySelectorAsync("#game_challengeItem_image");
                            var TaskElement = await e.Frame.XPathAsync("//*[@id=\"game_children_text\"]/h2");

                            if (CaptchaImage == null && await e.Frame.XPathAsync("//*[@id=\"root\"]/div/div[1]/div/div[3]/div/button[1]") is IElementHandle[] CI && CI.Length > 0) CaptchaImage = CI[0];
                            if (TaskElement.Length < 1) TaskElement = await e.Frame.XPathAsync("//*[@id=\"root\"]/div/div[1]/div/div[1]/h2");

                            if (TaskElement.Length < 1) continue;

                            string TaskString = await e.Frame.EvaluateFunctionAsync<string>("e => e.textContent", TaskElement[0]);

                            if (!string.IsNullOrEmpty(TaskString) && CaptchaImage != null)
                            {
                                string Source = await e.Frame.EvaluateFunctionAsync<string>("e => e.src", CaptchaImage);

                                if (string.IsNullOrEmpty(Source) && await e.Frame.EvaluateFunctionAsync<bool>("e => e instanceof HTMLButtonElement", CaptchaImage))
                                    Source = await e.Frame.EvaluateFunctionAsync<string>("x => new Promise(a=>fetch(x.style.backgroundImage.match(/(?!^)\".*?\"/g)[0].replaceAll('\"', '')).then(e=>e.blob()).then(e=>{const t=new FileReader;t.onload=()=>a(t.result),t.readAsDataURL(e)}))", CaptchaImage);

                                if (string.IsNullOrEmpty(Source)) continue;

                                if (Images.ContainsKey(Source) && !Solved.Contains(Source))
                                {
                                    var Response = await client.GetAsync($"https://api.nopecha.com/?key={APIKey}&id={Images[Source]}");

                                    JObject Data = JObject.Parse(await Response.Content.ReadAsStringAsync());

                                    if (Data.ContainsKey("data") && !Data.ContainsKey("error"))
                                    {
                                        int CorrectIndex = Array.IndexOf(Data["data"].ToObject<bool[]>(), true);
                                        var Correct = await e.Frame.XPathAsync($"//*[@id=\"image{CorrectIndex + 1}\"]/a");

                                        if (Correct.Length < 1)
                                            Correct = await e.Frame.XPathAsync($"//*[@id=\"root\"]/div/div[1]/div/div[3]/div/button[{CorrectIndex + 1}]");

                                        if (Correct != null && Correct.Length > 0)
                                        {
                                            try { await e.Frame.EvaluateExpressionAsync($"var x=document.evaluate('//*[@id=\"image{CorrectIndex + 1}\"]/a', document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;x.style.backgroundColor='#00ff00';x.style.opacity='24%'"); } catch { }
                                            try { await e.Frame.EvaluateExpressionAsync($"var x=document.evaluate('//*[@id=\"root\"]/div/div[1]/div/div[3]/div/button[{CorrectIndex + 1}]', document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;x.style.opacity='60%';setTimeout(()=>{{temp1.style.opacity = '100%';}},3000);"); } catch { }

                                            if (AccountManager.General.Get<bool>("NopechaAutoSolve")) await Correct[0].ClickAsync();

                                            Solved.Add(Source);
                                        }

                                        await Task.Delay(1800);
                                    }
                                }
                                else
                                {
                                    var Response = await client.PostAsync("https://api.nopecha.com/", JsonContent.Create(new { key = APIKey, type = "funcaptcha", task = TaskString, image_data = new string[] { Source.Substring(23) } }));
                                    Response.EnsureSuccessStatusCode();

                                    JObject data = JObject.Parse(await Response.Content.ReadAsStringAsync());

                                    if (!data.ContainsKey("error") && data.ContainsKey("data"))
                                    {
                                        Images.Add(Source, data?["data"]?.Value<string>() ?? "");

                                        await Task.Delay(500);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { } // Ignore exceptions
            }
        }

        private async void Page_RequestFinished(object sender, RequestEventArgs e)
        {
            Uri Url = new Uri(e.Request.Url);

            if (e.Request.Response.Status == HttpStatusCode.OK && e.Request.Method == HttpMethod.Post && Url.Host == "auth.roblox.com")
            {
                if ((Url.AbsolutePath == "/v2/login" || Url.AbsolutePath == "/v2/signup") && e.Request.PostData != null && Utilities.TryParseJson((string)e.Request.PostData, out JObject LoginData))
                {
                    if (LoginData?["password"]?.Value<string>() is string password && !string.IsNullOrEmpty(password) && LoginData?["ctype"].Value<string>() is string loginType && loginType.ToLowerInvariant() == "username")
                        Password = password;

                    if ((await page.GetCookiesAsync("https://roblox.com/")).FirstOrDefault(Cookie => Cookie.Name == ".ROBLOSECURITY") is CookieParam Cookie)
                        await AddAccount(Cookie);
                }
                else if (Regex.IsMatch(Url.AbsolutePath, "/users/[0-9]+/two-step-verification/login") && (await page.GetCookiesAsync("https://roblox.com/")).FirstOrDefault(Cookie => Cookie.Name == ".ROBLOSECURITY") is CookieParam Cookie)
                    await AddAccount(Cookie);
            }
        }

        /// <summary>
        /// Rastreia placeId quando o usuário navega para uma página de jogo.
        /// </summary>
        private void Page_FrameNavigated(object sender, FrameEventArgs e)
        {
            try
            {
                if (e.Frame != page?.MainFrame) return;
                string frameUrl = e.Frame.Url ?? "";
                var m = Regex.Match(frameUrl, @"roblox\.com/games/(\d+)");
                if (m.Success && long.TryParse(m.Groups[1].Value, out long pid))
                    _lastVisitedPlaceId = pid;
            }
            catch { }
        }

        /// <summary>
        /// Detecta quando o usuário entra num jogo pelo navegador e registra no histórico.
        /// </summary>
        private void Page_GameJoinRequested(object sender, RequestEventArgs e)
        {
            try
            {
                var url = new Uri(e.Request.Url);
                bool isPost = e.Request.Method == HttpMethod.Post;
                bool isRoblox = url.Host.Contains("roblox.com");

                // Debug: logar todos os POSTs roblox para diagnóstico
                if (isPost && isRoblox && AccountManager.DebugModeAtivo)
                    AccountManager.AddLog($"🔍 [Browser] POST → {url.Host}{url.AbsolutePath}");

                // Detectar game join — vários endpoints possíveis
                bool isGameJoin = isPost && url.Host.Contains("gamejoin.roblox.com");
                bool isAuthTicket = isPost && url.Host == "auth.roblox.com"
                    && url.AbsolutePath.Contains("authentication-ticket");
                bool isGameLaunch = isPost && url.Host.Contains("roblox.com")
                    && (url.AbsolutePath.Contains("game-launch") || url.AbsolutePath.Contains("join-game")
                        || url.AbsolutePath.Contains("PlaceLauncher"));

                if (!isGameJoin && !isAuthTicket && !isGameLaunch) return;
                if (_linkedAccount == null) return;

                // Cooldown de 10s para não logar duplicados
                if ((DateTime.UtcNow - _lastGameJoinLogTime).TotalSeconds < 10) return;

                long placeId = 0;
                string debugSource = "nenhuma";

                // 1. Extrair do body do request (gamejoin tem placeId no JSON body)
                if (e.Request.PostData != null)
                {
                    try
                    {
                        string postDataStr = e.Request.PostData.ToString();
                        AccountManager.AddLog($"🔍 [Browser] PostData: {postDataStr.Substring(0, Math.Min(postDataStr.Length, 300))}");

                        var body = JObject.Parse(postDataStr);
                        placeId = body["placeId"]?.Value<long>() ?? body["PlaceId"]?.Value<long>() ?? 0;
                        if (placeId > 0) debugSource = "PostData body";
                    }
                    catch { }
                }

                // 2. Tentar extrair do Referer header
                if (placeId == 0 && e.Request.Headers != null)
                {
                    string referer = null;
                    e.Request.Headers.TryGetValue("Referer", out referer);
                    if (string.IsNullOrEmpty(referer))
                        e.Request.Headers.TryGetValue("referer", out referer);

                    if (!string.IsNullOrEmpty(referer))
                    {
                        var m = Regex.Match(referer, @"roblox\.com/games/(\d+)");
                        if (m.Success && long.TryParse(m.Groups[1].Value, out placeId) && placeId > 0)
                            debugSource = "Referer header";
                    }
                }

                // 3. Tentar extrair da URL da página atual
                if (placeId == 0)
                {
                    string pageUrl = page?.Url ?? "";
                    var m = Regex.Match(pageUrl, @"roblox\.com/games/(\d+)");
                    if (m.Success && long.TryParse(m.Groups[1].Value, out placeId) && placeId > 0)
                        debugSource = "Page URL";
                }

                // 4. Fallback: usar o último placeId visitado (rastreado por FrameNavigated)
                if (placeId == 0 && _lastVisitedPlaceId > 0)
                {
                    placeId = _lastVisitedPlaceId;
                    debugSource = "FrameNavigated cache";
                }

                // 5. Tentar extrair placeId da URL do request
                if (placeId == 0)
                {
                    var m = Regex.Match(url.ToString(), @"[?&]placeId=(\d+)");
                    if (m.Success && long.TryParse(m.Groups[1].Value, out placeId) && placeId > 0)
                        debugSource = "Request URL query";
                }

                // Se for auth ticket sem placeId, não logar (esperar o gamejoin real)
                if (isAuthTicket && placeId == 0) return;

                _lastGameJoinLogTime = DateTime.UtcNow;

                AccountManager.AddLog($"🎮 [Browser] Game join: {_linkedAccount.Username}, placeId={placeId} (fonte: {debugSource}), trigger: {url.Host}{url.AbsolutePath.Substring(0, Math.Min(url.AbsolutePath.Length, 50))}");

                _ = SupabaseManager.Instance.LogAccountAccessAsync(_linkedAccount.Username, placeId);
            }
            catch (Exception ex)
            {
                AccountManager.AddLog($"❌ [Browser] Erro: {ex.Message}");
            }
        }

        private async Task AddAccount(CookieParam SecurityToken)
        {
            if (Proxy == null)
                AccountManager.AddAccount(SecurityToken.Value, Password);
            else
            {
                await page.WaitForNavigationAsync();
                AccountManager.AddAccount(SecurityToken.Value, Password, await page.EvaluateFunctionAsync<string>("() => { return fetch('/my/account/json').then(x=>x.text()); }"));
            }

            Password = null;

            await browser.DisposeAsync();
        }

        public static void CreateGrid(Vector2 Size)
        {
            ScreenGrid = new Dictionary<int, Vector2>();

            for (int x = 0; x < SystemInformation.VirtualScreen.Width; x += (int)Size.X)
                for (int y = 0; y < SystemInformation.VirtualScreen.Height - (Size.Y / 2); y += (int)Size.Y)
                    ScreenGrid.Add(ScreenGrid.Count, new Vector2(x, y));
        }

        /// <summary>
        /// Retorna o caminho da extensão anti-captcha selecionada
        /// </summary>
        private static string GetAntiCaptchaExtensionPath(string extensionName)
        {
            if (string.IsNullOrEmpty(extensionName) || extensionName == "none")
                return null;

            string extensionsFolder = Path.Combine(Environment.CurrentDirectory, "extensions");
            string extensionPath = Path.Combine(extensionsFolder, extensionName);

            // Também verifica a pasta antiga "extension" para compatibilidade
            if (!Directory.Exists(extensionPath))
            {
                string legacyPath = Path.Combine(Environment.CurrentDirectory, "extension");
                if (Directory.Exists(legacyPath))
                    return legacyPath;
            }

            return Directory.Exists(extensionPath) ? extensionPath : null;
        }

        /// <summary>
        /// Retorna a lista de extensões anti-captcha disponíveis
        /// </summary>
        public static List<string> GetAvailableAntiCaptchaExtensions()
        {
            List<string> extensions = new List<string> { "none" };
            
            string extensionsFolder = Path.Combine(Environment.CurrentDirectory, "extensions");
            
            if (Directory.Exists(extensionsFolder))
            {
                foreach (var dir in Directory.GetDirectories(extensionsFolder))
                {
                    extensions.Add(Path.GetFileName(dir));
                }
            }

            // Verifica pasta legada "extension"
            string legacyPath = Path.Combine(Environment.CurrentDirectory, "extension");
            if (Directory.Exists(legacyPath) && !extensions.Contains("extension"))
            {
                extensions.Add("extension (legado)");
            }

            return extensions;
        }
    }

    internal class CefBrowser : Form
    {
        public static CefBrowser Instance
        {
            get
            {
                BrowserForm ??= new CefBrowser();

                return BrowserForm;
            }
        }
        private static CefBrowser BrowserForm;
        public ChromiumWebBrowser browser { get; private set; }
        public bool BrowserMode = false;
        private string Password;

        private CefBrowser(string Url = "https://roblox.com/")
        {
            if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "x86"))) throw new DirectoryNotFoundException("Unable to locate CefSharp dependency folder");
            if (!File.Exists(Path.Combine(Environment.CurrentDirectory, "x86", "CefSharp.dll"))) throw new FileNotFoundException("Unable to locate CefSharp.dll");

            Size = new System.Drawing.Size(880, 740);

            FormClosing += (s, e) =>
            {
                e.Cancel = true;
                Hide();
                Cef.GetGlobalCookieManager().DeleteCookies();
            };

            AccountManager.SetDarkBar(Handle);

            browser = new ChromiumWebBrowser(Url);
            browser.AddressChanged += OnNavigated;
            browser.FrameLoadEnd += OnPageLoaded;

            Controls.Add(browser);
        }

        public void Login()
        {
            Cef.GetGlobalCookieManager().DeleteCookies();
            
            BrowserMode = false;

            browser.Load("https://roblox.com/login");

            Show();
        }

        public void EnterBrowserMode(Account account, string URL = null)
        {
            Cef.GetGlobalCookieManager().SetCookie("https://roblox.com", new CefSharp.Cookie()
            {
                Name = ".ROBLOSECURITY",
                Domain = ".roblox.com",
                Expires = DateTime.Now.AddYears(1),
                HttpOnly = true,
                Secure = true,
                Value = account.SecurityToken
            });

            BrowserMode = true;

            browser.Load(URL ?? "https://roblox.com/home");

            Show();
        }

        private async void OnNavigated(object sender, AddressChangedEventArgs args)
        {
            Program.Logger.Info($"Browser Navigated to {args.Address}"); // someone has an issue where they land on a home page and nothing happens

            string url = args.Address;

            if (url.Contains("/home") && !BrowserMode)
            {
                var cookieManager = Cef.GetGlobalCookieManager();

                await cookieManager.VisitAllCookiesAsync().ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        List<CefSharp.Cookie> cookies = t.Result;

                        CefSharp.Cookie RSec = cookies.Find(x => x.Name == ".ROBLOSECURITY");

                        if (RSec != null)
                        {
                            AccountManager.AddAccount(RSec.Value, Password);

                            Cef.GetGlobalCookieManager().DeleteCookies();

                            this.InvokeIfRequired(() => Hide());
                        }

                        Password = string.Empty;
                    }
                });
            }
        }

        private void OnPageLoaded(object sender, FrameLoadEndEventArgs args)
        {
            Task.Factory.StartNew(async () =>
            {
                await Task.Delay(200);

                while (Visible && args.Url == browser.Address)
                {
                    JavascriptResponse response = await browser.EvaluateScriptAsync("document.getElementById('login-password').value");

                    if (!response.Success)
                        response = await browser.EvaluateScriptAsync("document.getElementById('signup-password').value");

                    if (response.Success)
                        Password = (string)response.Result;

                    await Task.Delay(50);
                }
            });

            browser.ExecuteScriptAsyncWhenPageLoaded(@"document.body.classList.remove(""light-theme"");document.body.classList.add(""dark-theme"");");
        }
    }

    internal class BrowserConfig
    {
        public Dictionary<string, string> PreNavigateActions;

        public List<string> PreNavigateScripts;
        public List<string> PostNavigateScripts;

        public List<string> CustomArguments;
    }
}