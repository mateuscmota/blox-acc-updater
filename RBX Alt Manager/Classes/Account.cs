using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RBX_Alt_Manager.Classes;
using RBX_Alt_Manager.Forms;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

namespace RBX_Alt_Manager
{
    public class Account : IComparable<Account>
    {
        public bool Valid;
        public string SecurityToken;
        public string Username;
        public DateTime LastUse;
        private string _Alias = "";
        private string _Description = "";
        private string _Password = "";
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string Group { get; set; } = "Default";
        public long UserID;
        public Dictionary<string, string> Fields = new Dictionary<string, string>();
        public DateTime LastAttemptedRefresh;
        [JsonIgnore] public DateTime PinUnlocked;
        [JsonIgnore] public DateTime TokenSet;
        [JsonIgnore] public DateTime LastAppLaunch;
        [JsonIgnore] public string CSRFToken;
        [JsonIgnore] public UserPresence Presence;
        [JsonIgnore] public int HotkeyId;
        [JsonIgnore] public string HotkeyName = "";
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string SavedHotkey { get; set; } = "";

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        public int CompareTo(Account compareTo)
        {
            if (compareTo == null)
                return 1;
            else
                return Group.CompareTo(compareTo.Group);
        }

        public string BrowserTrackerID;

        public string Alias
        {
            get => _Alias;
            set
            {
                if (value == null || value.Length > 50)
                    return;

                _Alias = value;
                AccountManager.SaveAccounts();
            }
        }
        public string Description
        {
            get => _Description;
            set
            {
                if (value == null || value.Length > 5000)
                    return;

                _Description = value;
                AccountManager.SaveAccounts();
            }
        }
        public string Password
        {
            get => _Password;
            set
            {
                if (value == null || value.Length > 5000)
                    return;

                _Password = value;
                AccountManager.SaveAccounts();
            }
        }

        public Account() { }

        public Account(string Cookie, string AccountJSON = null)
        {
            SecurityToken = Cookie;
            
            AccountJSON ??= AccountManager.MainClient.Execute(MakeRequest("my/account/json", Method.Get)).Content;

            if (!string.IsNullOrEmpty(AccountJSON) && Utilities.TryParseJson(AccountJSON, out AccountJson Data))
            {
                Username = Data.Name;
                UserID = Data.UserId;

                Valid = true;

                LastUse = DateTime.Now;

                AccountManager.LastValidAccount = this;
            }
        }

        public RestRequest MakeRequest(string url, Method method = Method.Get) => new RestRequest(url, method).AddCookie(".ROBLOSECURITY", SecurityToken, "/", ".roblox.com");

        public bool GetAuthTicket(out string Ticket)
        {
            Ticket = string.Empty;

            if (!GetCSRFToken(out string Token)) return false;

            // Exactly like RAM 3.7.2: lowercase header, Blox-Fruits referer, empty JSON body
            RestRequest request = MakeRequest("/v1/authentication-ticket/", Method.Post)
                .AddHeader("x-csrf-token", Token)
                .AddHeader("Referer", "https://www.roblox.com/games/2753915549/Blox-Fruits")
                .AddJsonBody(string.Empty);

            RestResponse response = AccountManager.AuthClient.Execute(request);

            Parameter TicketHeader = response.Headers.FirstOrDefault(x => x.Name == "rbx-authentication-ticket");

            if (TicketHeader != null)
            {
                Ticket = (string)TicketHeader.Value;

                return true;
            }

            return false;
        }

        public bool GetCSRFToken(out string Result)
        {
            // Exactly like RAM 3.7.2: Blox-Fruits referer
            RestRequest request = MakeRequest("/v1/authentication-ticket/", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/games/2753915549/Blox-Fruits");

            RestResponse response = AccountManager.AuthClient.Execute(request);

            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                Result = $"[{(int)response.StatusCode} {response.StatusCode}] {response.Content}";
                return false;
            }

            Parameter result = response.Headers.FirstOrDefault(x => x.Name == "x-csrf-token");

            string Token = string.Empty;

            if (result != null)
            {
                Token = (string)result.Value;
                LastUse = DateTime.Now;

                AccountManager.LastValidAccount = this;
                AccountManager.SaveAccounts();
            }

            CSRFToken = Token;
            TokenSet = DateTime.Now;
            Result = Token;

            return !string.IsNullOrEmpty(Result);
        }

        public bool CheckPin(bool Internal = false)
        {
            if (!GetCSRFToken(out _))
            {
                if (!Internal) MessageBox.Show("Invalid Account Session!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return false;
            }

            if (DateTime.Now < PinUnlocked)
                return true;

            RestRequest request = MakeRequest("v1/account/pin/", Method.Get).AddHeader("Referer", "https://www.roblox.com/");

            RestResponse response = AccountManager.AuthClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                JObject pinInfo = JObject.Parse(response.Content);

                if (!pinInfo["isEnabled"].Value<bool>() || (pinInfo["unlockedUntil"].Type != JTokenType.Null && pinInfo["unlockedUntil"].Value<int>() > 0)) return true;
            }

            if (!Internal) MessageBox.Show("Pin required!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }

        public bool UnlockPin(string Pin)
        {
            if (Pin.Length != 4) return false;
            if (CheckPin(true)) return true;

            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("v1/account/pin/unlock", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded")
                .AddParameter("pin", Pin);

            RestResponse response = AccountManager.AuthClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                JObject pinInfo = JObject.Parse(response.Content);

                if (pinInfo["isEnabled"].Value<bool>() && pinInfo["unlockedUntil"].Value<int>() > 0)
                    PinUnlocked = DateTime.Now.AddSeconds(pinInfo["unlockedUntil"].Value<int>());

                if (PinUnlocked > DateTime.Now)
                {
                    MessageBox.Show("Pin unlocked for 5 minutes", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    return true;
                }
            }

            return false;
        }

        public async Task<string> GetEmailJSON()
        {
            RestRequest DataRequest = MakeRequest("v1/email", Method.Get);

            RestResponse response = await AccountManager.AccountClient.ExecuteAsync(DataRequest);

            return response.Content;
        }

        public async Task<JToken> GetMobileInfo()
        {
            RestRequest DataRequest = MakeRequest("mobileapi/userinfo", Method.Get);

            RestResponse response = await AccountManager.MainClient.ExecuteAsync(DataRequest);

            if (response.StatusCode == HttpStatusCode.OK && Utilities.TryParseJson(response.Content, out JToken Data))
                return Data;

            return null;
        }

        public async Task<JToken> GetUserInfo()
        {
            RestRequest DataRequest = MakeRequest($"v1/users/{UserID}", Method.Get);

            RestResponse response = await AccountManager.UsersClient.ExecuteAsync(DataRequest);

            if (response.StatusCode == HttpStatusCode.OK && Utilities.TryParseJson(response.Content, out JToken Data))
                return Data;

            return null;
        }

        public async Task<long> GetRobux() => (await GetMobileInfo())?["RobuxBalance"]?.Value<long>() ?? 0;

        public bool SetFollowPrivacy(int Privacy)
        {
            if (!CheckPin()) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("account/settings/follow-me-privacy", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/my/account")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded");

            switch (Privacy)
            {
                case 0:
                    request.AddParameter("FollowMePrivacy", "All");
                    break;
                case 1:
                    request.AddParameter("FollowMePrivacy", "Followers");
                    break;
                case 2:
                    request.AddParameter("FollowMePrivacy", "Following");
                    break;
                case 3:
                    request.AddParameter("FollowMePrivacy", "Friends");
                    break;
                case 4:
                    request.AddParameter("FollowMePrivacy", "NoOne");
                    break;
            }

            RestResponse response = AccountManager.MainClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK) return true;

            return false;
        }

        public bool ChangePassword(string Current, string New)
        {
            if (!CheckPin()) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("v2/user/passwords/change", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded")
                .AddParameter("currentPassword", Current)
                .AddParameter("newPassword", New);

            RestResponse response = AccountManager.AuthClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                Password = New;

                var SToken = response.Cookies[".ROBLOSECURITY"];

                if (SToken != null)
                {
                    SecurityToken = SToken.Value;
                    AccountManager.SaveAccounts();
                }
                else
                    MessageBox.Show("An error occured while changing passwords, you will need to re-login with your new password!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

                MessageBox.Show("Password changed!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }

            MessageBox.Show("Failed to change password!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }

        public bool ChangeEmail(string Password, string NewEmail)
        {
            if (!CheckPin()) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("v1/email", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded")
                .AddParameter("password", Password)
                .AddParameter("emailAddress", NewEmail);

            RestResponse response = AccountManager.AccountClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                MessageBox.Show("Email changed!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }

            MessageBox.Show("Failed to change email, maybe your password is incorrect!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }

        public bool LogOutOfOtherSessions(bool Internal = false)
        {
            if (!CheckPin(Internal)) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest request = MakeRequest("authentication/signoutfromallsessionsandreauthenticate", Method.Post)
                .AddHeader("Referer", "https://www.roblox.com/")
                .AddHeader("X-CSRF-TOKEN", Token)
                .AddHeader("Content-Type", "application/x-www-form-urlencoded");

            RestResponse response = AccountManager.MainClient.Execute(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                var SToken = response.Cookies[".ROBLOSECURITY"];

                if (SToken != null)
                {
                    SecurityToken = SToken.Value;
                    AccountManager.SaveAccounts(true);
                }
                else if (!Internal)
                    MessageBox.Show("An error occured, you will need to re-login!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

                if (!Internal) MessageBox.Show("Signed out of all other sessions!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }

            if (!Internal) MessageBox.Show("Failed to log out of other sessions!", "Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }

        public bool TogglePlayerBlocked(string Username, ref bool Unblocked)
        {
            if (!CheckPin()) throw new Exception("Pin is Locked!");
            if (!AccountManager.GetUserID(Username, out long BlockeeID, out _)) throw new Exception($"Failed to obtain UserId of {Username}!");

            RestResponse BlockedResponse = GetBlockedList();

            if (!BlockedResponse.IsSuccessful) throw new Exception("Failed to obtain blocked users list!");

            string BlockedUsers = BlockedResponse.Content;

            if (!Regex.IsMatch(BlockedUsers, $"\\b{BlockeeID}\\b"))
                return BlockUserId($"{BlockeeID}").IsSuccessful;

            Unblocked = true;

            return BlockUserId($"{BlockeeID}", Unblock: true).IsSuccessful;
        }

        public RestResponse BlockUserId(string UserID, bool SkipPinCheck = false, HttpListenerContext Context = null, bool Unblock = false)
        {
            if (Context != null) Context.Response.StatusCode = 401;
            if (!SkipPinCheck && !CheckPin(true)) throw new Exception("Pin Locked");
            if (!GetCSRFToken(out string Token)) throw new Exception("Invalid X-CSRF-Token");

            RestRequest blockReq = MakeRequest($"v1/users/{UserID}/{(Unblock ? "unblock" : "block")}", Method.Post).AddHeader("X-CSRF-TOKEN", Token);

            RestResponse blockRes = AccountManager.AccountClient.Execute(blockReq);

            Program.Logger.Info($"Block Response for {UserID} | Unblocking: {Unblock}: [{blockRes.StatusCode}] {blockRes.Content}");

            if (Context != null)
                Context.Response.StatusCode = (int)blockRes.StatusCode;

            return blockRes;
        }

        public RestResponse UnblockUserId(string UserID, bool SkipPinCheck = false, HttpListenerContext Context = null) => BlockUserId(UserID, SkipPinCheck, Context, true);

        public bool UnblockEveryone(out string Response)
        {
            if (!CheckPin(true)) { Response = "Pin is Locked"; return false; }

            RestResponse response = GetBlockedList();

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                Task.Run(async () =>
                {
                    JObject List = JObject.Parse(response.Content);

                    if (List.ContainsKey("blockedUsers"))
                    {
                        foreach (var User in List["blockedUsers"])
                        {
                            if (!UnblockUserId(User["userId"].Value<string>(), true).IsSuccessful)
                            {
                                await Task.Delay(20000);

                                UnblockUserId(User["userId"].Value<string>(), true);

                                if (!CheckPin(true))
                                    break;
                            }
                        }
                    }
                });

                Response = "Unblocking Everyone";

                return true; 
            }

            Response = "Failed to unblock everyone";

            return false;
        }

        public RestResponse GetBlockedList(HttpListenerContext Context = null)
        {
            if (Context != null) Context.Response.StatusCode = 401;

            if (!CheckPin(true)) throw new Exception("Pin is Locked");

            RestRequest request = MakeRequest($"v1/users/get-detailed-blocked-users", Method.Get);

            RestResponse response = AccountManager.AccountClient.Execute(request);

            if (Context != null) Context.Response.StatusCode = (int)response.StatusCode;

            return response;
        }

        public bool ParseAccessCode(RestResponse response, out string Code)
        {
            Code = "";

            Match match = Regex.Match(response.Content, "Roblox.GameLauncher.joinPrivateGame\\(\\d+\\,\\s*'(\\w+\\-\\w+\\-\\w+\\-\\w+\\-\\w+)'");

            if (match.Success && match.Groups.Count == 2)
            {
                Code = match.Groups[1]?.Value ?? string.Empty;

                return true;
            }

            return false;
        }

        public async Task<string> JoinServer(long PlaceID, string JobID = "", bool FollowUser = false, bool JoinVIP = false, bool Internal = false) // oh god i am not refactoring everything to be async im sorry
        {
            if (string.IsNullOrEmpty(BrowserTrackerID))
            {
                Random r = new Random();

                BrowserTrackerID = r.Next(100000, 175000).ToString() + r.Next(100000, 900000).ToString(); // oh god this is ugly
            }

            try { ClientSettingsPatcher.PatchSettings(); } catch (Exception Ex) { Program.Logger.Error($"Failed to patch ClientAppSettings: {Ex}"); }

            // Handle share link format (https://www.roblox.com/share?code=XXX&type=Server)
            // Must resolve this BEFORE attempting to join, to get the actual placeId and instanceId
            string shareCode = string.IsNullOrEmpty(JobID) ? string.Empty : 
                Regex.Match(JobID, @"share\?code=([a-f0-9]+)", RegexOptions.IgnoreCase)?.Groups[1]?.Value;
            
            if (!string.IsNullOrEmpty(shareCode))
            {
                Program.Logger.Info($"Resolving share link: {shareCode}");
                var resolveResult = await ResolveShareLink(shareCode);
                
                if (resolveResult.success)
                {
                    // Update PlaceID and JobID with resolved values
                    PlaceID = resolveResult.placeId;
                    
                    if (!string.IsNullOrEmpty(resolveResult.privateServerLinkCode))
                    {
                        // It's a private server - convert to the old format that JoinServer understands
                        JobID = $"privateServerLinkCode={resolveResult.privateServerLinkCode}";
                        JoinVIP = true;
                        Program.Logger.Info($"Resolved to private server: PlaceID={PlaceID}, LinkCode={resolveResult.privateServerLinkCode}");
                    }
                    else if (!string.IsNullOrEmpty(resolveResult.gameInstanceId))
                    {
                        // It's a regular server with instanceId - use it as JobID (gameId)
                        JobID = resolveResult.gameInstanceId;
                        Program.Logger.Info($"Resolved to server: PlaceID={PlaceID}, InstanceId={JobID}");
                    }
                    else
                    {
                        // Just the place, no specific server
                        JobID = "";
                        Program.Logger.Info($"Resolved to place only: PlaceID={PlaceID}");
                    }
                }
                else
                {
                    Program.Logger.Error($"Failed to resolve share link: {resolveResult.error}");
                    return $"ERROR: Failed to resolve share link: {resolveResult.error}";
                }
            }

            if (!GetCSRFToken(out string Token)) return $"ERROR: Account Session Expired, re-add the account or try again. (Invalid X-CSRF-Token)\n{Token}";

            if (AccountManager.ShuffleJobID && string.IsNullOrEmpty(JobID))
                JobID = await Utilities.GetRandomJobId(PlaceID);

            if (GetAuthTicket(out string Ticket))
            {
                if (AccountManager.General.Get<bool>("AutoCloseLastProcess"))
                {
                    try
                    {
                        foreach(Process proc in Process.GetProcessesByName("RobloxPlayerBeta"))
                        {
                            var TrackerMatch = Regex.Match(proc.GetCommandLine(), @"\-b (\d+)");
                            string TrackerID = TrackerMatch.Success ? TrackerMatch.Groups[1].Value : string.Empty;

                            if (TrackerID == BrowserTrackerID)
                            {
                                try // ignore ObjectDisposedExceptions
                                {
                                    proc.CloseMainWindow();
                                    await Task.Delay(250);
                                    proc.CloseMainWindow(); // Allows Roblox to disconnect from the server so we don't get the "Same account launched" error
                                    await Task.Delay(250);
                                    proc.Kill();
                                }
                                catch { }
                            }
                        }
                    }
                    catch (Exception x) { Program.Logger.Error($"An error occured attempting to close {Username}'s last process(es): {x}"); }
                }

                string LinkCode = string.IsNullOrEmpty(JobID) ? string.Empty : Regex.Match(JobID, "privateServerLinkCode=(.+)")?.Groups[1]?.Value;
                string AccessCode = JobID;

                if (!string.IsNullOrEmpty(LinkCode))
                {
                    RestRequest request = MakeRequest(string.Format("/games/{0}?privateServerLinkCode={1}", PlaceID, LinkCode), Method.Get).AddHeader("X-CSRF-TOKEN", Token).AddHeader("Referer", "https://www.roblox.com/games/4924922222/Brookhaven-RP");

                    RestResponse response = await AccountManager.MainClient.ExecuteAsync(request);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (ParseAccessCode(response, out string Code))
                        {
                            JoinVIP = true;
                            AccessCode = Code;
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.Redirect) // thx wally (p.s. i hate wally)
                    {
                        request = MakeRequest(string.Format("/games/{0}?privateServerLinkCode={1}", PlaceID, LinkCode), Method.Get).AddHeader("X-CSRF-TOKEN", Token).AddHeader("Referer", "https://www.roblox.com/games/4924922222/Brookhaven-RP");

                        RestResponse result = await AccountManager.Web13Client.ExecuteAsync(request);

                        if (result.StatusCode == HttpStatusCode.OK)
                        {
                            if (ParseAccessCode(response, out string Code))
                            {
                                JoinVIP = true;
                                AccessCode = Code;
                            }
                        }
                    }
                }

                if (JoinVIP)
                {
                    var request = MakeRequest("/account/settings/private-server-invite-privacy").AddHeader("X-CSRF-TOKEN", Token).AddHeader("Referer", "https://www.roblox.com/my/account");

                    RestResponse result = await AccountManager.MainClient.ExecuteAsync(request);

                    if (result.IsSuccessful && !result.Content.Contains("\"AllUsers\""))
                    {
                        AccountManager.Instance.InvokeIfRequired(() =>
                        {
                            if (Utilities.YesNoPrompt("Roblox Account Manager", "Account Manager has detected your account's privacy settings do not allow you to join private servers.", "Would you like to change this setting to Everyone now?"))
                            {
                                if (!CheckPin(true)) return;

                                var setRequest = MakeRequest("/account/settings/private-server-invite-privacy", Method.Post);

                                setRequest.AddHeader("X-CSRF-TOKEN", Token);
                                setRequest.AddHeader("Referer", "https://www.roblox.com/my/account");
                                setRequest.AddHeader("Content-Type", "application/x-www-form-urlencoded");

                                setRequest.AddParameter("PrivateServerInvitePrivacy", "AllUsers");

                                AccountManager.MainClient.Execute(setRequest);
                            }
                        });
                    }
                }

                double LaunchTime = Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds * 1000);

                if (AccountManager.UseOldJoin)
                {
                    string RPath = @"C:\Program Files (x86)\Roblox\Versions\" + AccountManager.CurrentVersion;

                    if (!Directory.Exists(RPath))
                        RPath = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), @"Roblox\Versions\" + AccountManager.CurrentVersion);

                    if (!Directory.Exists(RPath))
                        return "ERROR: Failed to find ROBLOX executable";

                    RPath += @"\RobloxPlayerBeta.exe";

                    AccountManager.Instance.NextAccount();

                    await Task.Run(() =>
                    {
                        ProcessStartInfo Roblox = new ProcessStartInfo(RPath);
                        
                        if (JoinVIP)
                            Roblox.Arguments = string.Format("--app -t {0} -j \"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId={1}&accessCode={2}&linkCode={3}\"", Ticket, PlaceID, AccessCode, LinkCode);
                        else if (FollowUser)
                            Roblox.Arguments = string.Format("--app -t {0} -j \"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser&userId={1}\"", Ticket, PlaceID);
                        else
                            Roblox.Arguments = string.Format("--app -t {0} -j \"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame{3}&placeId={1}{2}&isPlayTogetherGame=false\"", Ticket, PlaceID, "&gameId=" + JobID, string.IsNullOrEmpty(JobID) ? "" : "Job");
                    });

                    _ = Task.Run(AdjustWindowPosition);

                    return "Success";
                }
                else
                {
                    await Task.Run(() => // prevents roblox launcher hanging our main process
                    {
                        try
                        {
                            ProcessStartInfo LaunchInfo = new ProcessStartInfo();

                            if (JoinVIP)
                                LaunchInfo.FileName = $"roblox-player:1+launchmode:play+gameinfo:{Ticket}+launchtime:{LaunchTime}+placelauncherurl:{HttpUtility.UrlEncode($"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId={PlaceID}&accessCode={AccessCode}&linkCode={LinkCode}")}+browsertrackerid:{BrowserTrackerID}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
                            else if (FollowUser)
                                LaunchInfo.FileName = $"roblox-player:1+launchmode:play+gameinfo:{Ticket}+launchtime:{LaunchTime}+placelauncherurl:{HttpUtility.UrlEncode($"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser&userId={PlaceID}")}+browsertrackerid:{BrowserTrackerID}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
                            else
                                LaunchInfo.FileName = $"roblox-player:1+launchmode:play+gameinfo:{Ticket}+launchtime:{LaunchTime}+placelauncherurl:{HttpUtility.UrlEncode($"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame{(string.IsNullOrEmpty(JobID) ? "" : "Job")}&browserTrackerId={BrowserTrackerID}&placeId={PlaceID}{(string.IsNullOrEmpty(JobID) ? "" : ("&gameId=" + JobID))}&isPlayTogetherGame=false{(AccountManager.IsTeleport ? "&isTeleport=true" : "")}")}+browsertrackerid:{BrowserTrackerID}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
                            Process Launcher = Process.Start(LaunchInfo);
                            
                            Launcher.WaitForExit();

                            AccountManager.Instance.NextAccount();

                            _ = Task.Run(AdjustWindowPosition);
                        }
                        catch (Exception x)
                        {
                            Utilities.InvokeIfRequired(AccountManager.Instance, () => MessageBox.Show($"ERROR: Failed to launch Roblox! Try re-installing Roblox.\n\n{x.Message}{x.StackTrace}", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error));
                            AccountManager.Instance.CancelLaunching();
                            AccountManager.Instance.NextAccount();
                        }
                    });

                    return "Success";
                }
            }
            else
            {
                // Fallback: Auth ticket failed, try launching via browser method
                // This works because Open Browser successfully authenticates
                Program.Logger.Info($"Auth ticket failed for {Username}, attempting deep link launch for PlaceID {PlaceID}");
                
                return await LaunchViaDeepLink(PlaceID, JobID, FollowUser, JoinVIP);
            }
        }

        /// <summary>
        /// Launches the game using deep links (roblox://) after writing the account's cookie.
        /// This is the method used by RAM 3.7.2 to fix the authentication ticket issue.
        /// </summary>
        private async Task<string> LaunchViaDeepLink(long PlaceID, string JobID, bool FollowUser, bool JoinVIP)
        {
            try
            {
                // First try the GameJoin API method (like RAM 3.7.2)
                var gameJoinResult = await TryGameJoinApi(PlaceID, JobID, FollowUser);
                if (gameJoinResult.success)
                {
                    Program.Logger.Info($"GameJoin API success for {Username}");
                    AccountManager.Instance.NextAccount();
                    _ = Task.Run(AdjustWindowPosition);
                    return "Success";
                }
                
                Program.Logger.Info($"GameJoin API failed: {gameJoinResult.error}, trying deep link method...");

                // Delete the cookies file to fix 773 error (this is what RAM 3.7.2 does)
                DeleteRobloxCookies();

                // Close previous instances if needed
                if (AccountManager.General.Get<bool>("AutoCloseLastProcess"))
                {
                    try
                    {
                        foreach (Process proc in Process.GetProcessesByName("RobloxPlayerBeta"))
                        {
                            try
                            {
                                proc.CloseMainWindow();
                                await Task.Delay(250);
                                proc.CloseMainWindow();
                                await Task.Delay(250);
                                proc.Kill();
                            }
                            catch { }
                        }
                    }
                    catch (Exception x) { Program.Logger.Error($"An error occurred attempting to close previous process(es): {x}"); }
                }

                // Build the deep link URL
                string deepLinkUrl;
                
                // Check for share link format (new format)
                string shareCode = string.IsNullOrEmpty(JobID) ? string.Empty : 
                    Regex.Match(JobID, @"share\?code=([a-f0-9]+)(?:&|$)", RegexOptions.IgnoreCase)?.Groups[1]?.Value;
                
                // Check for private server link code
                string linkCode = string.IsNullOrEmpty(JobID) ? string.Empty : 
                    Regex.Match(JobID, @"privateServerLinkCode=(.+)")?.Groups[1]?.Value;

                if (!string.IsNullOrEmpty(shareCode))
                {
                    // New share link format - need to resolve it first
                    var resolveResult = await ResolveShareLink(shareCode);
                    if (resolveResult.success)
                    {
                        if (!string.IsNullOrEmpty(resolveResult.privateServerLinkCode))
                        {
                            // It's a private server share link
                            string accessCode = await GetPrivateServerAccessCode(resolveResult.placeId, resolveResult.privateServerLinkCode);
                            if (!string.IsNullOrEmpty(accessCode))
                            {
                                deepLinkUrl = $"roblox://placeId={resolveResult.placeId}^&accessCode={accessCode}^&linkCode={resolveResult.privateServerLinkCode}";
                            }
                            else
                            {
                                deepLinkUrl = $"roblox://placeId={resolveResult.placeId}^&linkCode={resolveResult.privateServerLinkCode}";
                            }
                        }
                        else if (!string.IsNullOrEmpty(resolveResult.gameInstanceId))
                        {
                            // It's a regular server share link with instanceId
                            deepLinkUrl = $"roblox://placeId={resolveResult.placeId}^&gameInstanceId={resolveResult.gameInstanceId}";
                        }
                        else
                        {
                            // Just the place, no specific server
                            deepLinkUrl = $"roblox://placeId={resolveResult.placeId}";
                        }
                    }
                    else
                    {
                        return $"ERROR: Failed to resolve share link: {resolveResult.error}";
                    }
                }
                else if (FollowUser)
                {
                    // Follow user - PlaceID is actually UserID in this case
                    // Format from 3.7.2: /c start roblox://userID={0}
                    deepLinkUrl = $"roblox://userID={PlaceID}";
                }
                else if (!string.IsNullOrEmpty(linkCode))
                {
                    // Private server with link code
                    // Format from 3.7.2: /c start roblox://placeId={0}^&accessCode={1}^&linkCode={2}
                    string accessCode = await GetPrivateServerAccessCode(PlaceID, linkCode);
                    if (!string.IsNullOrEmpty(accessCode))
                    {
                        deepLinkUrl = $"roblox://placeId={PlaceID}^&accessCode={accessCode}^&linkCode={linkCode}";
                    }
                    else
                    {
                        deepLinkUrl = $"roblox://placeId={PlaceID}^&linkCode={linkCode}";
                    }
                }
                else if (!string.IsNullOrEmpty(JobID) && !JobID.Contains("="))
                {
                    // Normal game with specific server (JobID/gameInstanceId)
                    // Format from 3.7.2: /c start roblox://placeId={0}&gameInstanceId={1}
                    deepLinkUrl = $"roblox://placeId={PlaceID}^&gameInstanceId={JobID}";
                }
                else
                {
                    // Normal game launch
                    deepLinkUrl = $"roblox://placeId={PlaceID}";
                }

                Program.Logger.Info($"Launching via deep link: {deepLinkUrl}");

                // Launch using cmd.exe /c start
                await Task.Run(() =>
                {
                    try
                    {
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c start {deepLinkUrl}",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        Process.Start(startInfo);
                        
                        AccountManager.Instance.NextAccount();
                        _ = Task.Run(AdjustWindowPosition);
                    }
                    catch (Exception x)
                    {
                        Utilities.InvokeIfRequired(AccountManager.Instance, () => 
                            MessageBox.Show($"ERROR: Failed to launch Roblox!\n\n{x.Message}", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error));
                        AccountManager.Instance.CancelLaunching();
                        AccountManager.Instance.NextAccount();
                    }
                });

                return "Success";
            }
            catch (Exception ex)
            {
                Program.Logger.Error($"LaunchViaDeepLink failed: {ex.Message}\n{ex.StackTrace}");
                return $"ERROR: Failed to launch via deep link.\n{ex.Message}";
            }
        }

        /// <summary>
        /// Tries to launch the game using the GameJoin API (v1/join-game).
        /// This is the primary method used by RAM 3.7.2.
        /// The API authenticates the session on Roblox servers, then we launch via deep link.
        /// </summary>
        private async Task<(bool success, string error)> TryGameJoinApi(long PlaceID, string JobID, bool FollowUser)
        {
            try
            {
                if (!GetCSRFToken(out string token))
                {
                    return (false, "Failed to get CSRF token");
                }

                // Build request body based on the type of join
                // Format from 3.7.2: { placeId = {0} } or { gameId = {0}, placeId = {1} } or { gameId = {0}, placeId = {1}, isTeleport = {2} }
                object requestBody;
                
                if (!string.IsNullOrEmpty(JobID) && !JobID.Contains("=") && !JobID.Contains("share"))
                {
                    // Join specific server with gameId
                    requestBody = new { 
                        gameId = JobID, 
                        placeId = PlaceID,
                        isTeleport = AccountManager.IsTeleport
                    };
                }
                else
                {
                    // Join any server - just placeId
                    requestBody = new { placeId = PlaceID };
                }

                Program.Logger.Info($"Calling v1/join-game with: placeId={PlaceID}, gameId={JobID}");

                var request = MakeRequest("v1/join-game", Method.Post)
                    .AddHeader("X-CSRF-TOKEN", token)
                    .AddHeader("Content-Type", "application/json")
                    .AddHeader("Referer", "https://www.roblox.com/")
                    .AddHeader("Origin", "https://www.roblox.com")
                    .AddJsonBody(requestBody);

                var response = await AccountManager.GameJoinClient.ExecuteAsync(request);

                Program.Logger.Info($"v1/join-game response: [{response.StatusCode}] {response.Content}");

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Check if we got a valid joinScript (not null)
                    if (!string.IsNullOrEmpty(response.Content))
                    {
                        var json = JObject.Parse(response.Content);
                        
                        // Check if joinScriptUrl is NOT null - this means success
                        var joinScriptUrl = json["joinScriptUrl"];
                        if (joinScriptUrl != null && joinScriptUrl.Type != JTokenType.Null)
                        {
                            Program.Logger.Info("v1/join-game returned valid joinScript - session authenticated!");
                            
                            // The session is now authenticated on Roblox servers
                            // We can launch via deep link and it will connect with this account
                            
                            // Delete cookies file to ensure clean state
                            DeleteRobloxCookies();
                            
                            // Launch via deep link
                            string deepLink;
                            if (!string.IsNullOrEmpty(JobID) && !JobID.Contains("="))
                            {
                                deepLink = $"roblox://placeId={PlaceID}^&gameInstanceId={JobID}";
                            }
                            else
                            {
                                deepLink = $"roblox://placeId={PlaceID}";
                            }
                            
                            Program.Logger.Info($"Launching via deep link: {deepLink}");
                            
                            ProcessStartInfo startInfo = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c start {deepLink}",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            
                            Process.Start(startInfo);
                            
                            return (true, null);
                        }
                        else
                        {
                            // joinScriptUrl is null - check for error message
                            var status = json["status"]?.Value<int>() ?? 0;
                            var message = json["message"]?.Value<string>() ?? "Unknown error";
                            return (false, $"Join failed: status={status}, message={message}");
                        }
                    }
                    
                    return (false, "Empty response from v1/join-game");
                }
                else if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    // 403 might mean we need to retry with new CSRF token
                    return (false, $"Forbidden (403) - CSRF token may be invalid");
                }

                return (false, $"API returned {response.StatusCode}: {response.Content}");
            }
            catch (Exception ex)
            {
                Program.Logger.Error($"TryGameJoinApi exception: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Launches Roblox using the joinScriptUrl from the GameJoin API.
        /// </summary>
        private async Task LaunchWithJoinScript(string joinScriptUrl)
        {
            await Task.Run(() =>
            {
                try
                {
                    double launchTime = Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds * 1000);
                    
                    // Build the roblox-player URL with the joinScript
                    string launchUrl = $"roblox-player:1+launchmode:play+gameinfo:+launchtime:{launchTime}+placelauncherurl:{HttpUtility.UrlEncode(joinScriptUrl)}+browsertrackerid:{BrowserTrackerID}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
                    
                    Program.Logger.Info($"Launching with joinScript URL");
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = launchUrl,
                        UseShellExecute = true
                    };
                    
                    Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    Program.Logger.Error($"Failed to launch with joinScript: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Deletes the RobloxCookies.dat file to fix the 773 authentication error.
        /// This forces Roblox to use the account from the deep link instead of a cached session.
        /// This is the fix used by RAM 3.7.2.
        /// </summary>
        private bool DeleteRobloxCookies()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                
                if (string.IsNullOrEmpty(localAppData))
                {
                    localAppData = Environment.GetEnvironmentVariable("LocalAppData");
                }
                
                if (string.IsNullOrEmpty(localAppData))
                {
                    Program.Logger.Error("Could not find LocalAppData folder");
                    return false;
                }

                string cookieFilePath = Path.Combine(localAppData, "Roblox", "LocalStorage", "RobloxCookies.dat");

                Program.Logger.Info($"Cookie file path: {cookieFilePath}");

                if (File.Exists(cookieFilePath))
                {
                    Program.Logger.Info($"Deleting RobloxCookies.dat to fix 773 error...");
                    File.Delete(cookieFilePath);
                    Program.Logger.Info("RobloxCookies.dat deleted successfully");
                }
                else
                {
                    Program.Logger.Info("RobloxCookies.dat does not exist (already clean)");
                }

                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                Program.Logger.Error($"Access denied deleting RobloxCookies.dat: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Program.Logger.Error($"Failed to delete RobloxCookies.dat: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resolves a share link code to get the placeId and gameInstanceId.
        /// API: apis.roblox.com/sharelinks/v1/resolve-link
        /// Response contains experienceInviteData with placeId and instanceId
        /// </summary>
        private async Task<(bool success, long placeId, string gameInstanceId, string privateServerLinkCode, string error)> ResolveShareLink(string shareCode)
        {
            try
            {
                if (!GetCSRFToken(out string token))
                {
                    return (false, 0, null, null, "Failed to get CSRF token");
                }

                // Format from 3.7.2: { linkId = {0}, linkType = {1} }
                var request = MakeRequest("sharelinks/v1/resolve-link", Method.Post)
                    .AddHeader("x-csrf-token", token)
                    .AddHeader("Content-Type", "application/json")
                    .AddJsonBody(new { linkId = shareCode, linkType = "Server" });

                var response = await AccountManager.ApisClient.ExecuteAsync(request);

                Program.Logger.Info($"ResolveShareLink response: [{response.StatusCode}] {response.Content}");

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var json = JObject.Parse(response.Content);
                    
                    // Check for experienceInviteData (contains placeId and instanceId)
                    var experienceInviteData = json["experienceInviteData"];
                    if (experienceInviteData != null && experienceInviteData.Type == JTokenType.Object)
                    {
                        var expData = (JObject)experienceInviteData;
                        long placeId = expData["placeId"]?.Type == JTokenType.Integer ? expData["placeId"].Value<long>() : 0;
                        string instanceId = expData["instanceId"]?.Type == JTokenType.String ? expData["instanceId"].Value<string>() : null;
                        
                        if (placeId > 0)
                        {
                            Program.Logger.Info($"Resolved share link: placeId={placeId}, instanceId={instanceId}");
                            return (true, placeId, instanceId, null, null);
                        }
                    }
                    
                    // Check for privateServerInviteData (contains linkCode, not privateServerLinkCode!)
                    var privateServerInviteData = json["privateServerInviteData"];
                    if (privateServerInviteData != null && privateServerInviteData.Type == JTokenType.Object)
                    {
                        var pvtData = (JObject)privateServerInviteData;
                        long placeId = pvtData["placeId"]?.Type == JTokenType.Integer ? pvtData["placeId"].Value<long>() : 0;
                        // The field is "linkCode", not "privateServerLinkCode"!
                        string linkCode = pvtData["linkCode"]?.Type == JTokenType.String ? pvtData["linkCode"].Value<string>() : null;
                        
                        if (placeId > 0 && !string.IsNullOrEmpty(linkCode))
                        {
                            Program.Logger.Info($"Resolved share link to private server: placeId={placeId}, linkCode={linkCode}");
                            return (true, placeId, null, linkCode, null);
                        }
                    }
                    
                    // Fallback: try direct fields at root level
                    long directPlaceId = json["placeId"]?.Type == JTokenType.Integer ? json["placeId"].Value<long>() : 0;
                    string directInstanceId = null;
                    
                    if (json["instanceId"]?.Type == JTokenType.String)
                        directInstanceId = json["instanceId"].Value<string>();
                    else if (json["gameInstanceId"]?.Type == JTokenType.String)
                        directInstanceId = json["gameInstanceId"].Value<string>();
                    
                    if (directPlaceId > 0)
                    {
                        Program.Logger.Info($"Resolved share link (direct): placeId={directPlaceId}, instanceId={directInstanceId}");
                        return (true, directPlaceId, directInstanceId, null, null);
                    }
                    
                    // Log all keys for debugging
                    Program.Logger.Info($"ResolveShareLink JSON keys: {string.Join(", ", json.Properties().Select(p => p.Name))}");
                }

                return (false, 0, null, null, $"API returned: {response.StatusCode} - {response.Content}");
            }
            catch (Exception ex)
            {
                Program.Logger.Error($"ResolveShareLink error: {ex.Message}\n{ex.StackTrace}");
                return (false, 0, null, null, ex.Message);
            }
        }

        /// <summary>
        /// Gets the access code for a private server from its link code.
        /// </summary>
        private async Task<string> GetPrivateServerAccessCode(long placeId, string linkCode)
        {
            try
            {
                if (!GetCSRFToken(out string token))
                {
                    return null;
                }

                var request = MakeRequest($"/games/{placeId}?privateServerLinkCode={linkCode}", Method.Get)
                    .AddHeader("X-CSRF-TOKEN", token)
                    .AddHeader("Referer", "https://www.roblox.com/");

                var response = await AccountManager.MainClient.ExecuteAsync(request);

                if (response.IsSuccessful && ParseAccessCode(response, out string accessCode))
                {
                    return accessCode;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async void AdjustWindowPosition()
        {
            if (!RobloxWatcher.RememberWindowPositions)
                return;

            if (!(int.TryParse(GetField("Window_Position_X"), out int PosX) && int.TryParse(GetField("Window_Position_Y"), out int PosY) && int.TryParse(GetField("Window_Width"), out int Width) && int.TryParse(GetField("Window_Height"), out int Height)))
                return;

            bool Found = false;
            DateTime Ends = DateTime.Now.AddSeconds(45);

            while (true)
            {
                await Task.Delay(350);

                foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta").Reverse())
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;

                    string CommandLine = process.GetCommandLine();

                    var TrackerMatch = Regex.Match(CommandLine, @"\-b (\d+)");
                    string TrackerID = TrackerMatch.Success ? TrackerMatch.Groups[1].Value : string.Empty;

                    if (TrackerID != BrowserTrackerID) continue;

                    Found = true;

                    MoveWindow(process.MainWindowHandle, PosX, PosY, Width, Height, true);

                    break;
                }

                if (Found) break;

                if (DateTime.Now > Ends) break;
            }
        }

        public string SetServer(long PlaceID, string JobID, out bool Successful)
        {
            Successful = false;

            if (!GetCSRFToken(out string Token)) return $"ERROR: Account Session Expired, re-add the account or try again. (Invalid X-CSRF-Token)\n{Token}";

            if (string.IsNullOrEmpty(Token))
                return "ERROR: Account Session Expired, re-add the account or try again. (Invalid X-CSRF-Token)";

            RestRequest request = MakeRequest("v1/join-game-instance", Method.Post).AddHeader("Content-Type", "application/json").AddJsonBody(new { gameId = JobID, placeId = PlaceID });

            RestResponse response = AccountManager.GameJoinClient.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                Successful = true;
                return Regex.IsMatch(response.Content, "\"joinScriptUrl\":[%s+]?null") ? response.Content : "Success";
            }
            else
                return $"Failed {response.StatusCode}: {response.Content} {response.ErrorMessage}";
        }

        public bool SendFriendRequest(string Username)
        {
            if (!AccountManager.GetUserID(Username, out long UserId, out _)) return false;
            if (!GetCSRFToken(out string Token)) return false;

            RestRequest friendRequest = MakeRequest($"/v1/users/{UserId}/request-friendship", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddHeader("Content-Type", "application/json");

            RestResponse friendResponse = AccountManager.FriendsClient.Execute(friendRequest);

            return friendResponse.IsSuccessful && friendResponse.StatusCode == HttpStatusCode.OK;
        }

        public void SetDisplayName(string DisplayName)
        {
            if (!GetCSRFToken(out string Token)) return;

            RestRequest dpRequest = MakeRequest($"/v1/users/{UserID}/display-names", Method.Patch).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(new { newDisplayName = DisplayName });

            RestResponse dpResponse = AccountManager.UsersClient.Execute(dpRequest);

            if (dpResponse.StatusCode != HttpStatusCode.OK)
                throw new Exception(JObject.Parse(dpResponse.Content)?["errors"]?[0]?["message"].Value<string>() ?? $"Something went wrong\n{dpResponse.StatusCode}: {dpResponse.Content}");
        }

        public void SetAvatar(string AvatarJSONData)
        {
            if (string.IsNullOrEmpty(AvatarJSONData)) return;
            if (!AvatarJSONData.TryParseJson(out JObject Avatar)) return;
            if (Avatar == null) return;
            if (!GetCSRFToken(out string Token)) return;

            RestRequest request;

            if (Avatar.ContainsKey("playerAvatarType"))
            {
                request = MakeRequest("v1/avatar/set-player-avatar-type", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(new { playerAvatarType = Avatar["playerAvatarType"].Value<string>() });

                AccountManager.AvatarClient.Execute(request);
            }

            JToken ScaleObject = Avatar.ContainsKey("scales") ? Avatar["scales"] : (Avatar.ContainsKey("scale") ? Avatar["scale"] : null);

            if (ScaleObject != null)
            {
                request = MakeRequest("v1/avatar/set-scales", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(ScaleObject.ToString());

                AccountManager.AvatarClient.Execute(request);
            }

            if (Avatar.ContainsKey("bodyColors"))
            {
                request = MakeRequest("v1/avatar/set-body-colors", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(Avatar["bodyColors"].ToString());

                AccountManager.AvatarClient.Execute(request);
            }

            if (Avatar.ContainsKey("assets"))
            {
                request = MakeRequest("v2/avatar/set-wearing-assets", Method.Post).AddHeader("X-CSRF-TOKEN", Token).AddJsonBody($"{{\"assets\":{Avatar["assets"]}}}");

                RestResponse Response = AccountManager.AvatarClient.Execute(request);

                if (Response.IsSuccessful)
                {
                    var ResponseJson = JObject.Parse(Response.Content);

                    if (ResponseJson.ContainsKey("invalidAssetIds"))
                        AccountManager.Instance.InvokeIfRequired(() => new MissingAssets(this, ResponseJson["invalidAssetIds"].Select(asset => asset.Value<long>()).ToArray()).Show());
                }
            }
        }

        public async Task<bool> QuickLogIn(string Code)
        {
            if (string.IsNullOrEmpty(Code) || Code.Length != 6) return false;
            if (!GetCSRFToken(out string Token)) return false;

            using var API = new RestClient("https://apis.roblox.com/");
            var Response = await API.PostAsync(MakeRequest("auth-token-service/v1/login/enterCode").AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(new { code = Code }));

            if (Response.IsSuccessful && Response.Content.TryParseJson(out dynamic Info))
                if (Utilities.YesNoPrompt("Quick Log In", "Please confirm you are logging in with this device", $"Device: {Info?.deviceInfo ?? "Unknown"}\nLocation: {Info?.location ?? "Unknown"}"))
                    return (await API.PostAsync(MakeRequest("auth-token-service/v1/login/validateCode").AddHeader("X-CSRF-TOKEN", Token).AddJsonBody(new { code = Code }))).IsSuccessful;

            return false;
        }

        public string GetField(string Name) => Fields.ContainsKey(Name) ? Fields[Name] : string.Empty;
        public void SetField(string Name, string Value) { Fields[Name] = Value; AccountManager.SaveAccounts(); }
        public void RemoveField(string Name) { Fields.Remove(Name); AccountManager.SaveAccounts(); }
    }

    public class AccountJson
    {
        public long UserId { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string UserEmail { get; set; }
        public bool IsEmailVerified { get; set; }
        public int AgeBracket { get; set; }
        public bool UserAbove13 { get; set; }
    }
}