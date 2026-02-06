using BrightIdeasSoftware;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;
using RBX_Alt_Manager.Classes;
using RBX_Alt_Manager.Controls;
using RBX_Alt_Manager.Forms;
using RBX_Alt_Manager.Properties;
using RestSharp;
using Sodium;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

#pragma warning disable CS0618 // parameter warnings

namespace RBX_Alt_Manager
{
    public partial class AccountManager : Form
    {
        public static AccountManager Instance;
        public static List<Account> AccountsList;
        public static List<Account> SelectedAccounts;
        public static List<Game> RecentGames;
        public static Account SelectedAccount;
        public static Account LastValidAccount; // this is used for the Batch class since getting place details requires authorization, auto updates whenever an account is used
        public static RestClient MainClient;
        public static RestClient AvatarClient;
        public static RestClient FriendsClient;
        public static RestClient UsersClient;
        public static RestClient PresenceClient;
        public static RestClient AuthClient;
        public static RestClient EconClient;
        public static RestClient AccountClient;
        public static RestClient GameJoinClient;
        public static RestClient Web13Client;
        public static RestClient ApisClient;
        public static string CurrentPlaceId { get => Instance.PlaceID.Text; }
        public static string CurrentJobId { get => Instance.JobID.Text; }
        private ArgumentsForm afform;
        private ServerList ServerListForm;
        private AccountUtils UtilsForm;
        private ImportForm ImportAccountsForm;
        private AccountFields FieldsForm;
        private ThemeEditor ThemeForm;
        private AccountControl ControlForm;
        private SettingsForm SettingsForm;
        private RecentGamesForm RGForm;
        private AccountUtils AccountUtilsForm;
        private readonly static DateTime startTime = DateTime.Now;
        public static bool IsTeleport = false;
        public static bool UseOldJoin = false;
        public static bool ShuffleJobID = false;
        public static bool DebugModeAtivo = false;
        private static bool PuppeteerSupported;

        // Painel de Inventário (Supabase)
        private Controls.InventoryPanelControl _inventoryPanel;

        // Contador de requisições da sessão
        private static int _requestCount = 0;
        public static int RequestCount => _requestCount;
        
        // 2FA Hotkey Global
        private static int _twoFAHotkeyId = 0;
        private static int _addFriendHotkeyId = 0;
        private static string _addFriendHotkeyType = ""; // "combo" para Ctrl+Shift+X, "fkey" para F1-F12
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        
        // Hotkey por conta específica
        private static Dictionary<long, int> _accountHotkeyIds = new Dictionary<long, int>();
        private static int _nextAccountHotkeyId = 0x1000;

        // Bring to Front hotkey
        private int _bringToFrontHotkeyId = 0;
        private const int BRING_TO_FRONT_HOTKEY_BASE = 0x200;

        // Roblox Mute Timer
        private System.Windows.Forms.Timer _robloxMuteTimer;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;
        
        public static void IncrementRequestCount(int count = 1)
        {
            _requestCount += count;
        }

        /// <summary>
        /// Adiciona uma mensagem ao log de debug na interface
        /// </summary>
        public static void AddLog(string message)
        {
            AddLog(message, Color.Empty);
        }

        /// <summary>
        /// Adiciona uma mensagem ao log de debug com cor específica
        /// </summary>
        public static void AddLog(string message, Color color)
        {
            if (Instance?.DebugLogTextBox == null) return;
            
            try
            {
                if (Instance.DebugLogTextBox.InvokeRequired)
                {
                    Instance.DebugLogTextBox.Invoke(new Action(() => AddLog(message, color)));
                    return;
                }
                
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string logLine = $"[{timestamp}] {message}\r\n";
                
                // Limitar tamanho do log
                if (Instance.DebugLogTextBox.TextLength > 10000)
                {
                    Instance.DebugLogTextBox.Text = Instance.DebugLogTextBox.Text.Substring(5000);
                }
                
                Instance.DebugLogTextBox.AppendText(logLine);
                Instance.DebugLogTextBox.ScrollToCaret();
            }
            catch { }
        }

        /// <summary>
        /// Log de sucesso (verde)
        /// </summary>
        public static void AddLogSuccess(string message)
        {
            AddLog("✅ " + message);
        }

        /// <summary>
        /// Log de erro (vermelho)
        /// </summary>
        public static void AddLogError(string message)
        {
            AddLog("❌ " + message);
        }

        /// <summary>
        /// Log de aviso (amarelo)
        /// </summary>
        public static void AddLogWarning(string message)
        {
            AddLog("⚠️ " + message);
        }

        /// <summary>
        /// Formata um número com separador de milhares (ponto)
        /// Ex: 900000000 -> 900.000.000
        /// </summary>
        private static string FormatNumberWithThousands(long number)
        {
            return number.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
        }

        public static string CurrentVersion;
        public OLVListItem SelectedAccountItem { get; private set; }
        private WebServer AltManagerWS;
        private string WSPassword { get; set; }
        public System.Timers.Timer AutoCookieRefresh { get; private set; }

        public static IniFile IniSettings;
        public static IniSection General;
        public static IniSection Developer;
        public static IniSection WebServer;
        public static IniSection AccountControl;
        public static IniSection Watcher;
        public static IniSection Prompts;

        private static Mutex rbxMultiMutex;
        private readonly static object saveLock = new object();
        private readonly static object rgSaveLock = new object();
        public event EventHandler<GameArgs> RecentGameAdded;

        private bool IsResettingPassword;
        private bool IsDownloadingChromium;
        private bool LaunchNext;
        private CancellationTokenSource LauncherToken;

        private static readonly byte[] Entropy = new byte[] { 0x52, 0x4f, 0x42, 0x4c, 0x4f, 0x58, 0x20, 0x41, 0x43, 0x43, 0x4f, 0x55, 0x4e, 0x54, 0x20, 0x4d, 0x41, 0x4e, 0x41, 0x47, 0x45, 0x52, 0x20, 0x7c, 0x20, 0x3a, 0x29, 0x20, 0x7c, 0x20, 0x42, 0x52, 0x4f, 0x55, 0x47, 0x48, 0x54, 0x20, 0x54, 0x4f, 0x20, 0x59, 0x4f, 0x55, 0x20, 0x42, 0x55, 0x59, 0x20, 0x69, 0x63, 0x33, 0x77, 0x30, 0x6c, 0x66 };

        [DllImport("DwmApi")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);

        public static void SetDarkBar(IntPtr Handle)
        {
            if (ThemeEditor.UseDarkTopBar && DwmSetWindowAttribute(Handle, 19, new[] { 1 }, 4) != 0)
                DwmSetWindowAttribute(Handle, 20, new[] { 1 }, 4);
        }

        public AccountManager()
        {
            Instance = this;

            ThemeEditor.LoadTheme();

            SetDarkBar(Handle);

            IniSettings = File.Exists(Path.Combine(Environment.CurrentDirectory, "RAMSettings.ini")) ? new IniFile("RAMSettings.ini") : new IniFile();

            General = IniSettings.Section("General");
            Developer = IniSettings.Section("Developer");
            WebServer = IniSettings.Section("WebServer");
            AccountControl = IniSettings.Section("AccountControl");
            Watcher = IniSettings.Section("Watcher");
            Prompts = IniSettings.Section("Prompts");

            if (!General.Exists("CheckForUpdates")) General.Set("CheckForUpdates", "true");
            if (!General.Exists("AccountJoinDelay")) General.Set("AccountJoinDelay", "1");
            if (!General.Exists("AsyncJoin")) General.Set("AsyncJoin", "false");
            if (!General.Exists("DisableAgingAlert")) General.Set("DisableAgingAlert", "false");
            if (!General.Exists("SavePasswords")) General.Set("SavePasswords", "true");
            if (!General.Exists("ServerRegionFormat")) General.Set("ServerRegionFormat", "<city>, <countryCode>", "Visit http://ip-api.com/json/1.1.1.1 to see available format options");
            if (!General.Exists("MaxRecentGames")) General.Set("MaxRecentGames", "20");
            if (!General.Exists("ShuffleChoosesLowestServer")) General.Set("ShuffleChoosesLowestServer", "false");
            if (!General.Exists("ShufflePageCount")) General.Set("ShufflePageCount", "5");
            if (!General.Exists("IPApiLink")) General.Set("IPApiLink", "http://ip-api.com/json/<ip>");
            if (!General.Exists("WindowScale"))
            {
                // Sempre usar escala 1.0, sem popup
                General.Set("WindowScale", "1.0");
            }
            if (!General.Exists("ScaleFonts")) General.Set("ScaleFonts", "true");
            if (!General.Exists("AutoCookieRefresh")) General.Set("AutoCookieRefresh", "true");
            if (!General.Exists("AutoCloseLastProcess")) General.Set("AutoCloseLastProcess", "true");
            if (!General.Exists("ShowPresence")) General.Set("ShowPresence", "true");
            if (!General.Exists("PresenceUpdateRate")) General.Set("PresenceUpdateRate", "5");
            if (!General.Exists("UnlockFPS")) General.Set("UnlockFPS", "false");
            if (!General.Exists("MaxFPSValue")) General.Set("MaxFPSValue", "120");
            // Forçar Chromium (Puppeteer) — CefSharp removido
            General.Set("UseCefSharpBrowser", "false");

            if (!General.Exists("EnableMultiRbx")) General.Set("EnableMultiRbx", "true");
            if (!General.Exists("CloseRobloxOnExit")) General.Set("CloseRobloxOnExit", "true");

            if (!Developer.Exists("DevMode")) Developer.Set("DevMode", "true");
            if (!Developer.Exists("EnableWebServer")) Developer.Set("EnableWebServer", "false");

            if (!WebServer.Exists("WebServerPort")) WebServer.Set("WebServerPort", "7963");
            if (!WebServer.Exists("AllowGetCookie")) WebServer.Set("AllowGetCookie", "true");
            if (!WebServer.Exists("AllowGetAccounts")) WebServer.Set("AllowGetAccounts", "false");
            if (!WebServer.Exists("AllowLaunchAccount")) WebServer.Set("AllowLaunchAccount", "false");
            if (!WebServer.Exists("AllowAccountEditing")) WebServer.Set("AllowAccountEditing", "false");
            if (!WebServer.Exists("Password")) WebServer.Set("Password", ""); else WSPassword = WebServer.Get("Password");
            if (!WebServer.Exists("EveryRequestRequiresPassword")) WebServer.Set("EveryRequestRequiresPassword", "false");
            if (!WebServer.Exists("AllowExternalConnections")) WebServer.Set("AllowExternalConnections", "false");

            if (!AccountControl.Exists("AllowExternalConnections")) AccountControl.Set("AllowExternalConnections", "false");
            if (!AccountControl.Exists("RelaunchDelay")) AccountControl.Set("RelaunchDelay", "60");
            if (!AccountControl.Exists("LauncherDelayNumber")) AccountControl.Set("LauncherDelayNumber", "9");
            if (!AccountControl.Exists("NexusPort")) AccountControl.Set("NexusPort", "5242");

            InitializeComponent();
            this.Rescale();

            AccountsList = new List<Account>();
            SelectedAccounts = new List<Account>();

            AccountsView.SetObjects(AccountsList);

            if (ThemeEditor.UseDarkTopBar) Icon = Properties.Resources.team_KX4_icon_white; // this has to go after or icon wont actually change

            AccountsView.UnfocusedHighlightBackgroundColor = Color.FromArgb(0, 150, 215);
            AccountsView.UnfocusedHighlightForegroundColor = Color.FromArgb(240, 240, 240);

            SimpleDropSink sink = AccountsView.DropSink as SimpleDropSink;
            sink.CanDropBetween = true;
            sink.CanDropOnItem = true;
            sink.CanDropOnBackground = false;
            sink.CanDropOnSubItem = false;
            sink.CanDrop += Sink_CanDrop;
            sink.Dropped += Sink_Dropped;
            sink.FeedbackColor = Color.FromArgb(33, 33, 33);

            AccountsView.AlwaysGroupByColumn = Group;

            Group.GroupKeyGetter = delegate (object account)
            {
                return ((Account)account).Group;
            };

            Group.GroupKeyToTitleConverter = delegate (object Key)
            {
                string GroupName = Key as string;
                Match match = Regex.Match(GroupName, @"\d{1,3}\s?");

                if (match.Success)
                    return GroupName.Substring(match.Length);
                else
                    return GroupName;
            };

            var VCKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X86");

            if (!Prompts.Exists("VCPrompted") && (VCKey == null || (VCKey is RegistryKey && VCKey.GetValue("Bld") is int VCVersion && VCVersion < 32532)))
                Task.Run(async () => // Make sure the user has the latest 2015-2022 vcredist installed
                {
                    using HttpClient Client = new HttpClient();
                    byte[] bs = await Client.GetByteArrayAsync("https://aka.ms/vs/17/release/vc_redist.x86.exe");
                    string FN = Path.Combine(Path.GetTempPath(), "vcredist.tmp");

                    File.WriteAllBytes(FN, bs);

                    Process.Start(new ProcessStartInfo(FN) { UseShellExecute = false, Arguments = "/q /norestart" }).WaitForExit();

                    Prompts.Set("VCPrompted", "1");
                });
        }

        private void Sink_CanDrop(object sender, OlvDropEventArgs e)
        {
            if (e.DataObject.GetType() != typeof(OLVDataObject) && e.DragEventArgs.Data.GetDataPresent(DataFormats.Text))
                e.Effect = DragDropEffects.Copy;
        }

        private void Sink_Dropped(object sender, OlvDropEventArgs e)
        {
            if (e.Effect == DragDropEffects.Copy)
            {
                string Text = (string)e.DragEventArgs.Data.GetData(DataFormats.Text);
                Regex RSecRegex = new Regex(@"(_\|WARNING:-DO-NOT-SHARE-THIS\.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items\.\|\w+)");
                MatchCollection RSecMatches = RSecRegex.Matches(Text);

                foreach (Match match in RSecMatches)
                    AddAccount(match.Value);
            }
        }

        private readonly static string SaveFilePath = Path.Combine(Environment.CurrentDirectory, "AccountData.json");
        private readonly static string RecentGamesFilePath = Path.Combine(Environment.CurrentDirectory, "RecentGames.json"); // i shouldve combined everything that isnt accountdata into one file but oh well im too lazy : |

        private void RefreshView(object obj = null)
        {
            AccountsView.InvokeIfRequired(() =>
            {
                AccountsView.BuildList();
                if (AccountsView.ShowGroups) AccountsView.BuildGroups();

                if (obj != null)
                {
                    AccountsView.RefreshObject(obj);
                    AccountsView.EnsureModelVisible(obj);
                }
            });
        }

        private void SearchAccountsTextBox_TextChanged(object sender, EventArgs e)
        {
            string searchText = SearchAccountsTextBox.Text.Trim().ToLower();
            
            if (string.IsNullOrEmpty(searchText))
            {
                // Show all accounts when search is empty
                AccountsView.SetObjects(AccountsList);
            }
            else
            {
                // Filter accounts by username, alias, description, or group
                var filteredAccounts = AccountsList.Where(account =>
                    (account.Username != null && account.Username.ToLower().Contains(searchText)) ||
                    (account.Alias != null && account.Alias.ToLower().Contains(searchText)) ||
                    (account.Description != null && account.Description.ToLower().Contains(searchText)) ||
                    (account.Group != null && account.Group.ToLower().Contains(searchText))
                ).ToList();
                
                AccountsView.SetObjects(filteredAccounts);
            }
            
            RefreshView();
        }

        #region Private Servers Management

        private void LoadPrivateServers()
        {
            PrivateServerManager.Load();
            RefreshPrivateServersList();
        }

        private void RefreshPrivateServersList()
        {
            PrivateServersListPanel.Controls.Clear();
            
            foreach (var server in PrivateServerManager.Servers)
            {
                AddServerToPanel(server);
            }
        }

        private void AddServerToPanel(Classes.PrivateServer server)
        {
            var serverItem = new Controls.PrivateServerItem(server);
            serverItem.Width = PrivateServersListPanel.Width - 10;
            serverItem.Margin = new Padding(1);
            
            serverItem.JoinClicked += (s, e) => JoinPrivateServer(server);
            serverItem.EditClicked += (s, e) => EditPrivateServer(server);
            serverItem.DeleteClicked += (s, e) => DeletePrivateServer(server);

            PrivateServersListPanel.Controls.Add(serverItem);
        }

        private void JoinPrivateServer(Classes.PrivateServer server)
        {
            if (SelectedAccount == null && (SelectedAccounts == null || SelectedAccounts.Count == 0))
            {
                MessageBox.Show("Selecione uma ou mais contas primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PlaceID.Text = "2753915549"; // Blox Fruits
            JobID.Text = server.Link;
            JoinServer.PerformClick();
        }

        private void EditPrivateServer(Classes.PrivateServer server)
        {
            using (Form editForm = new Form())
            {
                editForm.Text = "Editar Servidor";
                editForm.Size = new Size(350, 150);
                editForm.StartPosition = FormStartPosition.CenterParent;
                editForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                editForm.MaximizeBox = false;
                editForm.MinimizeBox = false;

                Label nameLabel = new Label { Text = "Nome:", Location = new System.Drawing.Point(10, 15), AutoSize = true };
                TextBox nameBox = new TextBox { Text = server.Name, Location = new System.Drawing.Point(60, 12), Width = 260 };

                Label linkLabel = new Label { Text = "Link:", Location = new System.Drawing.Point(10, 45), AutoSize = true };
                TextBox linkBox = new TextBox { Text = server.Link, Location = new System.Drawing.Point(60, 42), Width = 260 };

                Button saveButton = new Button { Text = "Salvar", Location = new System.Drawing.Point(140, 75), DialogResult = DialogResult.OK };

                editForm.Controls.AddRange(new Control[] { nameLabel, nameBox, linkLabel, linkBox, saveButton });
                editForm.AcceptButton = saveButton;

                if (editForm.ShowDialog() == DialogResult.OK)
                {
                    string newName = nameBox.Text.Trim();
                    string newLink = linkBox.Text.Trim();

                    if (!string.IsNullOrEmpty(newName) && !string.IsNullOrEmpty(newLink))
                    {
                        int index = PrivateServerManager.Servers.IndexOf(server);
                        if (index >= 0)
                        {
                            PrivateServerManager.Update(index, newName, newLink);
                            RefreshPrivateServersList();
                        }
                    }
                }
            }
        }

        private void DeletePrivateServer(Classes.PrivateServer server)
        {
            DialogResult result = MessageBox.Show($"Remover servidor '{server.Name}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                PrivateServerManager.Remove(server);
                RefreshPrivateServersList();
            }
        }

        private void AddServerButton_Click(object sender, EventArgs e)
        {
            string name = NewServerNameTextBox.Text.Trim();
            string link = NewServerLinkTextBox.Text.Trim();

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Por favor, digite um nome para o servidor.", "Blox Brasil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(link))
            {
                MessageBox.Show("Por favor, digite o link do servidor.", "Blox Brasil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PrivateServerManager.Add(name, link);
            
            // Clear inputs
            NewServerNameTextBox.Clear();
            NewServerLinkTextBox.Clear();
            
            // Refresh the list
            RefreshPrivateServersList();
        }

        #endregion

        #region Anti-Captcha Extensions

        private void LoadAntiCaptchaExtensions()
        {
            AntiCaptchaComboBox.Items.Clear();
            
            AntiCaptchaComboBox.Items.Add("none");
            
            var extensions = AccountBrowser.GetAvailableAntiCaptchaExtensions();
            foreach (var ext in extensions)
            {
                if (ext != "none")
                    AntiCaptchaComboBox.Items.Add(ext);
            }

            // Selecionar a extensão salva
            string savedExtension = General.Exists("SelectedAntiCaptcha") ? General.Get("SelectedAntiCaptcha") : "none";
            int index = AntiCaptchaComboBox.Items.IndexOf(savedExtension);
            AntiCaptchaComboBox.SelectedIndex = index >= 0 ? index : 0;
        }

        private void AntiCaptchaComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (AntiCaptchaComboBox.SelectedItem != null)
            {
                string selected = AntiCaptchaComboBox.SelectedItem.ToString();
                General.Set("SelectedAntiCaptcha", selected);
            }
        }

        private void TwoFAGenerateButton_Click(object sender, EventArgs e)
        {
            string secret = TwoFASecretTextBox.Text.Trim().Replace(" ", "");
            
            if (string.IsNullOrEmpty(secret))
            {
                MessageBox.Show("Insira o código 2FA Secret!", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string code = GenerateTOTP(secret);
                TwoFACodeTextBox.Text = code;
                
                // Copiar para clipboard
                Clipboard.SetText(code);
                AddLog($"🔐 2FA: {code} (copiado!)");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar código: {ex.Message}\n\nVerifique se o Secret Key está correto.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Gera código TOTP (Time-based One-Time Password) compatível com 2FA
        /// </summary>
        private string GenerateTOTP(string secret)
        {
            // Decodificar Base32
            byte[] key = Base32Decode(secret.ToUpper());
            
            // Tempo atual em intervalos de 30 segundos
            long counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            
            // Converter counter para bytes (big-endian)
            byte[] counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counterBytes);
            
            // HMAC-SHA1
            using (var hmac = new System.Security.Cryptography.HMACSHA1(key))
            {
                byte[] hash = hmac.ComputeHash(counterBytes);
                
                // Dynamic truncation
                int offset = hash[hash.Length - 1] & 0x0F;
                int binary = ((hash[offset] & 0x7F) << 24) |
                            ((hash[offset + 1] & 0xFF) << 16) |
                            ((hash[offset + 2] & 0xFF) << 8) |
                            (hash[offset + 3] & 0xFF);
                
                int otp = binary % 1000000;
                return otp.ToString("D6");
            }
        }

        /// <summary>
        /// Decodifica string Base32 para bytes
        /// </summary>
        private byte[] Base32Decode(string input)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            input = input.TrimEnd('=');
            
            int byteCount = input.Length * 5 / 8;
            byte[] result = new byte[byteCount];
            
            int buffer = 0;
            int bitsRemaining = 0;
            int index = 0;
            
            foreach (char c in input)
            {
                int value = alphabet.IndexOf(c);
                if (value < 0) continue;
                
                buffer = (buffer << 5) | value;
                bitsRemaining += 5;
                
                if (bitsRemaining >= 8)
                {
                    result[index++] = (byte)(buffer >> (bitsRemaining - 8));
                    bitsRemaining -= 8;
                }
            }
            
            return result;
        }

        /// <summary>
        /// Atualiza a hotkey global do 2FA
        /// </summary>
        public static void UpdateTwoFAHotkey(string hotkeyName)
        {
            // Remover hotkey anterior
            if (_twoFAHotkeyId != 0)
            {
                UnregisterHotKey(Instance.Handle, _twoFAHotkeyId);
                _twoFAHotkeyId = 0;
            }

            if (hotkeyName == "Desativado" || string.IsNullOrEmpty(hotkeyName))
                return;

            // Mapear nome para código de tecla
            uint vk = 0;
            switch (hotkeyName)
            {
                case "F1": vk = 0x70; break;
                case "F2": vk = 0x71; break;
                case "F3": vk = 0x72; break;
                case "F4": vk = 0x73; break;
                case "F5": vk = 0x74; break;
                case "F6": vk = 0x75; break;
                case "F7": vk = 0x76; break;
                case "F8": vk = 0x77; break;
                case "F9": vk = 0x78; break;
                case "F10": vk = 0x79; break;
                case "F11": vk = 0x7A; break;
                case "F12": vk = 0x7B; break;
                default: return;
            }

            _twoFAHotkeyId = vk.GetHashCode();
            RegisterHotKey(Instance.Handle, _twoFAHotkeyId, 0, vk);
        }

        /// <summary>
        /// Atualiza a hotkey global de Add Friend
        /// </summary>
        public static void UpdateAddFriendHotkey(string hotkeyName)
        {
            // Remover hotkey anterior
            if (_addFriendHotkeyId != 0)
            {
                UnregisterHotKey(Instance.Handle, _addFriendHotkeyId);
                _addFriendHotkeyId = 0;
            }

            if (hotkeyName == "Desativado" || string.IsNullOrEmpty(hotkeyName))
            {
                _addFriendHotkeyType = "";
                return;
            }

            uint vk = 0;
            uint modifiers = 0;

            // Verificar combinações Ctrl+Shift
            if (hotkeyName.StartsWith("Ctrl+Shift+"))
            {
                modifiers = MOD_CONTROL | MOD_SHIFT;
                string key = hotkeyName.Replace("Ctrl+Shift+", "");
                switch (key)
                {
                    case "V": vk = 0x56; break; // V
                    case "A": vk = 0x41; break; // A
                    case "F": vk = 0x46; break; // F
                    default: return;
                }
                _addFriendHotkeyType = "combo";
            }
            else
            {
                // Teclas de função F1-F12
                switch (hotkeyName)
                {
                    case "F1": vk = 0x70; break;
                    case "F2": vk = 0x71; break;
                    case "F3": vk = 0x72; break;
                    case "F4": vk = 0x73; break;
                    case "F5": vk = 0x74; break;
                    case "F6": vk = 0x75; break;
                    case "F7": vk = 0x76; break;
                    case "F8": vk = 0x77; break;
                    case "F9": vk = 0x78; break;
                    case "F10": vk = 0x79; break;
                    case "F11": vk = 0x7A; break;
                    case "F12": vk = 0x7B; break;
                    default: return;
                }
                _addFriendHotkeyType = "fkey";
            }

            _addFriendHotkeyId = (int)(vk + 0x100); // Offset para não conflitar com 2FA
            RegisterHotKey(Instance.Handle, _addFriendHotkeyId, modifiers, vk);
        }

        /// <summary>
        /// Registra hotkey para uma conta específica
        /// </summary>
        public static void RegisterAccountHotkey(Account account, string hotkeyName)
        {
            // Remover hotkey anterior se existir
            UnregisterAccountHotkey(account);

            if (hotkeyName == "Desativado" || string.IsNullOrEmpty(hotkeyName))
                return;

            uint vk = 0;
            uint modifiers = 0;

            // Verificar combinações Ctrl+Alt
            if (hotkeyName.StartsWith("Ctrl+Alt+"))
            {
                modifiers = MOD_CONTROL | 0x0001; // MOD_ALT = 0x0001
                string key = hotkeyName.Replace("Ctrl+Alt+", "");
                if (key.Length == 1 && char.IsDigit(key[0]))
                {
                    vk = (uint)(0x30 + (key[0] - '0')); // 0-9
                }
                else return;
            }
            else
            {
                // Teclas de função F1-F12
                switch (hotkeyName)
                {
                    case "F1": vk = 0x70; break;
                    case "F2": vk = 0x71; break;
                    case "F3": vk = 0x72; break;
                    case "F4": vk = 0x73; break;
                    case "F5": vk = 0x74; break;
                    case "F6": vk = 0x75; break;
                    case "F7": vk = 0x76; break;
                    case "F8": vk = 0x77; break;
                    case "F9": vk = 0x78; break;
                    case "F10": vk = 0x79; break;
                    case "F11": vk = 0x7A; break;
                    case "F12": vk = 0x7B; break;
                    default: return;
                }
            }

            int hotkeyId = _nextAccountHotkeyId++;
            if (RegisterHotKey(Instance.Handle, hotkeyId, modifiers, vk))
            {
                _accountHotkeyIds[account.UserID] = hotkeyId;
                account.HotkeyId = hotkeyId;
                account.HotkeyName = hotkeyName;
            }
        }

        /// <summary>
        /// Remove hotkey de uma conta específica
        /// </summary>
        public static void UnregisterAccountHotkey(Account account)
        {
            if (_accountHotkeyIds.TryGetValue(account.UserID, out int hotkeyId))
            {
                UnregisterHotKey(Instance.Handle, hotkeyId);
                _accountHotkeyIds.Remove(account.UserID);
                account.HotkeyId = 0;
                account.HotkeyName = "";
            }
        }

        /// <summary>
        /// Liga/desliga timer que muta processos Roblox
        /// </summary>
        public void UpdateRobloxMuteTimer(bool enabled)
        {
            if (enabled)
            {
                if (_robloxMuteTimer == null)
                {
                    _robloxMuteTimer = new System.Windows.Forms.Timer();
                    _robloxMuteTimer.Interval = 3000;
                    _robloxMuteTimer.Tick += (s, e) => Classes.AudioHelper.MuteAllRobloxProcesses();
                }
                _robloxMuteTimer.Start();
                // Mutar imediatamente também
                Classes.AudioHelper.MuteAllRobloxProcesses();
            }
            else
            {
                _robloxMuteTimer?.Stop();
            }
        }

        /// <summary>
        /// Atualiza a hotkey global para trazer janela na frente
        /// </summary>
        public void UpdateBringToFrontHotkey(string hotkeyName)
        {
            // Remover hotkey anterior
            if (_bringToFrontHotkeyId != 0)
            {
                UnregisterHotKey(Handle, _bringToFrontHotkeyId);
                _bringToFrontHotkeyId = 0;
            }

            if (hotkeyName == "Desativado" || string.IsNullOrEmpty(hotkeyName))
                return;

            uint vk = 0;
            uint modifiers = 0;

            if (hotkeyName.StartsWith("Ctrl+Shift+"))
            {
                modifiers = MOD_CONTROL | MOD_SHIFT;
                string key = hotkeyName.Replace("Ctrl+Shift+", "");
                switch (key)
                {
                    case "B": vk = 0x42; break;
                    case "M": vk = 0x4D; break;
                    default: return;
                }
            }
            else
            {
                switch (hotkeyName)
                {
                    case "F1": vk = 0x70; break;
                    case "F2": vk = 0x71; break;
                    case "F3": vk = 0x72; break;
                    case "F4": vk = 0x73; break;
                    case "F5": vk = 0x74; break;
                    case "F6": vk = 0x75; break;
                    case "F7": vk = 0x76; break;
                    case "F8": vk = 0x77; break;
                    case "F9": vk = 0x78; break;
                    case "F10": vk = 0x79; break;
                    case "F11": vk = 0x7A; break;
                    case "F12": vk = 0x7B; break;
                    default: return;
                }
            }

            _bringToFrontHotkeyId = BRING_TO_FRONT_HOTKEY_BASE + (int)vk;
            RegisterHotKey(Handle, _bringToFrontHotkeyId, modifiers, vk);
        }

        /// <summary>
        /// Restaura e traz a janela do app para frente
        /// </summary>
        private void BringAppToFront()
        {
            ShowWindow(Handle, SW_RESTORE);
            SetForegroundWindow(Handle);
            BringToFront();
        }

        /// <summary>
        /// Processa mensagens do Windows (para hotkey global)
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                
                // Hotkey 2FA
                if (hotkeyId == _twoFAHotkeyId)
                {
                    Generate2FACode();
                }
                // Hotkey Add Friend
                else if (hotkeyId == _addFriendHotkeyId)
                {
                    ExecuteAddFriendHotkey();
                }
                // Hotkey Bring to Front
                else if (hotkeyId == _bringToFrontHotkeyId)
                {
                    BringAppToFront();
                }
                // Hotkey de conta específica
                else
                {
                    foreach (var kvp in _accountHotkeyIds)
                    {
                        if (kvp.Value == hotkeyId)
                        {
                            SelectAccountByUserId(kvp.Key);
                            break;
                        }
                    }
                }
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// Gera código 2FA e copia para clipboard
        /// </summary>
        private void Generate2FACode()
        {
            string secret = TwoFASecretTextBox.Text.Trim().Replace(" ", "");
            
            if (string.IsNullOrEmpty(secret))
            {
                AddLogWarning("⚠️ 2FA: Insira o Secret Key primeiro!");
                return;
            }

            try
            {
                string code = GenerateTOTP(secret);
                TwoFACodeTextBox.Text = code;
                Clipboard.SetText(code);
                AddLog($"🔐 2FA: {code} (copiado!)");
            }
            catch (Exception ex)
            {
                AddLogError($"❌ 2FA: {ex.Message}");
            }
        }

        /// <summary>
        /// Executa a hotkey de adicionar amigo (cola do clipboard e adiciona)
        /// </summary>
        private void ExecuteAddFriendHotkey()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clipboardText = Clipboard.GetText().Trim();
                    
                    if (!string.IsNullOrEmpty(clipboardText))
                    {
                        FriendsAddTextBox.Text = clipboardText;
                        AddLog($"🔄 [Hotkey] Adicionando amigo: {clipboardText}");
                        FriendsAddButton_Click(ADDAMIGO, EventArgs.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogError($"❌ [Hotkey] Erro: {ex.Message}");
            }
        }

        /// <summary>
        /// Seleciona uma conta pelo UserID
        /// </summary>
        private void SelectAccountByUserId(long userId)
        {
            try
            {
                foreach (var account in AccountsList)
                {
                    if (account.UserID == userId)
                    {
                        // Selecionar a conta no AccountsView
                        AccountsView.SelectedObject = account;
                        AccountsView.EnsureModelVisible(account);
                        
                        // Trazer janela para frente
                        this.Activate();
                        this.BringToFront();
                        
                        AddLog($"🎯 [Hotkey] Conta selecionada: {account.Username}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogError($"❌ [Hotkey] Erro ao selecionar conta: {ex.Message}");
            }
        }

        #endregion

        #region Blox Brasil Auto Update

        private async void CheckBloxBrasilUpdate()
        {
            try
            {
                var (hasUpdate, newVersion, downloadUrl, changelog) = await Classes.BloxBrasilUpdater.CheckForUpdateAsync();

                if (hasUpdate && !string.IsNullOrEmpty(downloadUrl))
                {
                    string message = $"Uma nova versão está disponível!\n\n" +
                                   $"Versão atual: {Classes.BloxBrasilUpdater.CurrentVersion}\n" +
                                   $"Nova versão: {newVersion}\n\n" +
                                   $"Novidades:\n{changelog}\n\n" +
                                   $"Deseja atualizar agora?";

                    DialogResult result = MessageBox.Show(message, "Blox Brasil - Atualização Disponível", 
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        await DownloadAndInstallUpdate(downloadUrl, newVersion);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently fail - não interromper o usuário
                System.Diagnostics.Debug.WriteLine($"[UPDATE ERROR] {ex.Message}");
            }
        }

        private async Task DownloadAndInstallUpdate(string downloadUrl, string newVersion)
        {
            // Criar form de progresso
            Form progressForm = new Form
            {
                Text = "Blox Brasil - Atualizando...",
                Size = new Size(400, 120),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false
            };

            Label statusLabel = new Label
            {
                Text = "Baixando atualização...",
                Location = new System.Drawing.Point(20, 15),
                Size = new Size(350, 20)
            };

            ProgressBar progressBar = new ProgressBar
            {
                Location = new System.Drawing.Point(20, 40),
                Size = new Size(350, 25),
                Minimum = 0,
                Maximum = 100
            };

            progressForm.Controls.Add(statusLabel);
            progressForm.Controls.Add(progressBar);
            progressForm.Show();

            var progress = new Progress<int>(percent =>
            {
                progressBar.Value = percent;
                statusLabel.Text = $"Baixando atualização... {percent}%";
            });

            bool success = await Classes.BloxBrasilUpdater.DownloadAndInstallUpdateAsync(downloadUrl, newVersion, progress);

            if (success)
            {
                statusLabel.Text = "Atualização concluída! Reiniciando...";
                await Task.Delay(1000);
                Application.Exit();
            }
            else
            {
                progressForm.Close();
                MessageBox.Show("Erro ao instalar atualização. Tente novamente mais tarde.", 
                    "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        private static ReadOnlyMemory<byte> PasswordHash; // Store the hash after the data is successfully decrypted so we can encrypt again.

        private void LoadAccounts(byte[] Hash = null)
        {
            bool EnteredPassword = false;
            byte[] Data = File.Exists(SaveFilePath) ? File.ReadAllBytes(SaveFilePath) : Array.Empty<byte>();

            if (Data.Length > 0)
            {
                var Header = new ReadOnlySpan<byte>(Data, 0, Cryptography.RAMHeader.Length);

                if (Header.SequenceEqual(Cryptography.RAMHeader))
                {
                    if (Hash == null)
                    {
                        EncryptionSelectionPanel.Visible = false;
                        PasswordSelectionPanel.Visible = false;
                        PasswordLayoutPanel.Visible = true;
                        PasswordPanel.Visible = true;
                        PasswordPanel.BringToFront();
                        PasswordTextBox.Focus();

                        return;
                    }

                    Data = Cryptography.Decrypt(Data, Hash);
                    AccountsList = JsonConvert.DeserializeObject<List<Account>>(Encoding.UTF8.GetString(Data));
                    PasswordHash = new ReadOnlyMemory<byte>(ProtectedData.Protect(Hash, Array.Empty<byte>(), DataProtectionScope.CurrentUser));

                    PasswordPanel.Visible = false;
                    EnteredPassword = true;
                }
                else
                    try { AccountsList = JsonConvert.DeserializeObject<List<Account>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(Data, Entropy, DataProtectionScope.LocalMachine))); }
                    catch (CryptographicException e)
                    {
                        try { AccountsList = JsonConvert.DeserializeObject<List<Account>>(Encoding.UTF8.GetString(Data)); }
                        catch
                        {
                            File.WriteAllBytes(SaveFilePath + ".bak", Data);

                            MessageBox.Show($"Failed to load accounts!\nA backup file was created in case the data can be recovered.\n\n{e.Message}");
                        }
                    }
            }

            AccountsList ??= new List<Account>();

            if (!EnteredPassword && AccountsList.Count == 0 && File.Exists($"{SaveFilePath}.backup") && File.ReadAllBytes($"{SaveFilePath}.backup") is byte[] BackupData && BackupData.Length > 0)
            {
                var Header = new ReadOnlySpan<byte>(BackupData, 0, Cryptography.RAMHeader.Length);

                if (Header.SequenceEqual(Cryptography.RAMHeader) && MessageBox.Show("The existing backup file is password-locked, would you like to attempt to load it?", "Roblox Account Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    if (File.Exists(SaveFilePath))
                    {
                        if (File.Exists($"{SaveFilePath}.old")) File.Delete($"{SaveFilePath}.old");

                        File.Move(SaveFilePath, $"{SaveFilePath}.old");
                    }

                    File.Move($"{SaveFilePath}.backup", SaveFilePath);

                    LoadAccounts();

                    return;
                }

                if (MessageBox.Show("No accounts were loaded but there is a backup file, would you like to load the backup file?", "Roblox Account Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
                {
                    try
                    {
                        string Decoded = Encoding.UTF8.GetString(ProtectedData.Unprotect(BackupData, Entropy, DataProtectionScope.CurrentUser));

                        AccountsList = JsonConvert.DeserializeObject<List<Account>>(Decoded);
                    }
                    catch
                    {
                        try { AccountsList = JsonConvert.DeserializeObject<List<Account>>(Encoding.UTF8.GetString(BackupData)); }
                        catch { MessageBox.Show("Failed to load backup file!", "Roblox Account Manager", MessageBoxButtons.OKCancel, MessageBoxIcon.Error); }
                    }
                }
            }

            AccountsView.SetObjects(AccountsList);
            RefreshView();

            if (AccountsList.Count > 0)
            {
                LastValidAccount = AccountsList[0];

                foreach (Account account in AccountsList)
                {
                    if (account.LastUse > LastValidAccount.LastUse)
                        LastValidAccount = account;

                    // Registrar hotkey salva da conta
                    if (!string.IsNullOrEmpty(account.SavedHotkey) && account.SavedHotkey != "Desativado")
                    {
                        RegisterAccountHotkey(account, account.SavedHotkey);
                    }
                }
            }
        }

        public static void SaveAccounts(bool BypassRateLimit = false, bool BypassCountCheck = false)
        {
            if ((!BypassRateLimit && (DateTime.Now - startTime).Seconds < 2) || (!BypassCountCheck && AccountsList.Count == 0)) return;

            lock (saveLock)
            {
                byte[] OldInfo = File.Exists(SaveFilePath) ? File.ReadAllBytes(SaveFilePath) : Array.Empty<byte>();
                string SaveData = JsonConvert.SerializeObject(AccountsList);

                FileInfo OldFile = new FileInfo(SaveFilePath);
                FileInfo Backup = new FileInfo($"{SaveFilePath}.backup");

                if (!Backup.Exists || (Backup.Exists && (DateTime.Now - Backup.LastWriteTime).TotalMinutes > 60 * 8))
                    File.WriteAllBytes(Backup.FullName, OldInfo);

                if (!PasswordHash.IsEmpty)
                    File.WriteAllBytes(SaveFilePath, Cryptography.Encrypt(SaveData, ProtectedData.Unprotect(PasswordHash.ToArray(), Array.Empty<byte>(), DataProtectionScope.CurrentUser)));
                else
                {
                    if (File.Exists(Path.Combine(Environment.CurrentDirectory, "NoEncryption.IUnderstandTheRisks.iautamor")))
                        File.WriteAllBytes(SaveFilePath, Encoding.UTF8.GetBytes(SaveData));
                    else
                        File.WriteAllBytes(SaveFilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(SaveData), Entropy, DataProtectionScope.LocalMachine));
                }
            }
        }

        public void ResetEncryption(bool ManualReset = false)
        {
            foreach (var Form in Application.OpenForms.OfType<Form>())
                if (Form != this)
                    Form.Hide();

            IsResettingPassword = true;

            PasswordLayoutPanel.Visible = !PasswordHash.IsEmpty && ManualReset;
            PasswordSelectionPanel.Visible = false;
            EncryptionSelectionPanel.Visible = PasswordHash.IsEmpty || !ManualReset;

            PasswordPanel.Visible = true;
            PasswordPanel.BringToFront();
        }

        private void PasswordTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
            {
                UnlockButton.PerformClick();

                e.Handled = true;
            }
        }

        private void Error(string Message)
        {
            Program.Logger.Error(Message);

            throw new Exception(Message);
        }

        private void UnlockButton_Click(object sender, EventArgs e)
        {
            try
            {
                byte[] Hash = CryptoHash.Hash(PasswordTextBox.Text);

                if (PasswordTextBox.Text.Length < 4)
                    Error("Invalid password, your password must contain 4 or more characters");

                if (IsResettingPassword)
                {
                    byte[] Data = File.Exists(SaveFilePath) ? File.ReadAllBytes(SaveFilePath) : Array.Empty<byte>();

                    if (Data.Length > 0)
                    {
                        var Header = new ReadOnlySpan<byte>(Data, 0, Cryptography.RAMHeader.Length);

                        if (Header.SequenceEqual(Cryptography.RAMHeader))
                        {
                            if (Hash == null)
                            {
                                EncryptionSelectionPanel.Visible = false;
                                PasswordSelectionPanel.Visible = false;
                                PasswordLayoutPanel.Visible = true;
                                PasswordPanel.Visible = true;
                                PasswordPanel.BringToFront();
                                PasswordTextBox.Focus();

                                return;
                            }

                            Cryptography.Decrypt(Data, Hash);

                            PasswordLayoutPanel.Visible = false;
                            EncryptionSelectionPanel.Visible = true;
                            IsResettingPassword = false;
                        }
                    }
                }
                else
                    LoadAccounts(Hash);
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Incorrect Password!\n\n{exception.Message}", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { PasswordTextBox.Text = string.Empty; PasswordTextBox.Focus(); }
        }

        private void DefaultEncryptionButton_Click(object sender, EventArgs e)
        {
            PasswordHash = Array.Empty<byte>();
            SaveAccounts(true, true);

            PasswordPanel.Visible = false;
        }

        private void PasswordEncryptionButton_Click(object sender, EventArgs e)
        {
            EncryptionSelectionPanel.Visible = false;
            PasswordLayoutPanel.Visible = false;
            PasswordSelectionPanel.Visible = true;
        }

        private ReadOnlyMemory<byte> LastHash = null;

        private void SetPasswordButton_Click(object sender, EventArgs e)
        {
            if (PasswordSelectionTB.Text.Length < 4)
            {
                MessageBox.Show("Invalid password, your password must contain 4 or more characters", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            byte[] Hash = CryptoHash.Hash(PasswordSelectionTB.Text);

            PasswordHash = new ReadOnlyMemory<byte>(ProtectedData.Protect(Hash, Array.Empty<byte>(), DataProtectionScope.CurrentUser));

            if (LastHash.IsEmpty)
            {
                LastHash = new ReadOnlyMemory<byte>(PasswordHash.ToArray());
                PasswordSelectionTB.Text = string.Empty;
                MessageBox.Show("Please confirm your password.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
            else
            {
                if (ProtectedData.Unprotect(LastHash.ToArray(), Array.Empty<byte>(), DataProtectionScope.CurrentUser).SequenceEqual(Hash.ToArray()))
                {
                    SaveAccounts(true, true);

                    PasswordSelectionTB.Text = string.Empty;
                    PasswordPanel.Visible = false;

                    LastHash = null;
                }
                else
                    MessageBox.Show("You have entered the wrong password, please try again.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        CancellationTokenSource PasswordSelectionCancellationToken;

        private void PasswordSelectionTB_TextChanged(object sender, EventArgs e)
        {
            PasswordSelectionCancellationToken?.Cancel();

            SetPasswordButton.Enabled = false;

            PasswordSelectionCancellationToken = new CancellationTokenSource();
            var Token = PasswordSelectionCancellationToken.Token;

            Task.Run(async () =>
            {
                await Task.Delay(500); // Wait until the user has stopped typing to enable the continue button

                if (Token.IsCancellationRequested)
                    return;

                AccountsView.InvokeIfRequired(() => SetPasswordButton.Enabled = true);
            }, PasswordSelectionCancellationToken.Token);
        }

        private void PasswordPanel_VisibleChanged(object sender, EventArgs e)
        {
            foreach (Control Control in Controls)
                if (Control != PasswordPanel)
                    Control.Enabled = !PasswordPanel.Visible;
        }

        public static bool GetUserID(string Username, out long UserId, out RestResponse response)
        {
            RestRequest request = LastValidAccount?.MakeRequest("v1/usernames/users", Method.Post) ?? new RestRequest("v1/usernames/users", Method.Post);
            request.AddJsonBody(new { usernames = new string[] { Username } });

            response = UsersClient.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK && response.Content.TryParseJson(out JObject UserData) && UserData.ContainsKey("data") && UserData["data"].Count() >= 1)
            {
                UserId = UserData["data"]?[0]?["id"].Value<long>() ?? -1;

                return true;
            }

            UserId = -1;

            return false;
        }

        public void UpdateAccountView(Account account) =>
            AccountsView.InvokeIfRequired(() => AccountsView.UpdateObject(account));

        public static Account AddAccount(string SecurityToken, string Password = "", string AccountJSON = null)
        {
            Account account = new Account(SecurityToken, AccountJSON);

            if (account.Valid)
            {
                account.Password = Password;

                Account exists = AccountsList.AsReadOnly().FirstOrDefault(acc => acc.UserID == account.UserID);

                if (exists != null)
                {
                    account = exists;

                    exists.SecurityToken = SecurityToken;
                    exists.Password = Password;
                    exists.LastUse = DateTime.Now;

                    Instance.RefreshView(exists);
                }
                else
                {
                    AccountsList.Add(account);

                    Instance.RefreshView(account);
                }

                SaveAccounts(true);

                return account;
            }

            return null;
        }

        public static string ShowDialog(string text, string caption, string defaultText = "", bool big = false) // tbh pasted from stackoverflow
        {
            Form prompt = new Form()
            {
                Width = 340,
                Height = big ? 420 : 125,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen
            };

            Label textLabel = new Label() { Left = 15, Top = 10, Text = text, AutoSize = true };
            Control textBox;
            Button confirmation = new Button() { Text = "OK", Left = 15, Width = 100, Top = big ? 350 : 50, DialogResult = DialogResult.OK };

            if (big) textBox = new RichTextBox() { Left = 15, Top = 15 + textLabel.Size.Height, Width = 295, Height = 330 - textLabel.Size.Height, Text = defaultText };
            else textBox = new TextBox() { Left = 15, Top = 25, Width = 295, Text = defaultText };

            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(textLabel);
            if (!big) prompt.AcceptButton = confirmation;

            prompt.Rescale();

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "/UC";
        }

        private void AccountManager_Load(object sender, EventArgs e)
        {
            PasswordPanel.Dock = DockStyle.Fill;

            string AFN = Path.Combine(Directory.GetCurrentDirectory(), "Auto Update.exe");
            string AU2FN = Path.Combine(Directory.GetCurrentDirectory(), "AU.exe");

            if (File.Exists(AFN)) File.Delete(AFN);
            if (File.Exists(AU2FN)) File.Delete(AU2FN);

            DirectoryInfo UpdateDir = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "Update"));

            if (UpdateDir.Exists)
                UpdateDir.RecursiveDelete();

            afform = new ArgumentsForm();
            ServerListForm = new ServerList();
            UtilsForm = new AccountUtils();
            ImportAccountsForm = new ImportForm();
            FieldsForm = new AccountFields();
            ThemeForm = new ThemeEditor();
            RGForm = new RecentGamesForm();

            MainClient = new RestClient("https://www.roblox.com/");
            AvatarClient = new RestClient("https://avatar.roblox.com/");
            AuthClient = new RestClient("https://auth.roblox.com/");
            EconClient = new RestClient("https://economy.roblox.com/");
            AccountClient = new RestClient("https://accountsettings.roblox.com/");
            GameJoinClient = new RestClient(new RestClientOptions("https://gamejoin.roblox.com/") { UserAgent = "Roblox/WinInet" });
            UsersClient = new RestClient("https://users.roblox.com");
            FriendsClient = new RestClient("https://friends.roblox.com");
            PresenceClient = new RestClient("https://presence.roblox.com");
            Web13Client = new RestClient("https://web.roblox.com/");
            ApisClient = new RestClient("https://apis.roblox.com/");

            if (File.Exists(SaveFilePath))
                LoadAccounts();
            else
            {
                // Primeira execução - usar encriptação padrão automaticamente, sem popup
                PasswordHash = Array.Empty<byte>();
                SaveAccounts(true, true);
            }

            LoadPrivateServers();
            LoadAntiCaptchaExtensions();

            ApplyTheme();

            RGForm.RecentGameSelected += (sender, e) => { PlaceID.Text = e.Game.Details?.placeId.ToString(); };
            RGForm.RecentGameDeleted += RGForm_RecentGameDeleted;

            PlaceID.Text = General.Exists("SavedPlaceId") ? General.Get("SavedPlaceId") : "5315046213";

            if (!Developer.Get<bool>("DevMode"))
            {
                AccountsStrip.Items.Remove(viewFieldsToolStripMenuItem);
                AccountsStrip.Items.Remove(getAuthenticationTicketToolStripMenuItem);
                AccountsStrip.Items.Remove(copyRbxplayerLinkToolStripMenuItem);
                AccountsStrip.Items.Remove(copySecurityTokenToolStripMenuItem);
                AccountsStrip.Items.Remove(copyAppLinkToolStripMenuItem);
            }
            else
                


            // Verificar atualizações do Blox Brasil
            CheckBloxBrasilUpdate();

            // Inicializar painel de inventário (Supabase)
            InitializeInventoryPanel();
            
            // Inicializar painel de amigos
            InitializeFriendsPanel();

            // Inicializar hotkey 2FA
            string savedHotkey = General.Get<string>("TwoFAHotkey");
            if (!string.IsNullOrEmpty(savedHotkey) && savedHotkey != "Desativado")
                UpdateTwoFAHotkey(savedHotkey);

            // Inicializar mute Roblox
            if (General.Get<bool>("MuteRoblox"))
                UpdateRobloxMuteTimer(true);

            // Inicializar hotkey Bring to Front
            string savedBringToFrontHotkey = General.Get<string>("BringToFrontHotkey");
            if (!string.IsNullOrEmpty(savedBringToFrontHotkey) && savedBringToFrontHotkey != "Desativado")
                UpdateBringToFrontHotkey(savedBringToFrontHotkey);

            if (!General.Get<bool>("DisableAgingAlert"))
                Username.Renderer = new AccountRenderer();

            try
            {
                if (Developer.Get<bool>("EnableWebServer"))
                {
                    string Port = WebServer.Exists("WebServerPort") ? WebServer.Get("WebServerPort") : "7963";

                    List<string> Prefixes = new List<string>() { $"http://localhost:{Port}/" };

                    if (WebServer.Get<bool>("AllowExternalConnections"))
                        if (Program.Elevated)
                            Prefixes.Add($"http://*:{Port}/");
                        else
                            using (Process proc = new Process() { StartInfo = new ProcessStartInfo(AppDomain.CurrentDomain.FriendlyName, "-adminRequested") { Verb = "runas" } })
                                try
                                {
                                    proc.Start();
                                    Environment.Exit(1);
                                }
                                catch { }


                    AltManagerWS = new WebServer(SendResponse, Prefixes.ToArray());
                    AltManagerWS.Run();
                }
            }
            catch (Exception x) { MessageBox.Show($"Failed to start webserver!\n\n{x}", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Error); }

            Task.Run(() =>
            {
                WebClient WC = new WebClient();
                string VersionJSON = WC.DownloadString("https://clientsettings.roblox.com/v1/client-version/WindowsPlayer");

                if (JObject.Parse(VersionJSON).TryGetValue("clientVersionUpload", out JToken token))
                    CurrentVersion = token.Value<string>();
            });

            IniSettings.Save("RAMSettings.ini");

            PlaceID.AutoCompleteCustomSource = new AutoCompleteStringCollection();
            PlaceID.AutoCompleteMode = AutoCompleteMode.Suggest;
            PlaceID.AutoCompleteSource = AutoCompleteSource.CustomSource;

            Task.Run(LoadRecentGames);
            Task.Run(RobloxProcess.UpdateMatches);

            if (General.Get<bool>("AutoCookieRefresh"))
            {
                AutoCookieRefresh = new System.Timers.Timer(60000 * 5) { Enabled = true };
                AutoCookieRefresh.Elapsed += async (s, e) =>
                {
                    int Count = 0;

                    foreach (var Account in AccountsList)
                    {
                        if (Account.GetField("NoCookieRefresh") != "true" && (DateTime.Now - Account.LastUse).TotalDays > 20 && (DateTime.Now - Account.LastAttemptedRefresh).TotalDays >= 7)
                        {
                            Program.Logger.Info($"Attempting to refresh {Account.Username} | Last Use: {Account.LastUse}");

                            Account.LastAttemptedRefresh = DateTime.Now;

                            if (Account.LogOutOfOtherSessions(true)) Count++;

                            await Task.Delay(5000);
                        }
                    };
                };
            }

            var PresenceTimer = new System.Timers.Timer(60000 * 2) { Enabled = true };
            PresenceTimer.Elapsed += (s, e) => AccountsView.InvokeIfRequired(async () => await UpdatePresence());
        }

        public void ApplyTheme()
        {
            BackColor = ThemeEditor.FormsBackground;
            ForeColor = ThemeEditor.FormsForeground;

            if (AccountsView.BackColor != ThemeEditor.AccountBackground || AccountsView.ForeColor != ThemeEditor.AccountForeground)
            {
                AccountsView.BackColor = ThemeEditor.AccountBackground;
                AccountsView.ForeColor = ThemeEditor.AccountForeground;

                RefreshView();
            }

            AccountsView.HeaderStyle = ThemeEditor.ShowHeaders ? (AccountsView.ShowGroups ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.Clickable) : ColumnHeaderStyle.None;
            AccountsView.CellEditActivation = ObjectListView.CellEditActivateMode.DoubleClick;

            Controls.ApplyTheme();

            // Painéis que não são tratados pelo ApplyTheme extension (Panel não tem handler)
            GameSelectorPanel.BackColor = ThemeEditor.FormsBackground;
            EstoquePanel.BackColor = ThemeEditor.FormsBackground;
            EstoqueTitleLabel.BackColor = ThemeEditor.HeaderBackground;
            EstoqueRefreshButton.BackColor = ThemeEditor.HeaderBackground;

            // Preservar cor terminal do DebugLog
            DebugLogTextBox.BackColor = ThemeEditor.TextBoxesBackground;
            DebugLogTextBox.ForeColor = Color.LightGreen;

            afform.ApplyTheme();
            ServerListForm.ApplyTheme();
            UtilsForm.ApplyTheme();
            ImportAccountsForm.ApplyTheme();
            FieldsForm.ApplyTheme();
            ThemeForm.ApplyTheme();
            RGForm.ApplyTheme();

            ControlForm?.ApplyTheme();
            SettingsForm?.ApplyTheme();
        }

        private async void LoadRecentGames()
        {
            RecentGames = new List<Game>();

            if (File.Exists(RecentGamesFilePath))
            {
                List<Game> Games = JsonConvert.DeserializeObject<List<Game>>(File.ReadAllText(RecentGamesFilePath));

                RGForm.LoadGames(Games);

                foreach (Game RG in Games)
                    await AddRecentGame(RG, true);
            }
        }

        private async Task AddRecentGame(Game RG, bool Loading = false)
        {
            await RG.WaitForDetails();

            RecentGames.RemoveAll(g => g?.Details?.placeId == RG.Details?.placeId);

            while (RecentGames.Count > General.Get<int>("MaxRecentGames"))
            {
                this.InvokeIfRequired(() => PlaceID.AutoCompleteCustomSource.Remove(RecentGames[0].Details?.filteredName));
                RecentGames.RemoveAt(0);
            }

            RecentGames.Add(RG);

            this.InvokeIfRequired(() => PlaceID.AutoCompleteCustomSource.Add(RG.Details.filteredName));

            if (!Loading)
            {
                this.InvokeIfRequired(() => RecentGameAdded?.Invoke(this, new GameArgs(RG)));

                lock (rgSaveLock)
                    File.WriteAllText(RecentGamesFilePath, JsonConvert.SerializeObject(RecentGames));
            }
        }

        /// <summary>
        /// Remove um jogo da lista de jogos recentes
        /// </summary>
        private void RGForm_RecentGameDeleted(object sender, GameArgs e)
        {
            if (e.Game?.Details?.placeId == null) return;

            // Remover da lista
            RecentGames.RemoveAll(g => g?.Details?.placeId == e.Game.Details?.placeId);

            // Remover do autocomplete
            this.InvokeIfRequired(() => PlaceID.AutoCompleteCustomSource.Remove(e.Game.Details?.filteredName));

            // Salvar alterações
            lock (rgSaveLock)
                File.WriteAllText(RecentGamesFilePath, JsonConvert.SerializeObject(RecentGames));
        }

        private readonly List<ServerData> AttemptedJoins = new List<ServerData>();

        private string WebServerResponse(object Message, bool Success) => JsonConvert.SerializeObject(new { Success, Message });

        private string SendResponse(HttpListenerContext Context)
        {
            HttpListenerRequest request = Context.Request;

            bool V2 = request.Url.AbsolutePath.StartsWith("/v2/");
            string AbsolutePath = V2 ? request.Url.AbsolutePath.Substring(3) : request.Url.AbsolutePath;

            string Reply(string Response, bool Success = false, int Code = -1, string Raw = null)
            {
                Context.Response.StatusCode = Code > 0 ? Code : (Success ? 200 : 400);

                return V2 ? WebServerResponse(Response, Success) : (Raw ?? Response);
            }

            if (!request.IsLocal && !WebServer.Get<bool>("AllowExternalConnections")) return Reply("External connections are not allowed", false, 401, string.Empty);
            if (AbsolutePath == "/favicon.ico") return ""; // always return nothing

            if (AbsolutePath == "/Running") return Reply("Roblox Account Manager is running", true, Raw: "true");

            string Body = new StreamReader(request.InputStream).ReadToEnd();
            string Method = AbsolutePath.Substring(1);
            string Account = request.QueryString["Account"];
            string Password = request.QueryString["Password"];

            if (WebServer.Get<bool>("EveryRequestRequiresPassword") && (WSPassword.Length < 6 || Password != WSPassword)) return Reply("Invalid Password, make sure your password contains 6 or more characters", false, 401, "Invalid Password");

            if ((Method == "GetCookie" || Method == "GetAccounts" || Method == "LaunchAccount" || Method == "FollowUser") && ((WSPassword != null && WSPassword.Length < 6) || (Password != null && Password != WSPassword))) return Reply("Invalid Password, make sure your password contains 6 or more characters", false, 401, "Invalid Password");

            if (Method == "GetAccounts")
            {
                if (!WebServer.Get<bool>("AllowGetAccounts")) return Reply("Method `GetAccounts` not allowed", false, 401, "Method not allowed");

                string Names = "";
                string GroupFilter = request.QueryString["Group"];

                foreach (Account acc in AccountsList)
                {
                    if (!string.IsNullOrEmpty(GroupFilter) && acc.Group != GroupFilter) continue;

                    Names += acc.Username + ",";
                }

                return Reply(Names.Remove(Names.Length - 1), true, Raw: Names.Remove(Names.Length - 1));
            }

            if (Method == "GetAccountsJson")
            {
                if (!WebServer.Get<bool>("AllowGetAccounts")) return Reply("Method `GetAccountsJson` not allowed", false, 401, "Method not allowed");

                string GroupFilter = request.QueryString["Group"];
                bool ShowCookies = WSPassword.Length >= 6 && Password != WSPassword && request.QueryString["IncludeCookies"] == "true" && WebServer.Get<bool>("AllowGetCookie");

                List<object> Objects = new List<object>();

                foreach (Account acc in AccountsList)
                {
                    if (!string.IsNullOrEmpty(GroupFilter) && acc.Group != GroupFilter) continue;

                    object AccountObject = new
                    {
                        acc.Username,
                        acc.UserID,
                        acc.Alias,
                        acc.Description,
                        acc.Group,
                        acc.CSRFToken,
                        LastUsed = acc.LastUse.ToRobloxTick(),
                        Cookie = ShowCookies ? acc.SecurityToken : null,
                        acc.Fields,
                    };

                    Objects.Add(AccountObject);
                }

                return Reply(JsonConvert.SerializeObject(Objects), true);
            }

            if (Method == "ImportCookie")
            {
                Account New = AddAccount(request.QueryString["Cookie"]);

                bool Success = New != null;

                return Reply(Success ? "Cookie successfully imported" : "[ImportCookie] An error was encountered importing the cookie", Success, Raw: Success ? "true" : "false");
            }

            if (string.IsNullOrEmpty(Account)) return Reply("Empty Account", false);

            Account account = AccountsList.FirstOrDefault(x => x.Username == Account || x.UserID.ToString() == Account);

            if (account == null || !account.GetCSRFToken(out string Token)) return Reply("Invalid Account, the account's cookie may have expired and resulted in the account being logged out", false, Raw: "Invalid Account");

            if (Method == "GetCookie")
            {
                if (!WebServer.Get<bool>("AllowGetCookie")) return Reply("Method `GetCookie` not allowed", false, 401, "Method not allowed");

                return Reply(account.SecurityToken, true);
            }

            if (Method == "LaunchAccount")
            {
                if (!WebServer.Get<bool>("AllowLaunchAccount")) return Reply("Method `LaunchAccount` not allowed", false, 401, "Method not allowed");

                bool ValidPlaceId = long.TryParse(request.QueryString["PlaceId"], out long PlaceId); if (!ValidPlaceId) return Reply("Invalid PlaceId provided", false, Raw: "Invalid PlaceId");

                string JobID = !string.IsNullOrEmpty(request.QueryString["JobId"]) ? request.QueryString["JobId"] : "";
                string FollowUser = request.QueryString["FollowUser"];
                string JoinVIP = request.QueryString["JoinVIP"];

                account.JoinServer(PlaceId, JobID, FollowUser == "true", JoinVIP == "true");

                return Reply($"Launched {Account} to {PlaceId}", true);
            }

            if (Method == "FollowUser") // https://github.com/ic3w0lf22/Roblox-Account-Manager/pull/52
            {
                if (!WebServer.Get<bool>("AllowLaunchAccount")) return Reply("Method `FollowUser` not allowed", false, 401, "Method not allowed");

                string User = request.QueryString["Username"]; if (string.IsNullOrEmpty(User)) return Reply("Invalid Username Parameter", false);

                if (!GetUserID(User, out long UserId, out var Response))
                    return Reply($"[{Response.StatusCode} {Response.StatusDescription}] Failed to get UserId: {Response.Content}", false);

                account.JoinServer(UserId, "", true);

                return Reply($"Joining {User}'s game on {Account}", true);
            }

            if (Method == "GetCSRFToken") return Reply(Token, true);
            if (Method == "GetAlias") return Reply(account.Alias, true);
            if (Method == "GetDescription") return Reply(account.Description, true);

            if (Method == "BlockUser" && !string.IsNullOrEmpty(request.QueryString["UserId"]))
                try
                {
                    var Res = account.BlockUserId(request.QueryString["UserId"], Context: Context);

                    return Reply(Res.Content, Res.IsSuccessful, (int)Res.StatusCode);
                }
                catch (Exception x) { return Reply(x.Message, false, 500); }
            if (Method == "UnblockUser" && !string.IsNullOrEmpty(request.QueryString["UserId"]))
                try
                {
                    var Res = account.UnblockUserId(request.QueryString["UserId"], Context: Context);

                    return Reply(Res.Content, Res.IsSuccessful, (int)Res.StatusCode);
                }
                catch (Exception x) { return Reply(x.Message, false, 500); }
            if (Method == "GetBlockedList") try
                {
                    var Res = account.GetBlockedList(Context);

                    return Reply(Res.Content, Res.IsSuccessful, (int)Res.StatusCode);
                }
                catch (Exception x) { return Reply(x.Message, false, 500); }
            if (Method == "UnblockEveryone" && account.UnblockEveryone(out string UbRes) is bool UbSuccess) return Reply(UbRes, UbSuccess);

            if (Method == "SetServer" && !string.IsNullOrEmpty(request.QueryString["PlaceId"]) && !string.IsNullOrEmpty(request.QueryString["JobId"]))
            {
                string RSP = account.SetServer(Convert.ToInt64(request.QueryString["PlaceId"]), request.QueryString["JobId"], out bool Success);

                return Reply(RSP, Success);
            }

            if (Method == "SetRecommendedServer")
            {
                int attempts = 0;
                string res = "-1";

                for (int i = RBX_Alt_Manager.ServerList.servers.Count - 1; i > 0; i--)
                {
                    if (attempts > 10)
                        return Reply("Too many failed attempts", false);

                    ServerData server = RBX_Alt_Manager.ServerList.servers[i];

                    if (AttemptedJoins.FirstOrDefault(x => x.id == server.id) != null) continue;
                    if (AttemptedJoins.Count > 100) AttemptedJoins.Clear();

                    AttemptedJoins.Add(server);

                    attempts++;

                    res = account.SetServer(!string.IsNullOrEmpty(request.QueryString["PlaceId"]) ? Convert.ToInt64(request.QueryString["PlaceId"]) : RBX_Alt_Manager.ServerList.CurrentPlaceID, server.id, out bool iSuccess);

                    if (iSuccess)
                        return Reply(res, iSuccess);
                }

                bool Success = !string.IsNullOrEmpty(res);

                return Reply(Success ? "Failed" : res, Success);
            }

            if (Method == "GetField" && !string.IsNullOrEmpty(request.QueryString["Field"])) return Reply(account.GetField(request.QueryString["Field"]), true);

            if (Method == "SetField" && !string.IsNullOrEmpty(request.QueryString["Field"]) && !string.IsNullOrEmpty(request.QueryString["Value"]))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) return Reply("Method `SetField` not allowed", false, 401, "Method not allowed");

                account.SetField(request.QueryString["Field"], request.QueryString["Value"]);

                return Reply($"Set Field {request.QueryString["Field"]} to {request.QueryString["Value"]} for {account.Username}", true);
            }
            if (Method == "RemoveField" && !string.IsNullOrEmpty(request.QueryString["Field"]))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) return Reply("Method `RemoveField` not allowed", false, 401, "Method not allowed");

                account.RemoveField(request.QueryString["Field"]);

                return Reply($"Removed Field {request.QueryString["Field"]} from {account.Username}", true);
            }

            if (Method == "SetAvatar" && Body.TryParseJson(out object _))
            {
                account.SetAvatar(Body);

                return Reply($"Attempting to set avatar of {account.Username} to {Body}", true);
            }

            if (Method == "SetAlias" && !string.IsNullOrEmpty(Body))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) return Reply("Method `SetAlias` not allowed", false, Raw: "Method not allowed");

                account.Alias = Body;
                UpdateAccountView(account);

                return Reply($"Set Alias of {account.Username} to {Body}", true);
            }
            if (Method == "SetDescription" && !string.IsNullOrEmpty(Body))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) Reply("Method `SetDescription` not allowed", false, Raw: "Method not allowed");

                account.Description = Body;
                UpdateAccountView(account);

                return Reply($"Set Description of {account.Username} to {Body}", true);
            }
            if (Method == "AppendDescription" && !string.IsNullOrEmpty(Body))
            {
                if (!WebServer.Get<bool>("AllowAccountEditing")) return V2 ? WebServerResponse("Method `AppendDescription` not allowed", false) : "Method not allowed";

                account.Description += Body;
                UpdateAccountView(account);

                return Reply($"Appended Description of {account.Username} with {Body}", true);
            }

            return Reply("404 not found", false, 404);
        }

        private void AccountManager_Shown(object sender, EventArgs e)
        {
            if (!UpdateMultiRoblox() && !General.Get<bool>("HideRbxAlert"))
                MessageBox.Show(
                    "O Roblox já está aberto!\n\n" +
                    "O Multi Roblox NÃO vai funcionar até você:\n" +
                    "1. Fechar TODOS os processos do Roblox\n" +
                    "2. Reabrir o Account Manager\n" +
                    "3. Só depois abrir o Roblox pelo Account Manager\n\n" +
                    "Ordem correta: Account Manager → Conta principal → Outras contas",
                    "Multi Roblox - Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            // Limpar pasta de downloads residuais do Roblox (evita acúmulo de GB)
            CleanRobloxDownloads();

            // Tentar adicionar exclusão do Defender para evitar que o AV bloqueie o Roblox
            EnsureRobloxDefenderExclusion();

            int Major = Environment.OSVersion.Version.Major, Minor = Environment.OSVersion.Version.Minor;

            PuppeteerSupported = !(Major < 6 || (Major == 6 && Minor <= 1));

            // CefSharp removido — sempre usar Chromium (Puppeteer)

            if (!PuppeteerSupported)
            {
                AddAccountsStrip.Items.Remove(bulkUserPassToolStripMenuItem);
                AddAccountsStrip.Items.Remove(customURLJSToolStripMenuItem);
                OpenBrowserStrip.Items.Remove(URLJSToolStripMenuItem);
                OpenBrowserStrip.Items.Remove(joinGroupToolStripMenuItem);
            }

            if (PuppeteerSupported && (!Directory.Exists(AccountBrowser.Fetcher.DownloadsFolder) || Directory.GetDirectories(AccountBrowser.Fetcher.DownloadsFolder).Length == 0))
            {
                AddUserPassButton.Visible = false;
                AddCookieButton.Visible = false;

                Task.Run(async () =>
                {

                    IsDownloadingChromium = false;

                    this.InvokeIfRequired(() =>
                    {
                        AddUserPassButton.Visible = true;
                        AddCookieButton.Visible = true;
                    });
                });
            }
            // CefSharp removido — apenas Chromium (Puppeteer) é suportado
        }

        /// <summary>
        /// Limpa a pasta de downloads residuais do Roblox para evitar acúmulo de arquivos grandes.
        /// O bootstrapper do Roblox deixa arquivos em Downloads\roblox-player que podem acumular GBs.
        /// </summary>
        private void CleanRobloxDownloads()
        {
            Task.Run(() =>
            {
                try
                {
                    string downloadsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Roblox", "Downloads");

                    if (!Directory.Exists(downloadsPath)) return;

                    long totalSize = 0;
                    var dirs = Directory.GetDirectories(downloadsPath);

                    foreach (var dir in dirs)
                    {
                        try
                        {
                            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                                totalSize += new FileInfo(file).Length;
                        }
                        catch { }
                    }

                    // Só limpa se acumulou mais de 500MB
                    if (totalSize < 500 * 1024 * 1024) return;

                    double sizeMB = totalSize / (1024.0 * 1024.0);
                    AddLog($"🧹 [Multi Roblox] Limpando {sizeMB:F0}MB de downloads residuais do Roblox...");

                    int cleaned = 0;
                    foreach (var dir in dirs)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            cleaned++;
                        }
                        catch { }
                    }

                    // Limpar arquivos soltos na pasta Downloads também
                    try
                    {
                        foreach (var file in Directory.GetFiles(downloadsPath))
                        {
                            try { File.Delete(file); cleaned++; } catch { }
                        }
                    }
                    catch { }

                    if (cleaned > 0)
                        AddLog($"✅ [Multi Roblox] Limpeza concluída: {cleaned} itens removidos ({sizeMB:F0}MB liberados)");
                }
                catch (Exception ex)
                {
                    if (DebugModeAtivo)
                        AddLog($"⚠️ [Multi Roblox] Erro na limpeza: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Tenta adicionar exclusão do Windows Defender para a pasta do Roblox.
        /// Isso evita que o antivírus bloqueie arquivos baixados pelo bootstrapper do Roblox,
        /// o que causa problemas com MultiRoblox (re-download constante).
        /// Só tenta uma vez (salva flag no INI).
        /// </summary>
        private void EnsureRobloxDefenderExclusion()
        {
            if (General.Get<bool>("DefenderExclusionDone")) return;

            Task.Run(() =>
            {
                try
                {
                    string robloxPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Roblox");

                    if (!Directory.Exists(robloxPath)) return;

                    // Verificar se já existe exclusão
                    var checkProcess = new Process();
                    checkProcess.StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -Command \"(Get-MpPreference).ExclusionPath\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    checkProcess.Start();
                    string existingExclusions = checkProcess.StandardOutput.ReadToEnd();
                    checkProcess.WaitForExit(5000);

                    if (existingExclusions.Contains(robloxPath))
                    {
                        AddLog("✅ [Defender] Exclusão do Roblox já configurada");
                        this.InvokeIfRequired(() => { General.Set("DefenderExclusionDone", "true"); IniSettings.Save("RAMSettings.ini"); });
                        return;
                    }

                    // Tentar adicionar exclusão (requer admin)
                    var addProcess = new Process();
                    addProcess.StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"Add-MpPreference -ExclusionPath '{robloxPath}'\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    addProcess.Start();
                    addProcess.WaitForExit(10000);

                    if (addProcess.ExitCode == 0)
                    {
                        AddLog($"✅ [Defender] Exclusão adicionada: {robloxPath}");
                        this.InvokeIfRequired(() => { General.Set("DefenderExclusionDone", "true"); IniSettings.Save("RAMSettings.ini"); });
                    }
                    else
                    {
                        AddLog($"⚠️ [Defender] Não foi possível adicionar exclusão (código: {addProcess.ExitCode})");
                        this.InvokeIfRequired(() => { General.Set("DefenderExclusionDone", "true"); IniSettings.Save("RAMSettings.ini"); });
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Usuário cancelou o UAC
                    AddLog("⚠️ [Defender] Permissão negada. Se tiver problemas com o Roblox re-baixando, adicione exclusão manualmente no Windows Defender para: %LocalAppData%\\Roblox");
                    this.InvokeIfRequired(() => { General.Set("DefenderExclusionDone", "true"); IniSettings.Save("RAMSettings.ini"); });
                }
                catch (Exception ex)
                {
                    if (DebugModeAtivo)
                        AddLog($"⚠️ [Defender] Erro: {ex.Message}");
                }
            });
        }

        public bool UpdateMultiRoblox()
        {
            bool Enabled = General.Get<bool>("EnableMultiRbx");

            if (Enabled && rbxMultiMutex == null)
                try
                {
                    rbxMultiMutex = new Mutex(true, "ROBLOX_singletonMutex");

                    if (!rbxMultiMutex.WaitOne(TimeSpan.Zero, true))
                    {
                        AddLog("⚠️ [Multi Roblox] Não foi possível adquirir o mutex. O Roblox já está rodando. Feche todos os processos do Roblox e reabra o Account Manager.");
                        return false;
                    }

                    AddLog("✅ [Multi Roblox] Mutex adquirido com sucesso. Múltiplas instâncias permitidas.");
                }
                catch (Exception ex)
                {
                    AddLog($"❌ [Multi Roblox] Erro ao adquirir mutex: {ex.Message}");
                    return false;
                }
            else if (!Enabled && rbxMultiMutex != null)
            {
                rbxMultiMutex.Close();
                rbxMultiMutex = null;
                AddLog("ℹ️ [Multi Roblox] Desativado. Mutex liberado.");
            }

            return true;
        }

        private void Remove_Click(object sender, EventArgs e)
        {
            if (AccountsView.SelectedObjects.Count > 1)
            {
                DialogResult result = MessageBox.Show($"Are you sure you want to remove {AccountsView.SelectedObjects.Count} accounts?", "Remove Accounts", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    foreach (Account acc in AccountsView.SelectedObjects)
                        AccountsList.Remove(acc);

                    RefreshView();

                    SaveAccounts();
                }
            }
            else if (SelectedAccount != null)
            {
                DialogResult result = MessageBox.Show($"Are you sure you want to remove {SelectedAccount.Username}?", "Remove Account", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    AccountsList.RemoveAll(x => x == SelectedAccount);

                    RefreshView();

                    SaveAccounts();
                }
            }
        }

        private async void DeleteFriendsButton_Click(object sender, EventArgs e)
        {
            var selectedAccounts = AccountsView.SelectedObjects.Cast<Account>().ToList();

            if (selectedAccounts.Count == 0)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string msg = selectedAccounts.Count == 1
                ? $"Tem certeza que deseja REMOVER TODAS AS AMIZADES da conta '{selectedAccounts[0].Username}'?\n\nEssa ação não pode ser desfeita!"
                : $"Tem certeza que deseja REMOVER TODAS AS AMIZADES de {selectedAccounts.Count} contas?\n\nEssa ação não pode ser desfeita!";

            DialogResult confirm = MessageBox.Show(msg, "Confirmar Remoção de Amizades", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            int totalRemoved = 0;
            int accountsProcessed = 0;

            foreach (var account in selectedAccounts)
            {
                try
                {
                    AddLog($"🗑️ Removendo amizades de {account.Username} ({++accountsProcessed}/{selectedAccounts.Count})...");
                    int removedCount = await DeleteAllFriendsAsync(account);
                    totalRemoved += removedCount;
                    AddLogSuccess($"✅ {removedCount} amizades removidas de {account.Username}");
                }
                catch (Exception ex)
                {
                    AddLogError($"❌ Erro ao remover amizades de {account.Username}: {ex.Message}");
                }
            }

            MessageBox.Show(
                $"{totalRemoved} amizades removidas de {selectedAccounts.Count} conta(s)!",
                "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Remove todas as amizades de uma conta via API do Roblox
        /// </summary>
        private async Task<int> DeleteAllFriendsAsync(Account account)
        {
            int removedCount = 0;
            
            try
            {
                var cookieContainer = new System.Net.CookieContainer();
                cookieContainer.Add(new Uri("https://friends.roblox.com"), new System.Net.Cookie(".ROBLOSECURITY", account.SecurityToken, "/", ".roblox.com"));
                
                using (var handler = new System.Net.Http.HttpClientHandler { CookieContainer = cookieContainer, UseProxy = false })
                using (var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                    
                    // Primeiro, obter o CSRF token
                    string csrfToken = "";
                    try
                    {
                        var csrfResponse = await client.PostAsync("https://auth.roblox.com/v2/logout", null);
                        if (csrfResponse.Headers.TryGetValues("x-csrf-token", out var tokens))
                        {
                            csrfToken = tokens.FirstOrDefault() ?? "";
                        }
                    }
                    catch { }
                    
                    if (!string.IsNullOrEmpty(csrfToken))
                    {
                        client.DefaultRequestHeaders.Add("x-csrf-token", csrfToken);
                    }

                    // Buscar lista de amigos
                    long userId = account.UserID;
                    if (userId == 0)
                    {
                        // Buscar UserId se não tiver
                        var userResponse = await client.PostAsync(
                            "https://users.roblox.com/v1/usernames/users",
                            new System.Net.Http.StringContent(
                                $"{{\"usernames\":[\"{account.Username}\"]}}",
                                System.Text.Encoding.UTF8,
                                "application/json"
                            )
                        );
                        var userJson = await userResponse.Content.ReadAsStringAsync();
                        var userMatch = System.Text.RegularExpressions.Regex.Match(userJson, "\"id\":(\\d+)");
                        if (userMatch.Success)
                            long.TryParse(userMatch.Groups[1].Value, out userId);
                    }

                    if (userId == 0)
                    {
                        throw new Exception("Não foi possível obter o UserId");
                    }

                    // Buscar todos os amigos
                    var friendsResponse = await client.GetStringAsync($"https://friends.roblox.com/v1/users/{userId}/friends");
                    
                    // Extrair IDs dos amigos
                    var friendIds = System.Text.RegularExpressions.Regex.Matches(friendsResponse, "\"id\":(\\d+)")
                        .Cast<System.Text.RegularExpressions.Match>()
                        .Select(m => m.Groups[1].Value)
                        .Distinct()
                        .ToList();

                    AddLog($"📋 Encontrados {friendIds.Count} amigos para remover...");

                    // Remover cada amigo
                    foreach (var friendId in friendIds)
                    {
                        try
                        {
                            var unfriendResponse = await client.PostAsync(
                                $"https://friends.roblox.com/v1/users/{friendId}/unfriend",
                                null
                            );
                            
                            if (unfriendResponse.IsSuccessStatusCode)
                            {
                                removedCount++;
                                
                                // Log a cada 10 removidos
                                if (removedCount % 10 == 0)
                                {
                                    AddLog($"🗑️ {removedCount}/{friendIds.Count} amizades removidas...");
                                }
                            }
                            
                            // Pequeno delay para não sobrecarregar a API
                            await Task.Delay(100);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao remover amizades: {ex.Message}");
            }

            return removedCount;
        }

        private async void Add_Click(object sender, EventArgs e)
        {
            if (PuppeteerSupported)
            {
                AddUserPassButton.Enabled = false;
                AddCookieButton.Enabled = false;

                try { await new AccountBrowser().Login(); }
                catch (Exception x)
                {
                    Program.Logger.Error($"[Add_Click] An error was encountered attempting to login: {x}");

                    if (Utilities.YesNoPrompt($"An error was encountered attempting to login", "You may have a corrupted chromium installation", "Would you like to re-install chromium?", false))
                    {
                        MessageBox.Show("Roblox Account Manager will now close since it can't delete the folder while it's in use.", "", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        if (Directory.GetFiles(AccountBrowser.Fetcher.DownloadsFolder).Length <= 1 && Directory.GetDirectories(AccountBrowser.Fetcher.DownloadsFolder).Length <= 1)
                            Process.Start("cmd.exe", $"/c rmdir /s /q \"{AccountBrowser.Fetcher.DownloadsFolder}\"");
                        else
                            Process.Start("explorer.exe", "/select, " + AccountBrowser.Fetcher.DownloadsFolder);

                        Environment.Exit(0);
                    }
                }

                AddUserPassButton.Enabled = true;
                AddCookieButton.Enabled = true;
            }
            else
                MessageBox.Show("O navegador Chromium não está disponível.\nAguarde o download automático ou reinstale o aplicativo.", "Navegador Indisponível", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void DownloadProgressBar_Click(object sender, EventArgs e)
        {
            static void ShowManualInstallInstructions()
            {
                string Temp = Path.Combine(Path.GetTempPath(), "manual install instructions.html");

                string DownloadLink = PuppeteerSupported ? (string)typeof(BrowserFetcher).GetMethod("GetDownloadURL", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { AccountBrowser.Fetcher.Product, AccountBrowser.Fetcher.Platform, AccountBrowser.Fetcher.DownloadHost, BrowserFetcher.DefaultChromiumRevision }) : Resources.CefSharpDownload;
                string Directory = PuppeteerSupported ? Path.Combine(AccountBrowser.Fetcher.DownloadsFolder, $"{AccountBrowser.Fetcher.Platform}-{BrowserFetcher.DefaultChromiumRevision}") : Path.Combine(Environment.CurrentDirectory);

                File.WriteAllText(Temp, string.Format(Resources.ManualInstallHTML, PuppeteerSupported ? "Chromium" : "CefSharp", DownloadLink, PuppeteerSupported ? "chrome-win" : "x86", Directory));

                Process.Start(new ProcessStartInfo(Temp) { UseShellExecute = true });
                Process.Start(new ProcessStartInfo("cmd") { Arguments = $"/c mkdir \"{Directory}\"", CreateNoWindow = true });
            }

            if (TaskDialog.IsPlatformSupported)
            {
                TaskDialog Dialog = new TaskDialog()
                {
                    Caption = "Add Account",
                    InstructionText = $"{(PuppeteerSupported ? "Chromium" : "CefSharp")} is still being downloaded",
                    Text = "If this is not working for you, you can choose to manually install",
                    Icon = TaskDialogStandardIcon.Information
                };

                TaskDialogButton Manual = new TaskDialogButton("Manual", "Download Manually");
                TaskDialogButton Wait = new TaskDialogButton("Wait", "Wait");

                Wait.Click += (s, e) => Dialog.Close();
                Manual.Click += (s, e) =>
                {
                    Dialog.Close();

                    ShowManualInstallInstructions();
                };

                Dialog.Controls.Add(Manual);
                Dialog.Controls.Add(Wait);
                Wait.Default = true;

                Dialog.Show();
            }
            else if (MessageBox.Show($"{(PuppeteerSupported ? "Chromium" : "CefSharp")} is still downloading, you may have to wait a while before adding an account.\n\nNot working? You can choose to manually install by pressing \"Yes\"", "Roblox Account Manager", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) == DialogResult.Yes)
                ShowManualInstallInstructions();
        }

        private void DLChromiumLabel_Click(object sender, EventArgs e) => DownloadProgressBar_Click(sender, e);

        private void manualToolStripMenuItem_Click(object sender, EventArgs e) => AddUserPassButton.PerformClick();

        private void addAccountsToolStripMenuItem_Click(object sender, EventArgs e) => AddUserPassButton.PerformClick();

        private async void updateRobuxToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                AddLog($"🔄 Buscando saldo de Robux para {SelectedAccount.Username}...");
                
                // Buscar quantidade de Robux via API
                long robuxAmount = await GetAccountRobuxAsync(SelectedAccount);
                
                if (robuxAmount == 0)
                {
                    AddLogWarning($"⚠️ Não foi possível obter saldo de Robux para {SelectedAccount.Username}");
                    return;
                }

                // Atualizar no Supabase - buscar o jogo "ROBUX" e o item "ROBUX"
                int robuxGameId = 0;
                try
                {
                    var games = await SupabaseManager.Instance.GetGamesAsync();
                    var robuxGame = games?.FirstOrDefault(g => g.Name.Equals("ROBUX", StringComparison.OrdinalIgnoreCase));
                    
                    if (robuxGame != null)
                    {
                        robuxGameId = robuxGame.Id;
                        var items = await SupabaseManager.Instance.GetGameItemsAsync(robuxGame.Id);
                        var robuxItem = items?.FirstOrDefault(i => i.Name.Equals("ROBUX", StringComparison.OrdinalIgnoreCase));
                        
                        if (robuxItem != null)
                        {
                            await SupabaseManager.Instance.UpsertInventoryAsync(
                                SelectedAccount.Username,
                                robuxItem.Id,
                                robuxAmount
                            );
                            AddLog($"💾 Supabase atualizado: {SelectedAccount.Username} = {robuxAmount} R$");
                        }
                        else
                        {
                            AddLogWarning($"⚠️ Item 'ROBUX' não encontrado no jogo ROBUX do Supabase");
                        }
                    }
                    else
                    {
                        AddLogWarning($"⚠️ Jogo 'ROBUX' não encontrado no Supabase");
                    }
                }
                catch (Exception supaEx)
                {
                    AddLogWarning($"⚠️ Erro ao atualizar Supabase: {supaEx.Message}");
                }
                
                AddLogSuccess($"✅ Saldo atualizado: {SelectedAccount.Username} = {robuxAmount} R$");
                
                // Atualizar painel de INVENTÁRIO se estiver visualizando o jogo ROBUX
                if (robuxGameId > 0)
                {
                    _inventoryPanel?.RefreshIfCurrentGame(robuxGameId);
                }

                // Atualizar painel de ESTOQUE para refletir o novo saldo
                _ = LoadProductsFromSupabaseAsync(SelectedAccount.Username);
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro ao atualizar saldo: {ex.Message}");
            }
        }

        /// <summary>
        /// Busca o saldo de Robux de uma conta via API do Roblox
        /// </summary>
        private async Task<long> GetAccountRobuxAsync(Account account)
        {
            try
            {
                var request = new RestRequest($"v1/users/{account.UserID}/currency", Method.Get);
                request.AddHeader("Cookie", $".ROBLOSECURITY={account.SecurityToken}");

                var response = await EconClient.ExecuteAsync(request);

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var data = JObject.Parse(response.Content);
                    return data["robux"]?.Value<long>() ?? 0;
                }
            }
            catch (Exception ex)
            {
                AddLogWarning($"⚠️ Erro ao buscar Robux: {ex.Message}");
            }

            return 0;
        }

        private void byCookieToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ImportAccountsForm.Show();
            ImportAccountsForm.WindowState = FormWindowState.Normal;
            ImportAccountsForm.BringToFront();
        }

        private void AddCookieButton_Click(object sender, EventArgs e)
        {
            ImportAccountsForm.Show();
            ImportAccountsForm.WindowState = FormWindowState.Normal;
            ImportAccountsForm.BringToFront();
        }

        private async void AddUserPassButton_Click(object sender, EventArgs e)
        {
            // Método original com browser
            string Combos = ShowDialog("Separate the accounts with new lines\nMust be in user:pass form", "Import by User:Pass", big: true);

            if (Combos == "/UC") return;

            List<string> ComboList = new List<string>(Combos.Split('\n'));

            var Size = new System.Numerics.Vector2(455, 485);
            AccountBrowser.CreateGrid(Size);

            for (int i = 0; i < ComboList.Count; i++)
            {
                string Combo = ComboList[i];

                if (!Combo.Contains(':')) continue;

                var LoginTask = new AccountBrowser() { Index = i, Size = Size }.Login(Combo.Substring(0, Combo.IndexOf(':')), Combo.Substring(Combo.IndexOf(":") + 1));

                if ((i + 1) % 2 == 0) await LoginTask;
            }
        }


        private async void bulkUserPassToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Método original com browser
            string Combos = ShowDialog("Separate the accounts with new lines\nMust be in user:pass form", "Import by User:Pass", big: true);

            if (Combos == "/UC") return;

            List<string> ComboList = new List<string>(Combos.Split('\n'));

            var Size = new System.Numerics.Vector2(455, 485);
            AccountBrowser.CreateGrid(Size);

            for (int i = 0; i < ComboList.Count; i++)
            {
                string Combo = ComboList[i];

                if (!Combo.Contains(':')) continue;

                var LoginTask = new AccountBrowser() { Index = i, Size = Size }.Login(Combo.Substring(0, Combo.IndexOf(':')), Combo.Substring(Combo.IndexOf(":") + 1));

                if ((i + 1) % 2 == 0) await LoginTask;
            }
        }

        private void AccountsView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (AccountsView.SelectedItems.Count != 1)
            {
                SelectedAccount = null;
                SelectedAccountItem = null;

                if (AccountsView.SelectedObjects.Count > 1)
                    SelectedAccounts = AccountsView.SelectedObjects.Cast<Account>().ToList();

                return;
            }

            SelectedAccount = AccountsView.SelectedObject as Account;
            SelectedAccountItem = AccountsView.SelectedItem;

            if (SelectedAccount == null) return;

            AccountsView.HideSelection = false;


            if (!string.IsNullOrEmpty(SelectedAccount.GetField("SavedPlaceId"))) PlaceID.Text = SelectedAccount.GetField("SavedPlaceId");
            if (!string.IsNullOrEmpty(SelectedAccount.GetField("SavedJobId"))) JobID.Text = SelectedAccount.GetField("SavedJobId");

            // Carregar estoque da conta selecionada (Supabase)
            _ = LoadProductsFromSupabaseAsync(SelectedAccount.Username);
        }

        #region Estoque Panel (Supabase)

        private CancellationTokenSource _estoqueCts;
        private string _lastLoadedUsernameSupabase = "";
        private Dictionary<string, CancellationTokenSource> _supabaseEstoqueDebounce = new Dictionary<string, CancellationTokenSource>();

        private async Task LoadProductsFromSupabaseAsync(string username)
        {
            _estoqueCts?.Cancel();
            var cts = new CancellationTokenSource();
            _estoqueCts = cts;

            try
            {
                if (string.IsNullOrEmpty(username))
                {
                    ClearEstoquePanel();
                    return;
                }

                _lastLoadedUsernameSupabase = username;
                EstoqueUserLabel.Text = username;
                EstoqueItemsPanel.Controls.Clear();

                var inventoryTask = SupabaseManager.Instance.GetInventoryByUsernameAsync(username);
                var gamesTask = SupabaseManager.Instance.GetGamesAsync();
                var allItemsTask = SupabaseManager.Instance.GetAllGameItemsAsync();

                await Task.WhenAll(inventoryTask, gamesTask, allItemsTask);
                if (cts.Token.IsCancellationRequested) return;

                var inventory = inventoryTask.Result;
                var games = gamesTask.Result ?? new List<SupabaseGame>();
                var allItems = allItemsTask.Result;

                if (inventory == null || inventory.Count == 0)
                {
                    EstoqueGameLabel.Text = "SEM ESTOQUE";
                    var noItemsLabel = new Label
                    {
                        Text = "Nenhum produto",
                        ForeColor = Color.Gray,
                        Font = new Font("Segoe UI", 8F),
                        Size = new Size(210, 20),
                        TextAlign = ContentAlignment.MiddleCenter
                    };
                    EstoqueItemsPanel.Controls.Add(noItemsLabel);
                    return;
                }

                var gameDict = games.ToDictionary(g => g.Id, g => g);
                var itemToGame = new Dictionary<int, int>();
                var itemNames = new Dictionary<int, string>();

                foreach (var item in allItems)
                {
                    itemToGame[item.Id] = item.GameId;
                    itemNames[item.Id] = item.Name;
                }

                var groupedByGame = inventory
                    .Where(inv => itemToGame.ContainsKey(inv.ItemId))
                    .GroupBy(inv => itemToGame[inv.ItemId])
                    .OrderBy(g => gameDict.ContainsKey(g.Key) ? gameDict[g.Key].Name : "")
                    .ToList();

                EstoqueGameLabel.Text = groupedByGame.Count == 1
                    ? (gameDict.ContainsKey(groupedByGame[0].Key) ? gameDict[groupedByGame[0].Key].Name.ToUpper() : "JOGO")
                    : $"{groupedByGame.Count} JOGOS";

                int headerWidth = EstoquePanel.Width - 25;
                if (headerWidth < 120) headerWidth = 200;

                EstoqueItemsPanel.Controls.Clear();

                foreach (var gameGroup in groupedByGame)
                {
                    var gameObj = gameDict.ContainsKey(gameGroup.Key) ? gameDict[gameGroup.Key] : null;
                    string gameName = gameObj?.Name ?? "Jogo Desconhecido";

                    var gameHeader = new Label
                    {
                        Text = gameName.ToUpper(),
                        Font = new Font("Segoe UI", 7F, FontStyle.Bold),
                        ForeColor = Color.LimeGreen,
                        BackColor = ThemeEditor.ItemBackground,
                        Size = new Size(headerWidth, 18),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Margin = new Padding(0, 4, 0, 2),
                        Cursor = Cursors.Hand
                    };

                    // Ao clicar no header do jogo, preencher PlaceID
                    if (gameObj != null && gameObj.PlaceId.HasValue && gameObj.PlaceId.Value > 0)
                    {
                        long pid = gameObj.PlaceId.Value;
                        gameHeader.Click += (s, e) =>
                        {
                            PlaceID.Text = pid.ToString();
                            JobID.Text = "";
                            AddLog($"🎮 PlaceID preenchido: {pid} ({gameName})");
                        };
                    }

                    EstoqueItemsPanel.Controls.Add(gameHeader);

                    foreach (var inv in gameGroup.OrderBy(i => itemNames.ContainsKey(i.ItemId) ? itemNames[i.ItemId] : ""))
                    {
                        string itemName = itemNames.ContainsKey(inv.ItemId) ? itemNames[inv.ItemId] : $"Item {inv.ItemId}";
                        var itemPanel = CreateSupabaseEstoqueItemPanel(inv, itemName);
                        EstoqueItemsPanel.Controls.Add(itemPanel);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AddLogError($"[Estoque] Erro: {ex.Message}");
            }
        }

        private Panel CreateSupabaseEstoqueItemPanel(SupabaseInventoryEntry inventory, string itemName)
        {
            int panelWidth = EstoquePanel.Width - 25;
            if (panelWidth < 120) panelWidth = 200;

            var panel = new Panel
            {
                Size = new Size(panelWidth, 24),
                BackColor = ThemeEditor.PanelBackground,
                Margin = new Padding(0, 1, 0, 0)
            };

            var nameLabel = new Label
            {
                Text = itemName,
                Font = new Font("Segoe UI", 7F),
                ForeColor = ThemeEditor.FormsForeground,
                Location = new System.Drawing.Point(2, 4),
                Size = new Size(panelWidth - 120, 16),
                AutoEllipsis = true
            };

            var minusBtn = new Button
            {
                Text = "-",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(80, 40, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(22, 20),
                Location = new System.Drawing.Point(panelWidth - 118, 2),
                Tag = inventory,
                Cursor = Cursors.Hand
            };
            minusBtn.FlatAppearance.BorderSize = 0;
            minusBtn.Click += SupabaseEstoqueMinus_Click;

            var qtyTextBox = new TextBox
            {
                Text = FormatNumberWithThousands(inventory.Quantity),
                Name = $"estoqueqty_{inventory.Id}",
                Font = new Font("Segoe UI", 7.5F),
                ForeColor = ThemeEditor.TextBoxesForeground,
                BackColor = ThemeEditor.HeaderBackground,
                BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Center,
                Size = new Size(68, 16),
                Location = new System.Drawing.Point(panelWidth - 94, 4),
                Tag = inventory
            };
            qtyTextBox.KeyDown += SupabaseEstoqueQty_KeyDown;
            qtyTextBox.Leave += SupabaseEstoqueQty_Leave;

            var plusBtn = new Button
            {
                Text = "+",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 80, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(22, 20),
                Location = new System.Drawing.Point(panelWidth - 24, 2),
                Tag = inventory,
                Cursor = Cursors.Hand
            };
            plusBtn.FlatAppearance.BorderSize = 0;
            plusBtn.Click += SupabaseEstoquePlus_Click;

            panel.Controls.AddRange(new Control[] { nameLabel, minusBtn, qtyTextBox, plusBtn });
            return panel;
        }

        private void SupabaseEstoqueMinus_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var inventory = btn?.Tag as SupabaseInventoryEntry;
            if (inventory == null) return;

            if (inventory.Quantity > 0) inventory.Quantity--;

            var parent = btn.Parent as Panel;
            var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("estoqueqty_"));
            if (qtyTextBox != null) qtyTextBox.Text = FormatNumberWithThousands(inventory.Quantity);

            ScheduleSupabaseEstoqueUpdate(inventory);
        }

        private void SupabaseEstoquePlus_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var inventory = btn?.Tag as SupabaseInventoryEntry;
            if (inventory == null) return;

            inventory.Quantity++;

            var parent = btn.Parent as Panel;
            var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("estoqueqty_"));
            if (qtyTextBox != null) qtyTextBox.Text = FormatNumberWithThousands(inventory.Quantity);

            ScheduleSupabaseEstoqueUpdate(inventory);
        }

        private void SupabaseEstoqueQty_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SaveSupabaseEstoqueQty(sender as TextBox);
            }
        }

        private void SupabaseEstoqueQty_Leave(object sender, EventArgs e)
        {
            SaveSupabaseEstoqueQty(sender as TextBox);
        }

        private void SaveSupabaseEstoqueQty(TextBox textBox)
        {
            if (textBox == null) return;
            var inventory = textBox.Tag as SupabaseInventoryEntry;
            if (inventory == null) return;

            long newQty = ParseAbbreviatedNumber(textBox.Text);
            if (newQty < 0) newQty = 0;

            if (newQty != inventory.Quantity)
            {
                inventory.Quantity = newQty;
                ScheduleSupabaseEstoqueUpdate(inventory);
            }

            textBox.Text = FormatNumberWithThousands(inventory.Quantity);
        }

        private void ScheduleSupabaseEstoqueUpdate(SupabaseInventoryEntry inventory)
        {
            string key = $"supa_{inventory.Id}";
            if (_supabaseEstoqueDebounce.ContainsKey(key))
            {
                _supabaseEstoqueDebounce[key].Cancel();
                _supabaseEstoqueDebounce[key].Dispose();
            }
            var cts = new CancellationTokenSource();
            _supabaseEstoqueDebounce[key] = cts;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, cts.Token);
                    if (!cts.Token.IsCancellationRequested)
                    {
                        await SupabaseManager.Instance.UpdateInventoryQuantityAsync(inventory.Id, inventory.Quantity);
                        Classes.InventorySyncService.Instance.MarkLocalUpdate(inventory.Id);
                        AddLog($"✅ Estoque atualizado: {inventory.Quantity}");

                        // Atualizar painel de inventário também
                        _inventoryPanel?.UpdateInventoryQuantity(inventory.Id, inventory.Quantity);
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    AddLogError($"Erro ao salvar estoque: {ex.Message}");
                }
                finally
                {
                    if (_supabaseEstoqueDebounce.ContainsKey(key))
                        _supabaseEstoqueDebounce.Remove(key);
                }
            });
        }

        /// <summary>
        /// Chamado pelo InventorySyncService quando outro usuário atualizou inventário.
        /// Roda no thread da UI (WinForms Timer).
        /// </summary>
        private void OnInventoryChangedFromSync(object sender, System.Collections.Generic.List<SupabaseInventoryEntry> changedEntries)
        {
            if (DebugModeAtivo)
                AddLog($"🔄 [Sync] {changedEntries.Count} alterações recebidas de outro usuário");

            // 1. Atualizar EstoquePanel (por conta)
            if (!string.IsNullOrEmpty(_lastLoadedUsernameSupabase))
            {
                foreach (var entry in changedEntries)
                {
                    if (!entry.Username.Equals(_lastLoadedUsernameSupabase, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Buscar TextBox no EstoqueItemsPanel pelo nome
                    foreach (Control panel in EstoqueItemsPanel.Controls)
                    {
                        if (panel is Panel p)
                        {
                            foreach (Control child in p.Controls)
                            {
                                if (child is TextBox tb && tb.Name == $"estoqueqty_{entry.Id}")
                                {
                                    tb.Text = FormatNumberWithThousands(entry.Quantity);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // 2. Encaminhar para InventoryPanelControl
            foreach (var entry in changedEntries)
            {
                _inventoryPanel?.UpdateInventoryQuantity(entry.Id, entry.Quantity);
            }
        }

        private void ClearEstoquePanel()
        {
            EstoqueUserLabel.Text = "Selecione uma conta";
            EstoqueGameLabel.Text = "";
            EstoqueItemsPanel.Controls.Clear();
        }

        private void EstoqueRefreshButton_Click(object sender, EventArgs e)
        {
            if (SelectedAccount != null)
            {
                _lastLoadedUsernameSupabase = "";
                _ = LoadProductsFromSupabaseAsync(SelectedAccount.Username);
                AddLog("🔄 Atualizando estoque...");
            }
        }

        #endregion

        #region Inventory Panel (Supabase)

        /// <summary>
        /// Inicializa o painel de inventário usando Supabase
        /// </summary>
        private void InitializeInventoryPanel()
        {
            AddLog("🎮 [Inventory] Inicializando painel de inventário...");

            // Criar o controle de inventário
            _inventoryPanel = new Controls.InventoryPanelControl();
            _inventoryPanel.Dock = DockStyle.Fill;

            // Iniciar serviço de sincronização de inventário
            Classes.InventorySyncService.Instance.Start();
            Classes.InventorySyncService.Instance.InventoryEntriesChanged += OnInventoryChangedFromSync;

            // Configurar eventos de log
            _inventoryPanel.LogMessage += (s, msg) => AddLog(msg);
            _inventoryPanel.LogWarning += (s, msg) => AddLog($"⚠️ {msg}");
            _inventoryPanel.LogError += (s, msg) => AddLogError(msg);
            
            // Quando clicar numa conta no inventário, selecionar no AccountManager
            _inventoryPanel.AccountSelected += async (s, username) => {
                AddLog($"🔍 Buscando conta: {username}");
                
                // Encontrar a conta na lista local
                var account = AccountsList.FirstOrDefault(a => 
                    a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                
                if (account != null)
                {
                    // Conta já existe - apenas selecionar
                    // O LoadProductsFromSupabaseAsync será chamado pelo AccountsView_SelectionChanged
                    SelectAccountInView(account);
                }
                else
                {
                    // Conta não existe - buscar no Supabase e adicionar
                    AddLog($"⚠️ Conta não encontrada localmente, buscando no Supabase...");
                    
                    try
                    {
                        var supabaseAccounts = await SupabaseManager.Instance.GetAccountsAsync();
                        var supabaseAccount = supabaseAccounts?.FirstOrDefault(a => 
                            a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                        
                        if (supabaseAccount != null && !string.IsNullOrEmpty(supabaseAccount.Cookie))
                        {
                            AddLog($"☁️ Conta encontrada no Supabase, adicionando...");
                            
                            // Criar nova conta com o cookie do Supabase
                            var newAccount = new Account(supabaseAccount.Cookie);
                            
                            // Verificar se o cookie é válido
                            if (!string.IsNullOrEmpty(newAccount.Username))
                            {
                                AccountsList.Add(newAccount);
                                RefreshView();
                                SaveAccounts();
                                
                                AddLog($"✅ Conta '{newAccount.Username}' adicionada com sucesso!");
                                
                                // Selecionar a conta recém-adicionada
                                // O LoadProductsFromSupabaseAsync será chamado pelo AccountsView_SelectionChanged
                                SelectAccountInView(newAccount);
                            }
                            else
                            {
                                AddLog($"❌ Cookie inválido para '{username}'");
                            }
                        }
                        else
                        {
                            AddLog($"❌ Conta '{username}' não encontrada no Supabase ou sem cookie");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLogError($"Erro ao buscar conta: {ex.Message}");
                    }
                }
            };

            // Substituir o conteúdo do GameSelectorPanel pelo novo painel
            GameSelectorPanel.SuspendLayout();
            GameSelectorPanel.Controls.Clear();
            GameSelectorPanel.Controls.Add(_inventoryPanel);
            GameSelectorPanel.ResumeLayout(true);

            // Carregar jogos e contas
            _ = _inventoryPanel.InitializeAsync();

            AddLog("✅ [Inventory] Painel de inventário inicializado");
        }

        /// <summary>
        /// Seleciona uma conta na AccountsView
        /// </summary>
        private void SelectAccountInView(Account account)
        {
            if (account == null) return;
            
            try
            {
                // Limpar seleção atual
                AccountsView.DeselectAll();
                
                // Encontrar o índice da conta no ObjectListView
                int index = -1;
                for (int i = 0; i < AccountsView.GetItemCount(); i++)
                {
                    var item = AccountsView.GetModelObject(i) as Account;
                    if (item != null && item.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
                
                if (index >= 0)
                {
                    // Selecionar usando ObjectListView
                    AccountsView.SelectObject(account);
                    AccountsView.EnsureModelVisible(account);
                    
                    // Disparar seleção
                    SelectedAccount = account;
                    
                    // Focar na lista
                    AccountsView.Focus();
                    
                    AddLog($"📌 Conta selecionada: {account.Username}");
                }
                else
                {
                    AddLog($"⚠️ Conta não encontrada na lista: {account.Username}");
                }
            }
            catch (Exception ex)
            {
                AddLogError($"Erro ao selecionar conta: {ex.Message}");
            }
        }

        /// <summary>
        /// Sincroniza contas locais para o Supabase
        /// </summary>
        public async Task SyncAccountsToSupabaseAsync()
        {
            AddLog("☁️ Sincronizando contas para o Supabase...");
            
            var localAccounts = AccountsList.Select(a => (
                username: a.Username,
                cookie: a.SecurityToken,
                userId: (long?)a.UserID
            )).ToList();

            int synced = await Classes.SupabaseManager.Instance.SyncAccountsToCloudAsync(localAccounts);
            
            AddLog($"✅ {synced} contas sincronizadas para a nuvem");
        }

        /// <summary>
        /// Baixa contas do Supabase que não existem localmente
        /// </summary>
        public async Task<List<Classes.SupabaseAccount>> GetNewAccountsFromSupabaseAsync()
        {
            var localUsernames = AccountsList.Select(a => a.Username).ToList();
            return await Classes.SupabaseManager.Instance.GetNewAccountsFromCloudAsync(localUsernames);
        }

        #endregion

        #region Painel de Amigos
        
        private List<FriendItemData> _friendsList = new List<FriendItemData>();
        private bool _isLoadingFriends = false;
        private long _lastSelectedAccountId = 0;
        
        private class FriendItemData
        {
            public long UserId { get; set; }
            public string Username { get; set; }
            public string DisplayName { get; set; }
            public int PresenceType { get; set; }
            public string GameName { get; set; }
            public long PlaceId { get; set; }
            public string JobId { get; set; }
        }

        private void InitializeFriendsPanel()
        {
            AddLog("🔄 [FriendsPanel] Configurando painel de amigos no panel4...");
            
            // panel4 tem tamanho 1074x124
            // Criar FlowLayoutPanel para lista de amigos
            FriendsListPanel = new FlowLayoutPanel();
            FriendsListPanel.Location = new System.Drawing.Point(3, 3);
            FriendsListPanel.Size = new System.Drawing.Size(940, 118);
            FriendsListPanel.AutoScroll = true;
            FriendsListPanel.FlowDirection = FlowDirection.LeftToRight;
            FriendsListPanel.WrapContents = false;
            FriendsListPanel.BackColor = ThemeEditor.FormsBackground;
            FriendsListPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; // Ancoragem
            
            // Adicionar ao panel4
            panel4.Controls.Add(FriendsListPanel);
            panel4.BackColor = ThemeEditor.FormsBackground;
            panel4.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; // Ancoragem do panel4
            FriendsListPanel.BringToFront();
            
            // Conectar button4 (Atualizar Lista) ao evento
            button4.Click += FriendsRefreshButton_Click;
            
            // Conectar ADDAMIGO ao evento
            ADDAMIGO.Click += FriendsAddButton_Click;
            
            // Conectar button1 (Aceitar Todos) ao evento
            button1.Click += AcceptAllFriendRequests_Click;
            
            // Usar textBox1 como campo de username
            FriendsAddTextBox = textBox1;
            
            // Usar label5 como status
            FriendsStatusLabel = label5;
            FriendsStatusLabel.Text = "";
            FriendsStatusLabel.ForeColor = System.Drawing.Color.White;
            
            // Conectar evento de seleção de conta para atualizar amigos automaticamente
            AccountsView.SelectedIndexChanged += AccountsView_SelectedIndexChanged_Friends;
            
            // Carregar e registrar hotkey de AddFriend das configurações
            string savedAddFriendHotkey = General.Get<string>("AddFriendHotkey");
            if (string.IsNullOrEmpty(savedAddFriendHotkey)) savedAddFriendHotkey = "Ctrl+Shift+V";
            UpdateAddFriendHotkey(savedAddFriendHotkey);
            
            // Carregar Debug Mode
            DebugModeAtivo = General.Get<bool>("DebugMode");
            
            AddLog("✅ [FriendsPanel] Painel de amigos configurado no panel4");
            AddLog($"ℹ️ [FriendsPanel] Hotkey Add Friend: {savedAddFriendHotkey}");
        }
        
        private async void AcceptAllFriendRequests_Click(object sender, EventArgs e)
        {
            if (AccountsView.SelectedItem == null)
            {
                FriendsStatusLabel.Text = "Selecione uma conta";
                return;
            }

            var account = AccountsView.SelectedItem.RowObject as Account;
            if (account == null) return;

            button1.Enabled = false;
            FriendsStatusLabel.Text = "Buscando pedidos...";
            AddLog("🔄 [FriendsPanel] Buscando pedidos de amizade...");

            try
            {
                // Buscar pedidos de amizade pendentes
                var client = FriendsClient;
                var request = new RestSharp.RestRequest("/v1/my/friends/requests", RestSharp.Method.Get);
                request.AddHeader("Cookie", $".ROBLOSECURITY={account.SecurityToken}");

                var response = await client.ExecuteAsync(request);
                
                if (!response.IsSuccessful)
                {
                    FriendsStatusLabel.Text = "Erro ao buscar";
                    AddLogError($"❌ [FriendsPanel] Erro: {response.StatusCode}");
                    button1.Enabled = true;
                    return;
                }

                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(response.Content);
                var requests = data?["data"]?.ToObject<List<Newtonsoft.Json.Linq.JObject>>();

                if (requests == null || requests.Count == 0)
                {
                    FriendsStatusLabel.Text = "0 pedidos";
                    AddLog("✅ [FriendsPanel] Nenhum pedido pendente");
                    button1.Enabled = true;
                    return;
                }

                AddLog($"🔄 [FriendsPanel] {requests.Count} pedidos encontrados");
                FriendsStatusLabel.Text = $"Aceitando {requests.Count}...";

                // Obter CSRF token
                string csrfToken = "";
                var csrfClient = FriendsClient;
                var csrfRequest = new RestSharp.RestRequest("/v1/users/1/request-friendship", RestSharp.Method.Post);
                csrfRequest.AddHeader("Cookie", $".ROBLOSECURITY={account.SecurityToken}");
                var csrfResponse = await csrfClient.ExecuteAsync(csrfRequest);
                if (csrfResponse.Headers != null)
                {
                    var csrfHeader = csrfResponse.Headers.FirstOrDefault(h => h.Name?.ToLower() == "x-csrf-token");
                    if (csrfHeader != null)
                        csrfToken = csrfHeader.Value?.ToString() ?? "";
                }

                int accepted = 0;
                foreach (var req in requests)
                {
                    try
                    {
                        var userId = req["id"]?.Value<long>() ?? 0;
                        if (userId == 0) continue;

                        var acceptClient = FriendsClient;
                        var acceptRequest = new RestSharp.RestRequest($"/v1/users/{userId}/accept-friend-request", RestSharp.Method.Post);
                        acceptRequest.AddHeader("Cookie", $".ROBLOSECURITY={account.SecurityToken}");
                        acceptRequest.AddHeader("x-csrf-token", csrfToken);

                        var acceptResponse = await acceptClient.ExecuteAsync(acceptRequest);
                        
                        if (acceptResponse.IsSuccessful)
                        {
                            accepted++;
                            AddLog($"✅ [FriendsPanel] Aceito: {req["name"]}");
                        }
                        else if ((int)acceptResponse.StatusCode == 403)
                        {
                            // Atualizar CSRF token
                            var newCsrfHeader = acceptResponse.Headers?.FirstOrDefault(h => h.Name?.ToLower() == "x-csrf-token");
                            if (newCsrfHeader != null)
                            {
                                csrfToken = newCsrfHeader.Value?.ToString() ?? "";
                                // Tentar novamente
                                acceptRequest.AddOrUpdateHeader("x-csrf-token", csrfToken);
                                acceptResponse = await acceptClient.ExecuteAsync(acceptRequest);
                                if (acceptResponse.IsSuccessful)
                                {
                                    accepted++;
                                    AddLog($"✅ [FriendsPanel] Aceito: {req["name"]}");
                                }
                            }
                        }
                        
                        await Task.Delay(100); // Pequeno delay para não sobrecarregar
                    }
                    catch { continue; }
                }

                FriendsStatusLabel.Text = $"{accepted} aceitos!";
                AddLog($"✅ [FriendsPanel] {accepted} pedidos aceitos");
                
                // Atualizar lista de amigos
                await LoadFriendsForSelectedAccount();
            }
            catch (Exception ex)
            {
                FriendsStatusLabel.Text = "Erro";
                AddLogError($"❌ [FriendsPanel] Erro: {ex.Message}");
            }

            button1.Enabled = true;
        }
        
        private async void AccountsView_SelectedIndexChanged_Friends(object sender, EventArgs e)
        {
            // Atualizar lista de amigos automaticamente quando selecionar uma conta
            await LoadFriendsForSelectedAccount();
        }

        private async void FriendsRefreshButton_Click(object sender, EventArgs e)
        {
            AddLog("🔄 [FriendsPanel] Botão Atualizar Lista clicado");
            _lastSelectedAccountId = 0; // Forçar atualização
            await LoadFriendsForSelectedAccount();
        }

        private async Task LoadFriendsForSelectedAccount()
        {
            // Evitar chamadas duplicadas
            if (_isLoadingFriends) return;
            
            if (AccountsView.SelectedItem == null)
            {
                return; // Silenciosamente ignorar se não houver conta selecionada
            }

            var account = AccountsView.SelectedItem.RowObject as Account;
            if (account == null) return;
            
            // Evitar recarregar a mesma conta
            if (_lastSelectedAccountId == account.UserID && FriendsListPanel.Controls.Count > 0)
            {
                return;
            }
            
            _isLoadingFriends = true;
            _lastSelectedAccountId = account.UserID;
            
            AddLog($"🔄 [FriendsPanel] Carregando amigos para: {account.Username} (ID: {account.UserID})");

            button4.Enabled = false;
            FriendsStatusLabel.Text = "Carregando...";
            FriendsListPanel.Controls.Clear();
            _friendsList.Clear();

            try
            {
                // 1. Buscar lista de amigos
                AddLog($"🔄 [FriendsPanel] Buscando amigos...");
                var friendsClient = FriendsClient;
                var friendsRequest = new RestSharp.RestRequest($"/v1/users/{account.UserID}/friends", RestSharp.Method.Get);
                friendsRequest.AddHeader("Cookie", $".ROBLOSECURITY={account.SecurityToken}");

                var friendsResponse = await friendsClient.ExecuteAsync(friendsRequest);
                AddLog($"🔄 [FriendsPanel] Resposta amigos: {friendsResponse.StatusCode}");

                if (!friendsResponse.IsSuccessful)
                {
                    AddLogError($"❌ [FriendsPanel] Erro HTTP: {friendsResponse.StatusCode} - {friendsResponse.ErrorMessage}");
                    FriendsStatusLabel.Text = "Erro ao carregar amigos";
                    button4.Enabled = true;
                    return;
                }

                var friendsData = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(friendsResponse.Content);
                var friendsList = friendsData?["data"]?.ToObject<List<Newtonsoft.Json.Linq.JObject>>();
                
                AddLog($"🔄 [FriendsPanel] Amigos encontrados: {friendsList?.Count ?? 0}");

                if (friendsList == null || friendsList.Count == 0)
                {
                    FriendsStatusLabel.Text = "Nenhum amigo encontrado";
                    button4.Enabled = true;
                    return;
                }

                var friendIds = new List<long>();
                foreach (var f in friendsList)
                {
                    try
                    {
                        var idToken = f["id"];
                        if (idToken == null) continue;
                        
                        long id = 0;
                        if (idToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                            id = idToken.Value<long>();
                        else if (idToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                            long.TryParse(idToken.ToString(), out id);
                        
                        if (id > 0) friendIds.Add(id);
                    }
                    catch { continue; }
                }
                
                AddLog($"🔄 [FriendsPanel] IDs extraídos: {friendIds.Count}");

                // 2. Buscar presença
                var presenceClient = PresenceClient;
                var presenceRequest = new RestSharp.RestRequest("/v1/presence/users", RestSharp.Method.Post);
                presenceRequest.AddHeader("Cookie", $".ROBLOSECURITY={account.SecurityToken}");
                presenceRequest.AddHeader("Content-Type", "application/json");
                presenceRequest.AddJsonBody(new { userIds = friendIds });

                var presenceResponse = await presenceClient.ExecuteAsync(presenceRequest);
                var presenceData = presenceResponse.IsSuccessful
                    ? Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(presenceResponse.Content)?["userPresences"]?.ToObject<List<Newtonsoft.Json.Linq.JObject>>()
                    : null;

                // 3. Buscar usernames
                var usersClient = UsersClient;
                var usersRequest = new RestSharp.RestRequest("/v1/users", RestSharp.Method.Post);
                usersRequest.AddHeader("Content-Type", "application/json");
                usersRequest.AddJsonBody(new { userIds = friendIds, excludeBannedUsers = false });

                var usersResponse = await usersClient.ExecuteAsync(usersRequest);
                var usersData = usersResponse.IsSuccessful
                    ? Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(usersResponse.Content)?["data"]?.ToObject<List<Newtonsoft.Json.Linq.JObject>>()
                    : null;

                var userNames = new Dictionary<long, (string name, string displayName)>();
                if (usersData != null)
                {
                    foreach (var user in usersData)
                    {
                        try
                        {
                            var idToken = user["id"];
                            if (idToken == null) continue;
                            
                            long id = 0;
                            if (idToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                                id = idToken.Value<long>();
                            else if (idToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                                long.TryParse(idToken.ToString(), out id);
                            
                            if (id > 0)
                                userNames[id] = (user["name"]?.ToString() ?? "", user["displayName"]?.ToString() ?? "");
                        }
                        catch { continue; }
                    }
                }
                
                AddLog($"🔄 [FriendsPanel] Usernames carregados: {userNames.Count}");

                var presences = new Dictionary<long, (int type, string location, long placeId, string gameId)>();
                if (presenceData != null)
                {
                    foreach (var p in presenceData)
                    {
                        try
                        {
                            var idToken = p["userId"];
                            if (idToken == null) continue;
                            
                            long id = 0;
                            if (idToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                                id = idToken.Value<long>();
                            else if (idToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                                long.TryParse(idToken.ToString(), out id);
                            
                            if (id > 0)
                            {
                                int presenceType = 0;
                                var presenceToken = p["userPresenceType"];
                                if (presenceToken != null && presenceToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                                    presenceType = presenceToken.Value<int>();
                                
                                long placeId = 0;
                                var placeToken = p["placeId"];
                                if (placeToken != null && placeToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                                    placeId = placeToken.Value<long>();
                                
                                presences[id] = (
                                    presenceType,
                                    p["lastLocation"]?.ToString() ?? "",
                                    placeId,
                                    p["gameId"]?.ToString() ?? ""
                                );
                            }
                        }
                        catch { continue; }
                    }
                }

                AddLog($"🔄 [FriendsPanel] Presenças carregadas: {presences.Count}");

                // Construir lista
                foreach (var friend in friendsList)
                {
                    var userId = friend["id"]?.Value<long>() ?? 0;
                    if (userId == 0) continue;

                    _friendsList.Add(new FriendItemData
                    {
                        UserId = userId,
                        Username = userNames.ContainsKey(userId) ? userNames[userId].name : friend["name"]?.ToString() ?? "",
                        DisplayName = userNames.ContainsKey(userId) ? userNames[userId].displayName : friend["displayName"]?.ToString() ?? "",
                        PresenceType = presences.ContainsKey(userId) ? presences[userId].type : 0,
                        GameName = presences.ContainsKey(userId) ? presences[userId].location : "",
                        PlaceId = presences.ContainsKey(userId) ? presences[userId].placeId : 0,
                        JobId = presences.ContainsKey(userId) ? presences[userId].gameId : ""
                    });
                }

                // Ordenar: em jogo primeiro
                _friendsList = _friendsList.OrderByDescending(f => f.PresenceType == 2)
                                           .ThenByDescending(f => f.PresenceType == 1)
                                           .ThenBy(f => f.DisplayName)
                                           .ToList();

                AddLog($"🔄 [FriendsPanel] Lista ordenada. Em jogo: {_friendsList.Count(f => f.PresenceType == 2)}, Online: {_friendsList.Count(f => f.PresenceType == 1)}");

                // Criar controles visuais - mostrar amigos em jogo (2) e online (1)
                int inGameCount = 0;
                int onlineCount = 0;
                foreach (var friend in _friendsList.Where(f => f.PresenceType == 2 || f.PresenceType == 1).Take(20)) // Limitar a 20 para performance
                {
                    var itemPanel = CreateFriendItemPanel(friend, account);
                    FriendsListPanel.Controls.Add(itemPanel);
                    if (friend.PresenceType == 2)
                        inGameCount++;
                    else
                        onlineCount++;
                }

                AddLog($"✅ [FriendsPanel] Criados {inGameCount + onlineCount} painéis de amigos ({inGameCount} em jogo, {onlineCount} online)");
                FriendsStatusLabel.Text = $"{inGameCount} jogo, {onlineCount} online";
            }
            catch (Exception ex)
            {
                FriendsStatusLabel.Text = $"Erro";
                AddLogError($"❌ [FriendsPanel] Erro: {ex.Message}");
            }
            finally
            {
                _isLoadingFriends = false;
            }

            button4.Enabled = true;
        }

        private Panel CreateFriendItemPanel(FriendItemData friend, Account account)
        {
            // Layout: FOTO, NOME DE EXIBIÇÃO, USERNAME, JOGO, SEGUIR
            var panel = new Panel();
            panel.Size = new System.Drawing.Size(80, 112);
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.BackColor = ThemeEditor.PanelBackground;
            panel.Margin = new Padding(2, 0, 2, 0);

            // Avatar (no topo)
            var avatar = new PictureBox();
            avatar.Location = new System.Drawing.Point(14, 2);
            avatar.Size = new System.Drawing.Size(50, 42);
            avatar.SizeMode = PictureBoxSizeMode.StretchImage;
            avatar.BorderStyle = BorderStyle.FixedSingle;
            panel.Controls.Add(avatar);

            // Carregar avatar em background
            _ = LoadAvatarAsync(avatar, friend.UserId);

            // DisplayName (nome de exibição)
            var displayNameLabel = new Label();
            displayNameLabel.Text = friend.DisplayName?.Length > 9 ? friend.DisplayName.Substring(0, 9) + ".." : friend.DisplayName ?? "???";
            displayNameLabel.Font = new System.Drawing.Font("Segoe UI", 7.5F, System.Drawing.FontStyle.Bold);
            displayNameLabel.ForeColor = ThemeEditor.FormsForeground;
            displayNameLabel.BackColor = System.Drawing.Color.Transparent;
            displayNameLabel.Location = new System.Drawing.Point(1, 44);
            displayNameLabel.Size = new System.Drawing.Size(76, 14);
            displayNameLabel.TextAlign = ContentAlignment.MiddleCenter;
            panel.Controls.Add(displayNameLabel);

            // Username (@username)
            var usernameLabel = new Label();
            var username = friend.Username ?? friend.DisplayName ?? "???";
            usernameLabel.Text = "@" + (username.Length > 10 ? username.Substring(0, 10) + ".." : username);
            usernameLabel.Font = new System.Drawing.Font("Segoe UI", 6F);
            usernameLabel.ForeColor = ControlPaint.Dark(ThemeEditor.FormsForeground, 0.2f);
            usernameLabel.BackColor = System.Drawing.Color.Transparent;
            usernameLabel.Location = new System.Drawing.Point(1, 57);
            usernameLabel.Size = new System.Drawing.Size(76, 12);
            usernameLabel.TextAlign = ContentAlignment.MiddleCenter;
            panel.Controls.Add(usernameLabel);

            // Se está em jogo (PresenceType == 2), mostra jogo + botão SEGUIR
            if (friend.PresenceType == 2)
            {
                // Nome do jogo
                var gameLabel = new Label();
                var gameName = !string.IsNullOrEmpty(friend.GameName) ? friend.GameName : "Em jogo";
                gameLabel.Text = gameName.Length > 11 ? gameName.Substring(0, 11) + ".." : gameName;
                gameLabel.Font = new System.Drawing.Font("Segoe UI", 5.5F);
                gameLabel.ForeColor = System.Drawing.Color.FromArgb(67, 181, 129); // Verde
                gameLabel.BackColor = System.Drawing.Color.Transparent;
                gameLabel.Location = new System.Drawing.Point(1, 68);
                gameLabel.Size = new System.Drawing.Size(76, 11);
                gameLabel.TextAlign = ContentAlignment.MiddleCenter;
                panel.Controls.Add(gameLabel);

                // Botão Seguir
                var followBtn = new Button();
                followBtn.Text = "SEGUIR";
                followBtn.Font = new System.Drawing.Font("Segoe UI", 5.5F, System.Drawing.FontStyle.Bold);
                followBtn.Location = new System.Drawing.Point(3, 80);
                followBtn.Size = new System.Drawing.Size(72, 18);
                followBtn.Enabled = friend.PlaceId > 0 && !string.IsNullOrEmpty(friend.JobId);
                followBtn.BackColor = System.Drawing.Color.FromArgb(88, 101, 242);
                followBtn.ForeColor = System.Drawing.Color.White;
                followBtn.FlatStyle = FlatStyle.Flat;
                followBtn.FlatAppearance.BorderSize = 0;
                followBtn.Tag = friend;
                followBtn.Click += (s, e) =>
                {
                    var f = (FriendItemData)((Button)s).Tag;
                    if (f.PlaceId > 0 && !string.IsNullOrEmpty(f.JobId))
                    {
                        AddLog($"🎮 Seguindo {f.DisplayName} no jogo...");
                        account.JoinServer(f.PlaceId, f.JobId);
                        FriendsStatusLabel.Text = $"Entrando...";
                    }
                };
                panel.Controls.Add(followBtn);
            }
            else
            {
                // Amigo online mas não em jogo - mostra "Online" em azul
                var onlineLabel = new Label();
                onlineLabel.Text = "Online";
                onlineLabel.Font = new System.Drawing.Font("Segoe UI", 6.5F, System.Drawing.FontStyle.Bold);
                onlineLabel.ForeColor = System.Drawing.Color.FromArgb(88, 101, 242); // Azul
                onlineLabel.BackColor = System.Drawing.Color.Transparent;
                onlineLabel.Location = new System.Drawing.Point(1, 70);
                onlineLabel.Size = new System.Drawing.Size(76, 14);
                onlineLabel.TextAlign = ContentAlignment.MiddleCenter;
                panel.Controls.Add(onlineLabel);
            }

            return panel;
        }

        // Método InviteFriendToGame removido - API não funciona


        private async Task LoadAvatarAsync(PictureBox pictureBox, long userId)
        {
            try
            {
                var url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=50x50&format=Png";
                using (var client = new System.Net.WebClient())
                {
                    var json = await client.DownloadStringTaskAsync(url);
                    if (json.Contains("imageUrl"))
                    {
                        var startIndex = json.IndexOf("\"imageUrl\":\"") + 12;
                        var endIndex = json.IndexOf("\"", startIndex);
                        var imageUrl = json.Substring(startIndex, endIndex - startIndex);

                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            var imageData = await client.DownloadDataTaskAsync(imageUrl);
                            using (var ms = new System.IO.MemoryStream(imageData))
                            {
                                if (!pictureBox.IsDisposed)
                                {
                                    pictureBox.Invoke((MethodInvoker)delegate
                                    {
                                        pictureBox.Image = System.Drawing.Image.FromStream(ms);
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private async void FriendsAddButton_Click(object sender, EventArgs e)
        {
            var username = FriendsAddTextBox.Text.Trim();
            if (string.IsNullOrEmpty(username))
            {
                FriendsStatusLabel.Text = "Digite um username";
                return;
            }

            if (AccountsView.SelectedItem == null)
            {
                FriendsStatusLabel.Text = "Selecione uma conta primeiro";
                return;
            }

            var account = AccountsView.SelectedItem.RowObject as Account;
            if (account == null) return;

            ADDAMIGO.Enabled = false;
            FriendsStatusLabel.Text = "Buscando usuário...";

            try
            {
                var client = UsersClient;
                var request = new RestSharp.RestRequest("/v1/usernames/users", RestSharp.Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddJsonBody(new { usernames = new[] { username }, excludeBannedUsers = false });

                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(response.Content);
                    var users = data?["data"]?.ToObject<List<Newtonsoft.Json.Linq.JObject>>();

                    if (users != null && users.Count > 0)
                    {
                        var userId = users[0]["id"]?.Value<long>() ?? 0;
                        if (userId > 0)
                        {
                            // Usar navegador diretamente (API requer captcha)
                            var profileUrl = $"https://www.roblox.com/users/{userId}/profile";
                            AddLog($"🔄 [AddFriend] Abrindo perfil de {username} (ID: {userId})...");

                            new Classes.AccountBrowser(account, profileUrl, null, async (page) =>
                            {
                                try
                                {
                                    // Esperar página carregar
                                    await System.Threading.Tasks.Task.Delay(2000);
                                    
                                    // Esperar pelo botão usando seletor de ID
                                    try
                                    {
                                        await page.WaitForSelectorAsync("#user-profile-header-AddFriend", new PuppeteerSharp.WaitForSelectorOptions 
                                        { 
                                            Timeout = 10000,
                                            Visible = true
                                        });
                                        AddLog($"✅ [AddFriend] Botão encontrado via WaitForSelector");
                                    }
                                    catch
                                    {
                                        AddLog($"⚠️ [AddFriend] WaitForSelector timeout, tentando métodos alternativos...");
                                    }

                                    // Tentar clicar no botão várias vezes
                                    bool clicked = false;
                                    for (int attempt = 0; attempt < 5; attempt++)
                                    {
                                        // Primeiro tentar clique direto via PuppeteerSharp
                                        try
                                        {
                                            var btnElement = await page.QuerySelectorAsync("#user-profile-header-AddFriend");
                                            if (btnElement != null)
                                            {
                                                await btnElement.ClickAsync();
                                                clicked = true;
                                                AddLog($"✅ [AddFriend] Clique via PuppeteerSharp (tentativa {attempt + 1})");
                                                
                                                // Fechar imediatamente após clique bem-sucedido
                                                await System.Threading.Tasks.Task.Delay(300); // Pequeno delay para garantir que o clique foi processado
                                                try { await page.Browser.CloseAsync(); } catch { }
                                                return;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            AddLog($"⚠️ [AddFriend] PuppeteerSharp click falhou: {ex.Message}");
                                        }

                                        // Fallback: JavaScript
                                        var result = await page.EvaluateExpressionAsync<string>(@"
                                            (function() {
                                                var log = [];
                                                
                                                // Método 1: getElementById
                                                var btn = document.getElementById('user-profile-header-AddFriend');
                                                if (btn) {
                                                    btn.click();
                                                    return 'SUCCESS:getElementById';
                                                }
                                                log.push('getElementById failed');
                                                
                                                // Método 2: querySelector com ID
                                                btn = document.querySelector('#user-profile-header-AddFriend');
                                                if (btn) {
                                                    btn.click();
                                                    return 'SUCCESS:querySelector#id';
                                                }
                                                log.push('querySelector#id failed');
                                                
                                                // Método 3: Buscar todos elementos com id contendo AddFriend
                                                var allElements = document.querySelectorAll('*');
                                                for (var i = 0; i < allElements.length; i++) {
                                                    if (allElements[i].id && allElements[i].id.includes('AddFriend')) {
                                                        allElements[i].click();
                                                        return 'SUCCESS:idContains:' + allElements[i].id;
                                                    }
                                                }
                                                log.push('idContains failed');
                                                
                                                // Método 4: Buscar botões por texto
                                                var buttons = document.getElementsByTagName('button');
                                                for (var j = 0; j < buttons.length; j++) {
                                                    var text = buttons[j].innerText || buttons[j].textContent || '';
                                                    var textLower = text.toLowerCase();
                                                    if (textLower.indexOf('add connection') !== -1 || 
                                                        textLower.indexOf('add friend') !== -1 ||
                                                        textLower.indexOf('adicionar') !== -1) {
                                                        buttons[j].click();
                                                        return 'SUCCESS:buttonText:' + text.substring(0,30);
                                                    }
                                                }
                                                log.push('buttonText failed, buttons found: ' + buttons.length);
                                                
                                                // Log todos os botões para debug
                                                var btnInfo = [];
                                                for (var k = 0; k < buttons.length && k < 10; k++) {
                                                    btnInfo.push('id=' + (buttons[k].id || 'none') + ',text=' + (buttons[k].innerText || '').substring(0,20));
                                                }
                                                
                                                return 'FAILED:' + log.join('|') + '|buttons:' + btnInfo.join(';');
                                            })()
                                        ");

                                        AddLog($"🔍 [AddFriend] JS Result: {result}");

                                        if (result != null && result.StartsWith("SUCCESS"))
                                        {
                                            clicked = true;
                                            AddLog($"✅ [AddFriend] {result} (tentativa {attempt + 1})");
                                            
                                            // Fechar imediatamente após clique bem-sucedido via JS
                                            await System.Threading.Tasks.Task.Delay(300);
                                            try { await page.Browser.CloseAsync(); } catch { }
                                            return;
                                        }
                                        
                                        await System.Threading.Tasks.Task.Delay(1000); // Reduzido de 1500ms
                                    }

                                    if (!clicked)
                                    {
                                        AddLogWarning($"⚠️ [AddFriend] Não foi possível clicar no botão após 5 tentativas");
                                        try { await page.Browser.CloseAsync(); } catch { }
                                        return;
                                    }

                                    // Se chegou aqui, algo deu errado - fechar o navegador
                                    try { await page.Browser.CloseAsync(); } catch { }
                                }
                                catch (Exception ex)
                                {
                                    AddLogError($"❌ [AddFriend] Erro no navegador: {ex.Message}");
                                }
                            });

                            FriendsStatusLabel.ForeColor = System.Drawing.Color.Green;
                            FriendsStatusLabel.Text = $"Adicionando {username}...";
                            FriendsAddTextBox.Clear();
                        }
                    }
                    else
                    {
                        FriendsStatusLabel.ForeColor = System.Drawing.Color.Red;
                        FriendsStatusLabel.Text = "Usuário não encontrado";
                    }
                }
            }
            catch (Exception ex)
            {
                FriendsStatusLabel.ForeColor = System.Drawing.Color.Red;
                FriendsStatusLabel.Text = $"Erro: {ex.Message}";
            }

            ADDAMIGO.Enabled = true;
            FriendsStatusLabel.ForeColor = System.Drawing.Color.Black;
        }

        #endregion


        private void SetDescription_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var descForm = new Form())
            {
                descForm.Text = $"Definir Descrição - {SelectedAccount.Username}";
                descForm.Size = new System.Drawing.Size(400, 250);
                descForm.StartPosition = FormStartPosition.CenterParent;
                descForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                descForm.MaximizeBox = false;
                descForm.MinimizeBox = false;

                var label = new Label();
                label.Text = "Descrição:";
                label.Location = new System.Drawing.Point(20, 15);
                label.AutoSize = true;
                descForm.Controls.Add(label);

                var textBox = new TextBox();
                textBox.Multiline = true;
                textBox.ScrollBars = ScrollBars.Vertical;
                textBox.Location = new System.Drawing.Point(20, 35);
                textBox.Size = new System.Drawing.Size(345, 130);
                textBox.MaxLength = 5000;
                textBox.Text = SelectedAccount.Description ?? "";
                descForm.Controls.Add(textBox);

                var okButton = new Button();
                okButton.Text = "Salvar";
                okButton.Location = new System.Drawing.Point(200, 175);
                okButton.DialogResult = DialogResult.OK;
                descForm.Controls.Add(okButton);

                var cancelButton = new Button();
                cancelButton.Text = "Cancelar";
                cancelButton.Location = new System.Drawing.Point(290, 175);
                cancelButton.DialogResult = DialogResult.Cancel;
                descForm.Controls.Add(cancelButton);

                descForm.AcceptButton = okButton;
                descForm.CancelButton = cancelButton;

                if (descForm.ShowDialog() == DialogResult.OK)
                {
                    foreach (Account account in AccountsView.SelectedObjects)
                    {
                        account.Description = textBox.Text;
                    }

                    RefreshView();
                    AddLog($"📝 Descrição atualizada para {AccountsView.SelectedObjects.Count} conta(s)");
                }
            }
        }

        private void JoinServer_Click(object sender, EventArgs e)
        {
            Match IDMatch = Regex.Match(PlaceID.Text, @"\/games\/(\d+)[\/|\?]?"); // idiotproofing

            // Support for new share link format: https://www.roblox.com/share?code=XXX&type=Server
            Match ShareMatch = Regex.Match(PlaceID.Text, @"share\?code=([a-f0-9]+)(?:&|$)", RegexOptions.IgnoreCase);
            if (ShareMatch.Success)
            {
                JobID.Text = PlaceID.Text; // Pass the entire URL to JobID for processing
                PlaceID.Text = "0"; // PlaceID not needed for share links, set to 0
            }
            else if (PlaceID.Text.Contains("privateServerLinkCode") && IDMatch.Success)
                JobID.Text = PlaceID.Text;

            Game G = RecentGames.FirstOrDefault(RG => RG.Details.filteredName == PlaceID.Text);

            if (G != null)
                PlaceID.Text = G.Details.placeId.ToString();

            // Don't clear PlaceID if it's a share link
            if (!ShareMatch.Success)
                PlaceID.Text = IDMatch.Success ? IDMatch.Groups[1].Value : Regex.Replace(PlaceID.Text, "[^0-9]", "");

            bool VIPServer = JobID.TextLength > 4 && JobID.Text.Substring(0, 4) == "VIP:";

            if (!long.TryParse(PlaceID.Text, out long PlaceId)) return;

            // Só adicionar RecentGame se NÃO houver share link no JobID (o jogo real será resolvido depois)
            bool jobHasShareLink = Regex.IsMatch(JobID.Text, @"share\?code=[a-f0-9]+", RegexOptions.IgnoreCase);
            if (!PlaceTimer.Enabled && PlaceId > 0 && !jobHasShareLink)
                _ = Task.Run(() => AddRecentGame(new Game(PlaceId)));

            CancelLaunching();

            bool LaunchMultiple = AccountsView.SelectedObjects.Count > 1;
            
            // === AUTO FIX CAPTCHA antes de entrar no jogo ===
            AutoFixCaptchaThenJoin(PlaceId, VIPServer, LaunchMultiple);
        }
        
        /// <summary>
        /// Executa FIX CAPTCHA automaticamente para as contas selecionadas,
        /// depois entra no jogo. Se não houver extensão configurada, pula o captcha.
        /// </summary>
        private async void AutoFixCaptchaThenJoin(long placeId, bool vipServer, bool launchMultiple)
        {
            string jobId = vipServer ? JobID.Text.Substring(4) : JobID.Text;

            // Se o JobID contém um share link, resolver para obter o PlaceID real
            var shareMatch = Regex.Match(jobId, @"share\?code=([a-f0-9]+)", RegexOptions.IgnoreCase);
            if (shareMatch.Success && SelectedAccount != null)
            {
                string shareCode = shareMatch.Groups[1].Value;
                AddLog($"🔗 Share link detectado no Server Privado, resolvendo...");
                try
                {
                    var resolveResult = await SelectedAccount.ResolveShareLink(shareCode);
                    if (resolveResult.success && resolveResult.placeId > 0)
                    {
                        placeId = resolveResult.placeId;
                        PlaceID.Text = placeId.ToString();
                        AddLog($"✅ Share link resolvido → PlaceID real: {placeId}");

                        _ = Task.Run(() => AddRecentGame(new Game(placeId)));
                    }
                    else
                    {
                        AddLog($"⚠️ Falha ao resolver share link: {resolveResult.error}");
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"⚠️ Erro ao resolver share link: {ex.Message}");
                }
            }

            AddLog($"🎮 Entrar no Jogo chamado - PlaceID: {placeId}, VIP: {vipServer}, Múltiplas: {launchMultiple}");
            
            // Verificar se há extensão anti-captcha configurada
            string selectedExtension = AntiCaptchaComboBox.SelectedItem?.ToString() ?? "none";
            bool hasCaptchaExtension = selectedExtension != "none";
            
            AddLog($"🔍 Extensão anti-captcha selecionada: '{selectedExtension}' (ativa: {hasCaptchaExtension})");
            
            string extensionPath = null;
            if (hasCaptchaExtension)
            {
                if (selectedExtension == "extension (legado)")
                    extensionPath = Path.Combine(Environment.CurrentDirectory, "extension");
                else
                    extensionPath = Path.Combine(Environment.CurrentDirectory, "extensions", selectedExtension);
                
                AddLog($"📁 Caminho da extensão: {extensionPath}");
                
                if (!Directory.Exists(extensionPath))
                {
                    AddLog($"⚠️ Extensão '{selectedExtension}' não encontrada, pulando FIX CAPTCHA...");
                    hasCaptchaExtension = false;
                }
                else
                {
                    AddLog($"✅ Pasta da extensão encontrada");
                }
            }
            
            // Obter contas selecionadas
            var selectedAccounts = AccountsView.SelectedObjects.Cast<Account>().ToList();
            AddLog($"👥 Contas selecionadas: {selectedAccounts.Count} ({string.Join(", ", selectedAccounts.Select(a => a.Username))})");
            
            if (hasCaptchaExtension && selectedAccounts.Count > 0)
            {
                AddLog($"🔓 >>> INICIANDO Auto FIX CAPTCHA para {selectedAccounts.Count} conta(s) <<<");
                
                // Resolver captcha para todas as contas em paralelo e aguardar
                var captchaTasks = new List<Task>();
                foreach (var account in selectedAccounts)
                {
                    AddLog($"🔓 [{account.Username}] Enviando para FIX CAPTCHA...");
                    _captchaCount++;
                    UpdateCaptchaButtonText();
                    captchaTasks.Add(SolveCaptchaForAccountAsync(account, placeId, extensionPath));
                }
                
                AddLog($"⏳ Aguardando {captchaTasks.Count} captcha(s) terminarem...");
                
                // Aguardar TODAS as resoluções de captcha terminarem
                await Task.WhenAll(captchaTasks);
                
                AddLog($"✅ FIX CAPTCHA concluído para todas as contas, entrando no jogo...");
            }
            else
            {
                if (!hasCaptchaExtension)
                    AddLog($"⏭️ Nenhuma extensão anti-captcha ativa, pulando FIX CAPTCHA");
                if (selectedAccounts.Count == 0)
                    AddLog($"⏭️ Nenhuma conta selecionada");
                    
                AddLog($"🎮 Entrando no jogo direto (sem FIX CAPTCHA)...");
            }
            
            // Agora entrar no jogo
            AddLog($"🚀 Iniciando entrada no jogo - PlaceID: {placeId}");
            new Thread(async () =>
            {
                if (launchMultiple || (SelectedAccount == null && SelectedAccounts != null && SelectedAccounts.Count > 0))
                {
                    LauncherToken = new CancellationTokenSource();
                    await LaunchAccounts(SelectedAccounts, placeId, jobId, false, vipServer);
                }
                else if (SelectedAccount != null)
                {
                    if (DebugModeAtivo) AddLog($"🔍 [Debug] Lançando conta única: {SelectedAccount.Username}");

                    // Registrar acesso ANTES de lançar (JoinServer aguarda o jogo fechar)
                    _ = Classes.SupabaseManager.Instance.LogAccountAccessAsync(SelectedAccount.Username, placeId);

                    string res = await SelectedAccount.JoinServer(placeId, jobId, false, vipServer);

                    if (!res.Contains("Success"))
                        MessageBox.Show(res);
                }
                else
                {
                    MessageBox.Show("Selecione uma ou mais contas primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }).Start();
        }

        private int _captchaCount = 0; // Contador de captchas em andamento

        private async void SolveCaptchaButton_Click(object sender, EventArgs e)
        {
            // Verificar se tem contas selecionadas
            var selectedAccounts = AccountsView.SelectedObjects.Cast<Account>().ToList();
            
            if (selectedAccounts.Count == 0)
            {
                MessageBox.Show("Selecione uma ou mais contas primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Verificar extensão selecionada
            string selectedExtension = AntiCaptchaComboBox.SelectedItem?.ToString() ?? "none";
            
            if (selectedExtension == "none")
            {
                MessageBox.Show(
                    "Nenhuma extensão anti-captcha selecionada!\n\n" +
                    "Selecione uma extensão no campo 'Anti-Captcha' antes de resolver.",
                    "Aviso",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            // Montar caminho da extensão
            string extensionPath;
            if (selectedExtension == "extension (legado)")
            {
                extensionPath = Path.Combine(Environment.CurrentDirectory, "extension");
            }
            else
            {
                extensionPath = Path.Combine(Environment.CurrentDirectory, "extensions", selectedExtension);
            }

            if (!Directory.Exists(extensionPath))
            {
                MessageBox.Show(
                    $"Extensão '{selectedExtension}' não encontrada!\n\n" +
                    $"Caminho esperado: {extensionPath}",
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            // Verificar se tem PlaceID para usar, senão usa Blox Fruits por padrão
            long placeId = 2753915549; // Blox Fruits

            if (!string.IsNullOrWhiteSpace(PlaceID.Text))
            {
                string cleanPlaceId = Regex.Replace(PlaceID.Text, "[^0-9]", "");
                if (long.TryParse(cleanPlaceId, out long parsedId) && parsedId > 0)
                {
                    placeId = parsedId;
                }
            }

            // Resolver captcha para todas as contas selecionadas
            AddLog($"🔓 Resolvendo captcha para {selectedAccounts.Count} conta(s)...");
            
            foreach (var account in selectedAccounts)
            {
                // Incrementar contador e atualizar botão
                _captchaCount++;
                UpdateCaptchaButtonText();

                // Iniciar resolução em paralelo (não aguarda cada uma)
                _ = SolveCaptchaForAccountAsync(account, placeId, extensionPath);
            }
        }

        private async Task SolveCaptchaForAccountAsync(Account account, long placeId, string extensionPath)
        {
            try
            {
                AddLog($"🔄 [{account.Username}] Iniciando captcha...");
                var solver = new Classes.CaptchaSolver(account);
                await solver.SolveCaptchaWithPuppeteerAsync(placeId, extensionPath);
                AddLogSuccess($"[{account.Username}] Captcha resolvido!");
            }
            catch (Exception ex)
            {
                AddLogError($"[{account.Username}] Erro: {ex.Message}");
            }
            finally
            {
                // Decrementar contador e atualizar botão
                _captchaCount--;
                UpdateCaptchaButtonText();
            }
        }

        private void UpdateCaptchaButtonText()
        {
            if (_captchaCount > 0)
            {
                SolveCaptchaButton.Text = $"🔓 RESOLVER CAPTCHA ({_captchaCount} em andamento)";
            }
            else
            {
                SolveCaptchaButton.Text = "🔓 RESOLVER CAPTCHA";
            }
        }

        private void ServerList_Click(object sender, EventArgs e)
        {
            if (AccountsList.Count == 0 || LastValidAccount == null)
                MessageBox.Show("Some features may not work unless there is a valid account", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            if (ServerListForm.Visible)
            {
                ServerListForm.WindowState = FormWindowState.Normal;
                ServerListForm.BringToFront();
            }
            else
                ServerListForm.Show();

            ServerListForm.Busy = false; // incase it somehow bugs out

            ServerListForm.StartPosition = FormStartPosition.Manual;
            ServerListForm.Top = Top;
            ServerListForm.Left = Right;
        }


        private void removeAccountToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (AccountsView.SelectedObjects.Count > 1)
            {
                DialogResult result = MessageBox.Show($"Are you sure you want to remove {AccountsView.SelectedObjects.Count} accounts?", "Remove Accounts", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    foreach (Account acc in AccountsView.SelectedObjects)
                        AccountsList.Remove(acc);

                    RefreshView();
                    SaveAccounts();
                }
            }
            else if (SelectedAccount != null)
            {
                DialogResult result = MessageBox.Show($"Are you sure you want to remove {SelectedAccount.Username}?", "Remove Account", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    AccountsList.Remove(SelectedAccount);

                    RefreshView();
                    SaveAccounts();
                }
            }
        }

        private void setHotkeyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Criar form para selecionar hotkey
            using (var hotkeyForm = new Form())
            {
                hotkeyForm.Text = $"Definir Hotkey - {SelectedAccount.Username}";
                hotkeyForm.Size = new System.Drawing.Size(300, 150);
                hotkeyForm.StartPosition = FormStartPosition.CenterParent;
                hotkeyForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                hotkeyForm.MaximizeBox = false;
                hotkeyForm.MinimizeBox = false;

                var label = new Label();
                label.Text = "Selecione a hotkey:";
                label.Location = new System.Drawing.Point(20, 20);
                label.AutoSize = true;
                hotkeyForm.Controls.Add(label);

                var comboBox = new ComboBox();
                comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
                comboBox.Items.AddRange(new object[] {
                    "Desativado",
                    "Ctrl+Alt+1", "Ctrl+Alt+2", "Ctrl+Alt+3", "Ctrl+Alt+4", "Ctrl+Alt+5",
                    "Ctrl+Alt+6", "Ctrl+Alt+7", "Ctrl+Alt+8", "Ctrl+Alt+9", "Ctrl+Alt+0",
                    "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
                });
                comboBox.Location = new System.Drawing.Point(20, 45);
                comboBox.Size = new System.Drawing.Size(240, 21);
                
                // Selecionar hotkey atual se existir
                string currentHotkey = SelectedAccount.SavedHotkey;
                if (!string.IsNullOrEmpty(currentHotkey))
                {
                    int index = comboBox.Items.IndexOf(currentHotkey);
                    comboBox.SelectedIndex = index >= 0 ? index : 0;
                }
                else
                {
                    comboBox.SelectedIndex = 0;
                }
                hotkeyForm.Controls.Add(comboBox);

                var okButton = new Button();
                okButton.Text = "OK";
                okButton.Location = new System.Drawing.Point(100, 80);
                okButton.DialogResult = DialogResult.OK;
                hotkeyForm.Controls.Add(okButton);

                hotkeyForm.AcceptButton = okButton;

                if (hotkeyForm.ShowDialog() == DialogResult.OK)
                {
                    string selectedHotkey = comboBox.SelectedItem?.ToString() ?? "Desativado";
                    
                    // Remover hotkey anterior
                    UnregisterAccountHotkey(SelectedAccount);
                    
                    // Salvar e registrar nova hotkey
                    SelectedAccount.SavedHotkey = selectedHotkey;
                    
                    if (selectedHotkey != "Desativado")
                    {
                        RegisterAccountHotkey(SelectedAccount, selectedHotkey);
                        AddLog($"🎯 Hotkey {selectedHotkey} definida para {SelectedAccount.Username}");
                        MessageBox.Show($"Hotkey {selectedHotkey} definida!\n\nPressione {selectedHotkey} para selecionar esta conta.", 
                            "Hotkey Definida", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        AddLog($"🎯 Hotkey removida de {SelectedAccount.Username}");
                    }
                    
                    SaveAccounts();
                }
            }
        }

        private async void syncToSupabaseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Mostrar popup para selecionar jogo e item
                await ShowSyncToCloudDialog(SelectedAccount);
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro: {ex.Message}");
                MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task ShowSyncToCloudDialog(Account account)
        {
            // Criar o form
            var form = new Form
            {
                Text = "☁️ Sincronizar para Nuvem",
                Size = new Size(400, 380),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = ThemeEditor.FormsBackground,
                ForeColor = ThemeEditor.FormsForeground
            };

            // Label da conta
            var accountLabel = new Label
            {
                Text = $"Conta: {account.Username}",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new System.Drawing.Point(20, 15),
                AutoSize = true,
                ForeColor = Color.LimeGreen
            };

            // Label Jogo
            var gameLabel = new Label
            {
                Text = "Jogo:",
                Location = new System.Drawing.Point(20, 55),
                AutoSize = true
            };

            // ComboBox Jogo
            var gameCombo = new ComboBox
            {
                Location = new System.Drawing.Point(20, 75),
                Size = new Size(340, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeEditor.TextBoxesBackground,
                ForeColor = ThemeEditor.TextBoxesForeground,
                FlatStyle = FlatStyle.Flat
            };

            // Label Item
            var itemLabel = new Label
            {
                Text = "Item:",
                Location = new System.Drawing.Point(20, 115),
                AutoSize = true
            };

            // ComboBox Item
            var itemCombo = new ComboBox
            {
                Location = new System.Drawing.Point(20, 135),
                Size = new Size(340, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeEditor.TextBoxesBackground,
                ForeColor = ThemeEditor.TextBoxesForeground,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };

            // Label Quantidade
            var qtyLabel = new Label
            {
                Text = "Quantidade em estoque:",
                Location = new System.Drawing.Point(20, 175),
                AutoSize = true
            };

            // TextBox Quantidade
            var qtyTextBox = new TextBox
            {
                Location = new System.Drawing.Point(20, 195),
                Size = new Size(120, 25),
                BackColor = ThemeEditor.TextBoxesBackground,
                ForeColor = ThemeEditor.TextBoxesForeground,
                Text = "0",
                BorderStyle = BorderStyle.FixedSingle
            };

            // Botão Puxar Robux
            var robuxButton = new Button
            {
                Text = "💰 Puxar Robux",
                Location = new System.Drawing.Point(150, 193),
                Size = new Size(100, 27),
                BackColor = Color.FromArgb(80, 80, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            robuxButton.FlatAppearance.BorderSize = 0;

            // Label dica
            var tipLabel = new Label
            {
                Text = "💡 use k, m, b (ex: 1.5m)",
                Location = new System.Drawing.Point(260, 198),
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8F)
            };

            // Botão Sincronizar
            var syncButton = new Button
            {
                Text = "☁️ Sincronizar",
                Location = new System.Drawing.Point(20, 280),
                Size = new Size(120, 35),
                BackColor = Color.FromArgb(0, 120, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Cursor = Cursors.Hand
            };
            syncButton.FlatAppearance.BorderSize = 0;

            // Botão Cancelar
            var cancelButton = new Button
            {
                Text = "Cancelar",
                Location = new System.Drawing.Point(240, 280),
                Size = new Size(120, 35),
                BackColor = ThemeEditor.ButtonsBackground,
                ForeColor = ThemeEditor.ButtonsForeground,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel,
                Cursor = Cursors.Hand
            };
            cancelButton.FlatAppearance.BorderSize = 0;

            // Robux info label (será atualizado depois)
            var robuxInfoLabel = new Label
            {
                Text = "Robux da conta: Carregando...",
                Location = new System.Drawing.Point(20, 230),
                AutoSize = true,
                ForeColor = Color.Gold,
                Font = new Font("Segoe UI", 9F)
            };

            // Status label
            var statusLabel = new Label
            {
                Text = "Carregando jogos...",
                Location = new System.Drawing.Point(20, 320),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            form.Controls.AddRange(new Control[] { 
                accountLabel, gameLabel, gameCombo, itemLabel, itemCombo, 
                qtyLabel, qtyTextBox, robuxButton, tipLabel, robuxInfoLabel,
                syncButton, cancelButton, statusLabel 
            });

            // Variável para armazenar o robux
            long accountRobux = 0;

            // Evento do botão Puxar Robux (busca assíncrona usando o mesmo método que funciona)
            robuxButton.Click += async (s, ev) =>
            {
                try
                {
                    robuxButton.Enabled = false;
                    robuxButton.Text = "⏳...";
                    statusLabel.Text = "Buscando Robux...";
                    
                    // Usar o mesmo método que "Atualizar Saldo Robux" usa
                    accountRobux = await GetAccountRobuxAsync(account);
                    
                    if (accountRobux > 0)
                    {
                        qtyTextBox.Text = accountRobux.ToString();
                        robuxInfoLabel.Text = $"Robux da conta: {accountRobux:N0}";
                        statusLabel.Text = $"Quantidade definida para {accountRobux:N0} (Robux da conta)";
                    }
                    else
                    {
                        robuxInfoLabel.Text = "Robux da conta: 0 (ou erro)";
                        statusLabel.Text = "Não foi possível obter saldo (cookie inválido?)";
                    }
                }
                catch (Exception ex)
                {
                    statusLabel.Text = $"Erro ao buscar Robux: {ex.Message}";
                }
                finally
                {
                    robuxButton.Enabled = true;
                    robuxButton.Text = "💰 Puxar Robux";
                }
            };

            // Carregar jogos
            var games = await Classes.SupabaseManager.Instance.GetGamesAsync();
            if (games != null && games.Count > 0)
            {
                gameCombo.DisplayMember = "Name";
                gameCombo.ValueMember = "Id";
                gameCombo.DataSource = games.OrderBy(g => g.Name).ToList();
                statusLabel.Text = $"{games.Count} jogos disponíveis";
            }
            else
            {
                statusLabel.Text = "Nenhum jogo encontrado";
                MessageBox.Show("Nenhum jogo encontrado no Supabase.\nConfigure os jogos primeiro.", 
                    "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Evento ao selecionar jogo
            gameCombo.SelectedIndexChanged += async (s, ev) =>
            {
                var selectedGame = gameCombo.SelectedItem as Classes.SupabaseGame;
                if (selectedGame == null) return;

                itemCombo.Enabled = false;
                itemCombo.DataSource = null;
                statusLabel.Text = "Carregando itens...";

                var items = await Classes.SupabaseManager.Instance.GetGameItemsAsync(selectedGame.Id);
                if (items != null && items.Count > 0)
                {
                    itemCombo.DisplayMember = "Name";
                    itemCombo.ValueMember = "Id";
                    itemCombo.DataSource = items.OrderBy(i => i.Name).ToList();
                    itemCombo.Enabled = true;
                    syncButton.Enabled = true;
                    statusLabel.Text = $"{items.Count} itens disponíveis";
                }
                else
                {
                    statusLabel.Text = "Nenhum item encontrado para este jogo";
                    syncButton.Enabled = false;
                }
            };

            // Evento do botão sincronizar
            syncButton.Click += async (s, ev) =>
            {
                var selectedGame = gameCombo.SelectedItem as Classes.SupabaseGame;
                var selectedItem = itemCombo.SelectedItem as Classes.SupabaseGameItem;
                
                if (selectedGame == null || selectedItem == null)
                {
                    MessageBox.Show("Selecione um jogo e item.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Parse da quantidade com suporte a k, m, b, t
                long quantity = ParseAbbreviatedNumber(qtyTextBox.Text);

                syncButton.Enabled = false;
                statusLabel.Text = "Sincronizando...";

                try
                {
                    // 1. Sincronizar a conta (username + cookie)
                    var accountResult = await Classes.SupabaseManager.Instance.UpsertAccountAsync(
                        account.Username,
                        account.SecurityToken,
                        account.UserID
                    );

                    if (accountResult == null)
                    {
                        MessageBox.Show("Erro ao sincronizar conta.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        syncButton.Enabled = true;
                        statusLabel.Text = "Erro na sincronização";
                        return;
                    }

                    // 2. Adicionar ao inventário (jogo/item)
                    var inventoryResult = await Classes.SupabaseManager.Instance.UpsertInventoryAsync(
                        account.Username,
                        selectedItem.Id,
                        quantity
                    );

                    if (inventoryResult)
                    {
                        AddLog($"✅ {account.Username} sincronizado: {selectedGame.Name} > {selectedItem.Name} ({quantity:N0})");
                        
                        // Atualizar painel de inventário se estiver visualizando o mesmo jogo/item
                        _inventoryPanel?.RefreshIfCurrentGameItem(selectedGame.Id, selectedItem.Id);
                        
                        MessageBox.Show(
                            $"Conta '{account.Username}' sincronizada com sucesso!\n\n" +
                            $"Jogo: {selectedGame.Name}\n" +
                            $"Item: {selectedItem.Name}\n" +
                            $"Quantidade: {quantity:N0}",
                            "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        form.DialogResult = DialogResult.OK;
                        form.Close();
                    }
                    else
                    {
                        MessageBox.Show("Erro ao adicionar ao inventário.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        syncButton.Enabled = true;
                        statusLabel.Text = "Erro na sincronização";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    syncButton.Enabled = true;
                    statusLabel.Text = "Erro na sincronização";
                }
            };

            form.ShowDialog();
        }

        private async void viewHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var username = SelectedAccount.Username;

            var form = new Form
            {
                Text = $"Histórico - {username}",
                Size = new Size(750, 500),
                MinimumSize = new Size(600, 350),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                MaximizeBox = true,
                MinimizeBox = false,
                BackColor = ThemeEditor.FormsBackground,
                ForeColor = ThemeEditor.FormsForeground
            };

            var loadingLabel = new Label
            {
                Text = "Carregando histórico...",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.Gray,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            form.Controls.Add(loadingLabel);
            form.Show();

            try
            {
                var history = await Classes.SupabaseManager.Instance.GetAccountHistoryAsync(username);
                form.Controls.Remove(loadingLabel);
                loadingLabel.Dispose();

                var listView = new ListView
                {
                    Dock = DockStyle.Fill,
                    View = View.Details,
                    FullRowSelect = true,
                    GridLines = false,
                    BackColor = ThemeEditor.FormsBackground,
                    ForeColor = ThemeEditor.FormsForeground,
                    Font = new Font("Segoe UI", 9F),
                    BorderStyle = BorderStyle.None,
                    HeaderStyle = ColumnHeaderStyle.Nonclickable
                };

                listView.Columns.Add("Usuário", 150);
                listView.Columns.Add("Jogo", 220);
                listView.Columns.Add("PlaceID", 120);
                listView.Columns.Add("Data/Hora", 160);

                if (history.Count == 0)
                {
                    var emptyLabel = new Label
                    {
                        Text = "Nenhum registro de acesso encontrado.",
                        Font = new Font("Segoe UI", 9F),
                        ForeColor = Color.Gray,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleCenter
                    };
                    form.Controls.Add(emptyLabel);
                }
                else
                {
                    foreach (var entry in history)
                    {
                        var item = new ListViewItem(entry.UserDisplayName ?? "—");
                        item.SubItems.Add(entry.GameName ?? "—");
                        item.SubItems.Add(entry.PlaceId?.ToString() ?? "—");
                        item.SubItems.Add(entry.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
                        listView.Items.Add(item);
                    }
                    form.Controls.Add(listView);
                }

                var bottomPanel = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 40,
                    BackColor = ThemeEditor.FormsBackground
                };

                var countLabel = new Label
                {
                    Text = $"{history.Count} registro(s)",
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = Color.Gray,
                    Location = new System.Drawing.Point(10, 10),
                    AutoSize = true
                };

                var closeButton = new Button
                {
                    Text = "Fechar",
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = ThemeEditor.ButtonsForeground,
                    BackColor = ThemeEditor.ButtonsBackground,
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(80, 28),
                    Anchor = AnchorStyles.Right,
                    Location = new System.Drawing.Point(form.ClientSize.Width - 95, 6)
                };
                closeButton.FlatAppearance.BorderColor = ThemeEditor.ButtonsBorder;
                closeButton.Click += (s, ev) => form.Close();

                bottomPanel.Controls.Add(countLabel);
                bottomPanel.Controls.Add(closeButton);
                form.Controls.Add(bottomPanel);
            }
            catch (Exception ex)
            {
                loadingLabel.Text = $"Erro ao carregar histórico: {ex.Message}";
                loadingLabel.ForeColor = Color.Red;
            }
        }

        /// <summary>
        /// Converte texto com notação abreviada para número
        /// Ex: "1k" -> 1000, "2m" -> 2000000, "3b" -> 3000000000
        /// </summary>
        private long ParseAbbreviatedNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            string original = text;
            text = text.Trim().ToLower().Replace(",", "");

            long multiplier = 1;
            string suffix = "";
            if (text.EndsWith("k"))
            {
                multiplier = 1_000;
                suffix = "k";
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("m"))
            {
                multiplier = 1_000_000;
                suffix = "m";
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("b"))
            {
                multiplier = 1_000_000_000;
                suffix = "b";
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("t"))
            {
                multiplier = 1_000_000_000_000;
                suffix = "t";
                text = text.Substring(0, text.Length - 1);
            }

            if (DebugModeAtivo)
                AddLog($"[DEBUG] AM.Parse: original='{original}', cleaned='{text}', suffix='{suffix}', multiplier={multiplier}");

            if (decimal.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal decimalValue))
            {
                long result = (long)(decimalValue * multiplier);
                if (DebugModeAtivo)
                    AddLog($"[DEBUG] AM.Parse: decimal OK → {decimalValue} * {multiplier} = {result}");
                return result;
            }

            if (long.TryParse(text, out long longValue))
            {
                long result = longValue * multiplier;
                if (DebugModeAtivo)
                    AddLog($"[DEBUG] AM.Parse: long OK → {longValue} * {multiplier} = {result}");
                return result;
            }

            if (DebugModeAtivo)
                AddLog($"[DEBUG] AM.Parse: FALHOU ao parsear '{text}' (original: '{original}')");
            
            return 0;
        }

        private void followFriendToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var form = new Forms.FollowFriendForm(SelectedAccount);
            form.ShowDialog();
        }

        private async void removeAllFriendsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedAccounts = AccountsView.SelectedObjects.Cast<Account>().ToList();

            if (selectedAccounts.Count == 0)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string msg = selectedAccounts.Count == 1
                ? $"Tem certeza que deseja REMOVER TODAS AS AMIZADES da conta '{selectedAccounts[0].Username}'?\n\nEssa ação não pode ser desfeita!"
                : $"Tem certeza que deseja REMOVER TODAS AS AMIZADES de {selectedAccounts.Count} contas?\n\nEssa ação não pode ser desfeita!";

            DialogResult confirm = MessageBox.Show(msg, "Confirmar Remoção de Amizades", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            int totalRemoved = 0;
            int accountsProcessed = 0;

            foreach (var account in selectedAccounts)
            {
                try
                {
                    AddLog($"🗑️ Removendo amizades de {account.Username} ({++accountsProcessed}/{selectedAccounts.Count})...");
                    int removedCount = await DeleteAllFriendsAsync(account);
                    totalRemoved += removedCount;
                    AddLogSuccess($"✅ {removedCount} amizades removidas de {account.Username}");
                }
                catch (Exception ex)
                {
                    AddLogError($"❌ Erro ao remover amizades de {account.Username}: {ex.Message}");
                }
            }

            MessageBox.Show(
                $"{totalRemoved} amizades removidas de {selectedAccounts.Count} conta(s)!",
                "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void unblockAllUsersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedAccounts = AccountsView.SelectedObjects.Cast<Account>().ToList();

            if (selectedAccounts.Count == 0)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string msg = selectedAccounts.Count == 1
                ? $"Desbloquear TODOS os usuários bloqueados da conta '{selectedAccounts[0].Username}'?"
                : $"Desbloquear TODOS os usuários bloqueados de {selectedAccounts.Count} contas?";

            if (MessageBox.Show(msg, "Confirmar Desbloqueio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            int successCount = 0;

            foreach (var account in selectedAccounts)
            {
                try
                {
                    AddLog($"🔓 [{account.Username}] Iniciando desbloqueio...");
                    int result = await UnblockAllUsersNewApiAsync(account);
                    if (result >= 0)
                        successCount++;
                }
                catch (Exception ex)
                {
                    AddLogError($"❌ [{account.Username}] Erro: {ex.GetType().Name}: {ex.Message}");
                }
            }

            MessageBox.Show(
                $"Desbloqueio finalizado para {successCount}/{selectedAccounts.Count} conta(s).\nVerifique os logs para detalhes.",
                "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task<int> UnblockAllUsersNewApiAsync(Account account)
        {
            // 1. CSRF Token (usa método existente que funciona)
            bool csrfOk = account.GetCSRFToken(out string csrfToken);
            AddLog($"🔍 [{account.Username}] CSRF Token: {(csrfOk ? "OK" : $"FALHOU → {csrfToken}")}");
            if (!csrfOk)
            {
                AddLogWarning($"⚠️ [{account.Username}] Sessão inválida (CSRF falhou). Cookie pode estar expirado.");
                return -1;
            }

            // 2. Gerar BrowserTrackerID se necessário
            if (string.IsNullOrEmpty(account.BrowserTrackerID))
            {
                var r = new Random();
                account.BrowserTrackerID = r.Next(100000, 175000).ToString() + r.Next(100000, 900000).ToString();
            }
            AddLog($"🔍 [{account.Username}] BrowserTrackerID: {account.BrowserTrackerID}");

            // 3. HttpClient com cookies e CSRF — usando novos endpoints apis.roblox.com
            var cookieContainer = new System.Net.CookieContainer();
            var apiUri = new Uri("https://apis.roblox.com");
            cookieContainer.Add(apiUri, new System.Net.Cookie(".ROBLOSECURITY", account.SecurityToken, "/", ".roblox.com"));
            string trackerValue = Uri.EscapeDataString($"CreateDate={DateTime.UtcNow:M/d/yyyy h:mm:ss tt}") +
                $"&rbxid={account.UserID}&browserid={account.BrowserTrackerID}";
            cookieContainer.Add(apiUri, new System.Net.Cookie("RBXEventTrackerV2", trackerValue, "/", ".roblox.com"));

            using (var handler = new HttpClientHandler { CookieContainer = cookieContainer, UseProxy = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
            {
                client.DefaultRequestHeaders.Add("x-csrf-token", csrfToken);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                // 3. GET lista de bloqueados com paginação (novo endpoint exige count)
                var allBlockedUserIds = new List<string>();
                string cursor = "";
                int pageNum = 0;

                while (true)
                {
                    pageNum++;
                    string getUrl = "https://apis.roblox.com/user-blocking-api/v1/users/get-blocked-users?count=50"
                        + (string.IsNullOrEmpty(cursor) ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");

                    var blockedResponse = await client.GetAsync(getUrl);
                    var blockedContent = await blockedResponse.Content.ReadAsStringAsync();

                    AddLog($"🔍 [{account.Username}] GetBlockedUsers page {pageNum}: [{(int)blockedResponse.StatusCode}] {blockedContent.Substring(0, Math.Min(blockedContent.Length, 500))}");

                    if (!blockedResponse.IsSuccessStatusCode)
                    {
                        AddLogWarning($"⚠️ [{account.Username}] GetBlockedUsers falhou.");
                        return -1;
                    }

                    var blockedData = JObject.Parse(blockedContent);

                    // Resposta: {"data":{"blockedUserIds":[...],"blockedUsers":[...],"cursor":...},"error":...}
                    var dataObj = blockedData["data"] as JObject ?? blockedData;

                    // Usar blockedUserIds (array simples de longs) como fonte principal
                    var blockedUserIds = dataObj["blockedUserIds"] as JArray;
                    if (blockedUserIds != null && blockedUserIds.Count > 0)
                    {
                        foreach (var id in blockedUserIds)
                        {
                            string uid = id.ToString();
                            if (!string.IsNullOrEmpty(uid))
                                allBlockedUserIds.Add(uid);
                        }
                    }
                    else
                    {
                        // Fallback: tentar blockedUsers array com objetos
                        var blockedUsers = dataObj["blockedUsers"] as JArray;
                        if (blockedUsers != null && blockedUsers.Count > 0)
                        {
                            foreach (var user in blockedUsers)
                            {
                                string userId = user["blockedUserId"]?.ToString()
                                    ?? user["userId"]?.ToString()
                                    ?? user["UserId"]?.ToString()
                                    ?? user["id"]?.ToString();
                                if (!string.IsNullOrEmpty(userId))
                                    allBlockedUserIds.Add(userId);
                            }
                        }
                        else
                            break; // Nenhum bloqueado encontrado
                    }

                    // Verificar se há próxima página
                    string nextCursor = dataObj["cursor"]?.ToString()
                        ?? blockedData["nextCursor"]?.ToString()
                        ?? blockedData["pagingToken"]?.ToString();

                    if (string.IsNullOrEmpty(nextCursor))
                        break;

                    cursor = nextCursor;
                }

                int blockedCount = allBlockedUserIds.Count;
                AddLog($"📋 [{account.Username}] Total de usuários bloqueados: {blockedCount}");

                if (blockedCount == 0)
                {
                    AddLog($"📋 [{account.Username}] Nenhum usuário bloqueado.");
                    return 0;
                }

                // 5. Desbloquear cada um (novo endpoint)
                int unblocked = 0;
                foreach (var userId in allBlockedUserIds)
                {
                    try
                    {
                        var unblockRes = await client.PostAsync(
                            $"https://apis.roblox.com/user-blocking-api/v1/users/{userId}/unblock-user", null);

                        if (unblockRes.IsSuccessStatusCode)
                        {
                            unblocked++;
                            if (unblocked % 5 == 0 || unblocked == blockedCount)
                                AddLog($"🔓 [{account.Username}] {unblocked}/{blockedCount} desbloqueados...");
                        }
                        else
                        {
                            var errContent = await unblockRes.Content.ReadAsStringAsync();
                            AddLogWarning($"⚠️ [{account.Username}] Falha ao desbloquear {userId}: [{(int)unblockRes.StatusCode}] {errContent}");

                            if ((int)unblockRes.StatusCode == 429)
                            {
                                AddLog($"⏳ [{account.Username}] Rate limited, aguardando 20s...");
                                await Task.Delay(20000);
                                var retryRes = await client.PostAsync(
                                    $"https://apis.roblox.com/user-blocking-api/v1/users/{userId}/unblock-user", null);
                                if (retryRes.IsSuccessStatusCode) unblocked++;
                            }
                        }

                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        AddLogError($"❌ [{account.Username}] Erro unblock {userId}: {ex.Message}");
                    }
                }

                AddLogSuccess($"✅ [{account.Username}] {unblocked}/{blockedCount} desbloqueados!");
                return unblocked;
            }
        }

        private void accountUtilitiesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AccountUtilsForm ??= new AccountUtils();
            AccountUtilsForm.ApplyTheme();
            
            if (AccountUtilsForm.Visible)
            {
                AccountUtilsForm.WindowState = FormWindowState.Normal;
                AccountUtilsForm.BringToFront();
            }
            else
                AccountUtilsForm.Show();
        }

        private void AccountManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (IsDownloadingChromium && !Utilities.YesNoPrompt("Roblox Account Manager", $"{(PuppeteerSupported ? "Chromium" : "CefSharp")} is still being downloaded, exiting may corrupt your chromium installation and prevent account manager from working", "Exit anyways?", false))
            {
                e.Cancel = true;

                return;
            }

            Classes.InventorySyncService.Instance.Stop();
            AltManagerWS?.Stop();

            // Limpar hotkey Bring to Front
            if (_bringToFrontHotkeyId != 0)
                UnregisterHotKey(Handle, _bringToFrontHotkeyId);

            // Parar timer de mute
            _robloxMuteTimer?.Stop();
            _robloxMuteTimer?.Dispose();

            // Matar processos zumbis do Roblox ao fechar o app
            if (General.Get<bool>("CloseRobloxOnExit"))
            {
                foreach (Process p in Utilities.GetRobloxProcesses())
                    try { p.Kill(); } catch { }
            }

            if (PlaceID == null || string.IsNullOrEmpty(PlaceID.Text)) return;

            General.Set("SavedPlaceId", PlaceID.Text);
            IniSettings.Save("RAMSettings.ini");
        }

        private void BrowserButton_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("No Account Selected!");
                return;
            }

            UtilsForm.Show();
            UtilsForm.WindowState = FormWindowState.Normal;
            UtilsForm.BringToFront();
        }

        private void getAuthenticationTicketToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount != null)
            {
                if (SelectedAccount.GetAuthTicket(out string STicket))
                    Clipboard.SetText(STicket);

                return;
            }

            if (SelectedAccounts.Count < 1) return;

            List<string> Tickets = new List<string>();

            foreach (Account acc in SelectedAccounts)
            {
                if (acc.GetAuthTicket(out string Ticket))
                    Tickets.Add($"{acc.Username}:{Ticket}");
            }

            if (Tickets.Count > 0)
                Clipboard.SetText(string.Join("\n", Tickets));
        }

        private void copyRbxplayerLinkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null) return;

            if (SelectedAccount.GetAuthTicket(out string Ticket))
            {
                bool HasJobId = string.IsNullOrEmpty(JobID.Text);
                double LaunchTime = Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds * 1000);

                Random r = new Random();
                Clipboard.SetText(string.Format("<roblox-player://1/1+launchmode:play+gameinfo:{0}+launchtime:{4}+browsertrackerid:{5}+placelauncherurl:https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame{3}&placeId={1}{2}+robloxLocale:en_us+gameLocale:en_us>", Ticket, PlaceID.Text, HasJobId ? "" : ("&gameId=" + JobID.Text), HasJobId ? "" : "Job", LaunchTime, r.Next(100000, 130000).ToString() + r.Next(100000, 900000).ToString()));
            }
        }

        private void ArgumentsB_Click(object sender, EventArgs e)
        {
            if (afform != null)
                if (afform.Visible)
                    afform.HideForm();
                else
                    afform.ShowForm();
        }

        private void copySecurityTokenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Tokens = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Tokens.Add(account.SecurityToken);

            Clipboard.SetText(string.Join("\n", Tokens));
        }

        /// <summary>
        /// Bypass Cookie AUTOMÁTICO - Detecta país e faz bypass sem input
        /// </summary>
        private async void bypassCookieToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                AddLog("❌ Nenhuma conta selecionada!");
                return;
            }

            AddLog($"🔄 Iniciando bypass automático para {SelectedAccount.Username}...");

            try
            {
                var bypass = new Classes.CookieBypass();
                var (success, newCookie, detectedCountry, error) = await bypass.AutoBypassAsync(
                    SelectedAccount.SecurityToken
                );

                if (success && !string.IsNullOrEmpty(newCookie))
                {
                    SelectedAccount.SecurityToken = newCookie;
                    AccountsView.RefreshObject(SelectedAccount);
                    SaveAccounts();
                    
                    AddLog($"✅ Bypass concluído! País: {detectedCountry}");
                    MessageBox.Show(
                        $"Bypass concluído com sucesso!\n\nPaís detectado: {detectedCountry}\nCookie atualizado automaticamente.",
                        "Bypass Automático",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    AddLog($"❌ Falha: {error}");
                    MessageBox.Show($"Falha no bypass:\n\n{error}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Exceção: {ex.Message}");
            }
        }

        private async void bypassAllCookiesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedAccounts = AccountsView.SelectedObjects.Cast<Account>().ToList();
            
            if (selectedAccounts.Count == 0)
            {
                AddLog("❌ Nenhuma conta selecionada!");
                return;
            }

            var result = MessageBox.Show(
                $"Bypass AUTOMÁTICO de {selectedAccounts.Count} conta(s)?\n\nO país será detectado automaticamente para cada conta.",
                "Bypass em Massa (Automático)",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            AddLog($"🔄 Iniciando bypass automático em massa ({selectedAccounts.Count} contas)...");

            int successCount = 0, failCount = 0;

            foreach (var account in selectedAccounts)
            {
                AddLog($"🔄 {account.Username}...");
                
                try
                {
                    var bypass = new Classes.CookieBypass();
                    var (success, newCookie, detectedCountry, error) = await bypass.AutoBypassAsync(account.SecurityToken);

                    if (success && !string.IsNullOrEmpty(newCookie))
                    {
                        account.SecurityToken = newCookie;
                        AccountsView.RefreshObject(account);
                        successCount++;
                        AddLog($"✅ {account.Username}: OK! ({detectedCountry})");
                    }
                    else
                    {
                        failCount++;
                        AddLog($"❌ {account.Username}: {error}");
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    AddLog($"❌ {account.Username}: {ex.Message}");
                }

                await Task.Delay(1500);
            }

            SaveAccounts();
            AddLog($"📊 Concluído: {successCount} OK, {failCount} falhas");
            MessageBox.Show($"Bypass em massa concluído!\n\nSucesso: {successCount}\nFalhas: {failCount}", "Resultado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string ShowInputDialog(string text, string title, string defaultValue = "")
        {
            Form prompt = new Form()
            {
                Width = 400, Height = 180,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = title,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false, MinimizeBox = false
            };
            
            Label textLabel = new Label() { Left = 10, Top = 10, Width = 370, Height = 60, Text = text };
            TextBox textBox = new TextBox() { Left = 10, Top = 75, Width = 360, Text = defaultValue };
            Button ok = new Button() { Text = "OK", Left = 210, Width = 75, Top = 105, DialogResult = DialogResult.OK };
            Button cancel = new Button() { Text = "Cancelar", Left = 295, Width = 75, Top = 105, DialogResult = DialogResult.Cancel };
            
            prompt.Controls.Add(textLabel);
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(ok);
            prompt.Controls.Add(cancel);
            prompt.AcceptButton = ok;
            prompt.CancelButton = cancel;
            
            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : null;
        }

        private void copyUsernameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Usernames = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Usernames.Add(account.Username);

            Clipboard.SetText(string.Join("\n", Usernames));
        }

        private void copyPasswordToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Passwords = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Passwords.Add($"{account.Password}");

            Clipboard.SetText(string.Join("\n", Passwords));
        }

        private void copyUserPassComboToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Combos = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Combos.Add($"{account.Username}:{account.Password}");

            Clipboard.SetText(string.Join("\n", Combos));
        }

        private void copyUserIdToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> UserIds = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                UserIds.Add(account.UserID.ToString());

            Clipboard.SetText(string.Join("\n", UserIds));
        }

        private void PlaceID_TextChanged(object sender, EventArgs e)
        {
            if (PlaceTimer.Enabled) PlaceTimer.Stop();

            LabelPlaceID.Text = "ID do Jogo";

            PlaceTimer.Start();
        }

        private async void PlaceTimer_Tick(object sender, EventArgs e)
        {
            if (EconClient == null) return;

            PlaceTimer.Stop();

            string placeText = PlaceID.Text.Trim();
            if (string.IsNullOrEmpty(placeText) || !long.TryParse(placeText, out long placeIdValue) || placeIdValue <= 0)
            {
                LabelPlaceID.Text = "ID do Jogo";
                return;
            }

            RestRequest request = new RestRequest($"v2/assets/{placeText}/details", Method.Get);
            request.AddHeader("Accept", "application/json");
            RestResponse response = await EconClient.ExecuteAsync(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK && response.Content.StartsWith("{") && response.Content.EndsWith("}"))
            {
                ProductInfo placeInfo = JsonConvert.DeserializeObject<ProductInfo>(response.Content);

                if (placeInfo != null && !string.IsNullOrEmpty(placeInfo.Name))
                    LabelPlaceID.Text = placeInfo.Name;
                else
                    LabelPlaceID.Text = "ID do Jogo";
            }
        }

        private void moveToToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (AccountsView.SelectedObjects.Count == 0) return;

            string GroupName = ShowDialog("Group Name", "Move Account to Group", SelectedAccount != null ? SelectedAccount.Group : string.Empty);

            if (GroupName == "/UC") return; // User Cancelled
            if (string.IsNullOrEmpty(GroupName)) GroupName = "Default";

            foreach (Account acc in AccountsView.SelectedObjects)
                acc.Group = GroupName;

            RefreshView();
            SaveAccounts();
        }

        /// <summary>
        /// Popula o menu Groups dinamicamente com todos os grupos existentes
        /// </summary>
        // Lista estática de grupos customizados (grupos criados sem contas)
        private static HashSet<string> _customGroups = new HashSet<string>();
        private static bool _customGroupsLoaded = false;

        private void moveGroupUpToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            moveGroupUpToolStripMenuItem.DropDownItems.Clear();

            // Carregar grupos customizados do arquivo (só uma vez)
            if (!_customGroupsLoaded)
            {
                LoadCustomGroupsFromFile();
                _customGroupsLoaded = true;
            }

            // Coletar todos os grupos únicos das contas
            var groups = new HashSet<string>();
            
            // Adicionar "Default" sempre
            groups.Add("Default");
            
            // Adicionar grupos customizados salvos
            foreach (var cg in _customGroups)
                groups.Add(cg);
            
            // Pegar grupos de todas as contas
            if (AccountsView.Objects != null)
            {
                foreach (object obj in AccountsView.Objects)
                {
                    if (obj is Account acc && !string.IsNullOrEmpty(acc.Group))
                    {
                        groups.Add(acc.Group);
                    }
                }
            }

            // Criar item para cada grupo (clica = move contas selecionadas)
            foreach (var groupName in groups.OrderBy(g => g))
            {
                var item = new ToolStripMenuItem(groupName);
                string targetGroup = groupName;
                
                item.Click += (s, args) =>
                {
                    if (AccountsView.SelectedObjects.Count == 0)
                    {
                        MessageBox.Show("Selecione uma ou mais contas para mover para este grupo!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    foreach (Account acc in AccountsView.SelectedObjects)
                        acc.Group = targetGroup;

                    RefreshView();
                    SaveAccounts();
                    AddLog($"✅ Conta(s) movida(s) para '{targetGroup}'");
                };
                moveGroupUpToolStripMenuItem.DropDownItems.Add(item);
            }

            // Separador
            moveGroupUpToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            
            // Opção de criar novo grupo (SÓ CRIA, não move contas)
            var newGroupItem = new ToolStripMenuItem("➕ Novo Grupo...");
            newGroupItem.Click += (s, args) =>
            {
                string newName = ShowDialog("Nome do Grupo", "Criar Novo Grupo", "");
                if (newName == "/UC" || string.IsNullOrEmpty(newName)) return;

                // Salvar grupo na lista de grupos customizados
                _customGroups.Add(newName);
                SaveCustomGroupsToFile();

                AddLog($"✅ Grupo '{newName}' criado!");
                MessageBox.Show($"Grupo '{newName}' criado!\n\nPara mover contas, selecione-as e clique no nome do grupo.", "Grupo Criado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            moveGroupUpToolStripMenuItem.DropDownItems.Add(newGroupItem);
        }
        
        private void LoadCustomGroupsFromFile()
        {
            try
            {
                string path = Path.Combine(Environment.CurrentDirectory, "CustomGroups.txt");
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            _customGroups.Add(line.Trim());
                        }
                    }
                }
            }
            catch { }
        }
        
        private void SaveCustomGroupsToFile()
        {
            try
            {
                string path = Path.Combine(Environment.CurrentDirectory, "CustomGroups.txt");
                File.WriteAllLines(path, _customGroups);
            }
            catch { }
        }

        private void copyGroupToolStripMenuItem_Click(object sender, EventArgs e) => Clipboard.SetText(SelectedAccount?.Group ?? "No Account Selected");

        private void copyAppLinkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null) return;

            if (SelectedAccount.GetAuthTicket(out string Ticket))
            {
                double LaunchTime = Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds * 1000);

                Random r = new Random();
                Clipboard.SetText(string.Format("<roblox-player://1/1+launchmode:app+gameinfo:{0}+launchtime:{1}+browsertrackerid:{2}+robloxLocale:en_us+gameLocale:en_us>", Ticket, LaunchTime, r.Next(500000, 600000).ToString() + r.Next(10000, 90000).ToString()));
            }
        }

        private void JoinDiscord_Click(object sender, EventArgs e) => Process.Start("https://discord.gg/MsEH7smXY8");

        private void OpenBrowser_Click(object sender, EventArgs e)
        {
            // Se nenhuma conta selecionada, abre navegador para fazer login e adicionar conta
            if (SelectedAccount == null || AccountsView.SelectedObjects.Count == 0)
            {
                if (PuppeteerSupported)
                {
                    // Abre navegador sem conta - modo login
                    new AccountBrowser(null, "https://www.roblox.com/login", string.Empty, 
                        PostNavigation: async (page) =>
                        {
                            try
                            {
                                // Espera o usuário fazer login e redirecionar para home
                                await page.WaitForNavigationAsync(new PuppeteerSharp.NavigationOptions { Timeout = 300000 }); // 5 minutos
                                
                                // Pega os cookies após login
                                var cookies = await page.GetCookiesAsync();
                                var robloSecurity = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY");
                                
                                if (robloSecurity != null && !string.IsNullOrEmpty(robloSecurity.Value))
                                {
                                    // Adiciona conta pelo cookie
                                    this.Invoke((MethodInvoker)delegate
                                    {
                                        AddAccount(robloSecurity.Value);
                                        AddLogSuccess("✅ Conta adicionada via navegador!");
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                this.Invoke((MethodInvoker)delegate
                                {
                                    AddLogError($"❌ Erro ao capturar login: {ex.Message}");
                                });
                            }
                        });
                }
                else
                {
                    MessageBox.Show("O navegador Chromium não está disponível.\nAguarde o download automático ou reinstale o aplicativo.", "Navegador Indisponível", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            // Com conta selecionada, abre normalmente
            if (PuppeteerSupported)
                foreach (Account account in AccountsView.SelectedObjects)
                    new AccountBrowser(account);
            else if (!PuppeteerSupported && SelectedAccount != null)
                MessageBox.Show("O navegador Chromium não está disponível.\nAguarde o download automático ou reinstale o aplicativo.", "Navegador Indisponível", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Abre o navegador sempre na página de login do Roblox,
        /// independente de ter conta selecionada ou não.
        /// Usado pelo botão NAVEGADOR no painel de Adicionar Conta.
        /// </summary>
        private void OpenLoginBrowser_Click(object sender, EventArgs e)
        {
            if (PuppeteerSupported)
            {
                new AccountBrowser(null, "https://www.roblox.com/login", string.Empty,
                    PostNavigation: async (page) =>
                    {
                        try
                        {
                            await page.WaitForNavigationAsync(new PuppeteerSharp.NavigationOptions { Timeout = 300000 });

                            var cookies = await page.GetCookiesAsync();
                            var robloSecurity = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY");

                            if (robloSecurity != null && !string.IsNullOrEmpty(robloSecurity.Value))
                            {
                                this.Invoke((MethodInvoker)delegate
                                {
                                    AddAccount(robloSecurity.Value);
                                    AddLogSuccess("✅ Conta adicionada via navegador!");
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                AddLogError($"❌ Erro ao capturar login: {ex.Message}");
                            });
                        }
                    });
            }
            else
            {
                MessageBox.Show("O navegador Chromium não está disponível.\nAguarde o download automático ou reinstale o aplicativo.", "Navegador Indisponível", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void customURLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Uri.TryCreate(ShowDialog("URL", "Open Browser"), UriKind.Absolute, out Uri Link))
                if (PuppeteerSupported)
                    foreach (Account account in AccountsView.SelectedObjects)
                        new AccountBrowser(account, Link.ToString(), string.Empty);
                else if (!PuppeteerSupported && SelectedAccount != null)
                    MessageBox.Show("O navegador Chromium não está disponível.", "Navegador Indisponível", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void URLJSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Utilities.YesNoPrompt("Warning", "Your accounts may be at risk using this feature", "Do not paste in javascript unless you know what it does, your account's cookies can easily be logged through javascript.\n\nPress Yes to continue", true)) return;

            if (Uri.TryCreate(ShowDialog("URL", "Open Browser"), UriKind.Absolute, out Uri Link))
            {
                string Script = ShowDialog("Javascript", "Open Browser", big: true);

                foreach (Account account in AccountsView.SelectedObjects)
                    new AccountBrowser(account, Link.ToString(), Script);
            }
        }

        private void joinGroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Uri.TryCreate(ShowDialog("Group Link", "Open Browser"), UriKind.Absolute, out Uri Link))
            {
                foreach (Account account in AccountsView.SelectedObjects)
                    new AccountBrowser(account, Link.ToString(), PostNavigation: async (page) =>
                    {
                        await (await page.WaitForSelectorAsync("#group-join-button", new WaitForSelectorOptions() { Timeout = 12000 })).ClickAsync();
                    });
            }
        }

        private void customURLJSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int Count = 1;

            if (ModifierKeys == Keys.Shift)
                int.TryParse(ShowDialog("Amount (Limited to 15)", "Launch Browser", "1"), out Count);

            if (Uri.TryCreate(ShowDialog("URL", "Launch Browser", "https://roblox.com/"), UriKind.Absolute, out Uri Link))
            {
                string Script = ShowDialog("Javascript", "Launch Browser", big: true);

                var Size = new System.Numerics.Vector2(550, 440);
                AccountBrowser.CreateGrid(Size);

                for (int i = 0; i < Math.Min(Count, 15); i++) {
                    var Browser = new AccountBrowser() { Size = Size, Index = i };

                    _ = Browser.LaunchBrowser(Url: Link.ToString(), Script: Script, PostNavigation: async (p) => await Browser.LoginTask(p));
                }
            }
        }

        private void copyProfileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> Profiles = new List<string>();

            foreach (Account account in AccountsView.SelectedObjects)
                Profiles.Add($"https://www.roblox.com/users/{account.UserID}/profile");

            Clipboard.SetText(string.Join("\n", Profiles));
        }

        private void viewFieldsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null) return;

            FieldsForm.View(SelectedAccount);
        }

        private void SaveToAccount_Click(object sender, EventArgs e)
        {
            if (ModifierKeys == Keys.Shift)
            {
                List<Account> HasSaved = new List<Account>();

                foreach (Account account in AccountsList)
                    if (account.Fields.ContainsKey("SavedPlaceId") || account.Fields.ContainsKey("SavedJobId"))
                        HasSaved.Add(account);

                if (HasSaved.Count > 0 && MessageBox.Show($"Are you sure you want to remove {HasSaved.Count} saved Place Ids?", "Roblox Account Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.OK)
                    foreach (Account account in HasSaved)
                    {
                        account.RemoveField("SavedPlaceId");
                        account.RemoveField("SavedJobId");
                    }
            }

            foreach (Account account in AccountsView.SelectedObjects)
            {
                if (string.IsNullOrEmpty(PlaceID.Text) && string.IsNullOrEmpty(JobID.Text))
                {
                    account.RemoveField("SavedPlaceId");
                    account.RemoveField("SavedJobId");

                    return;
                }

                string PlaceId = CurrentPlaceId;

                // Support for new share link format
                if (Regex.IsMatch(JobID.Text, @"share\?code=[a-f0-9]+", RegexOptions.IgnoreCase))
                    PlaceId = "0"; // PlaceID not needed for share links
                else if (JobID.Text.Contains("privateServerLinkCode") && Regex.IsMatch(JobID.Text, @"\/games\/(\d+)\/"))
                    PlaceId = Regex.Match(CurrentJobId, @"\/games\/(\d+)\/").Groups[1].Value;

                account.SetField("SavedPlaceId", PlaceId);
                account.SetField("SavedJobId", JobID.Text);
            }
        }

        private void AccountsView_ModelCanDrop(object sender, ModelDropEventArgs e)
        {
            if (e.SourceModels[0] != null && e.SourceModels[0] is Account) e.Effect = DragDropEffects.Move;
        }

        private void AccountsView_ModelDropped(object sender, ModelDropEventArgs e)
        {
            if (e.TargetModel == null || e.SourceModels.Count == 0) return;

            Account droppedOn = e.TargetModel as Account;

            int Index = e.DropTargetIndex;

            for (int i = e.SourceModels.Count; i > 0; i--)
            {
                if (!(e.SourceModels[i - 1] is Account dragged)) continue;

                dragged.Group = droppedOn.Group;

                AccountsList.Remove(dragged);
                AccountsList.Insert(Index, dragged);
            }

            RefreshView(e.SourceModels[e.SourceModels.Count - 1]);
            SaveAccounts();
        }

        private void sortAlphabeticallyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show($"Are you sure you want to sort every account alphabetically?", "Roblox Account Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                AccountsList = AccountsList.OrderByDescending(x => x.Username.All(char.IsDigit)).ThenByDescending(x => x.Username.Any(char.IsLetter)).ThenBy(x => x.Username).ToList();

                AccountsView.SetObjects(AccountsList);
                AccountsView.BuildGroups();
            }
        }

        private async void quickLogInToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null) return;

            if (!Utilities.YesNoPrompt("Quick Log In", "Only enter codes that you requested\nNever enter another user's code", $"Do you understand?", SaveIfNo: false))
                return;

            if (Clipboard.ContainsText() && Clipboard.GetText() is string ClipCode && ClipCode.Length == 6 && await SelectedAccount.QuickLogIn(ClipCode))
                return;

            string Code = ShowDialog("Code", "Quick Log In");

            if (Code.Length != 6) { MessageBox.Show("Quick Log In codes requires 6 characters", "Quick Log In", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            await SelectedAccount.QuickLogIn(Code);
        }

        private void toggleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AccountsView.ShowGroups = !AccountsView.ShowGroups;

            if (AccountsView.HeaderStyle != ColumnHeaderStyle.None) AccountsView.HeaderStyle = AccountsView.ShowGroups ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.Clickable;

            AccountsView.BuildGroups();
        }

        private void EditTheme_Click(object sender, EventArgs e)
        {
            if (ThemeForm != null && ThemeForm.Visible)
            {
                ThemeForm.Hide();
                return;
            }

            ThemeForm.Show();
        }

        private void LaunchNexus_Click(object sender, EventArgs e)
        {
            if (ControlForm != null)
            {
                ControlForm.Top = Bottom;
                ControlForm.Left = Left;
                ControlForm.Show();
                ControlForm.BringToFront();
            }
            else
            {
                ControlForm = new AccountControl
                {
                    StartPosition = FormStartPosition.Manual,
                    Top = Bottom,
                    Left = Left
                };
                ControlForm.Show();
                ControlForm.ApplyTheme();
            }
        }

        private async Task LaunchAccounts(List<Account> Accounts, long PlaceID, string JobID, bool FollowUser = false, bool VIPServer = false)
        {
            int Delay = General.Exists("AccountJoinDelay") ? General.Get<int>("AccountJoinDelay") : 1;

            bool AsyncJoin = General.Get<bool>("AsyncJoin");
            CancellationTokenSource Token = LauncherToken;

            var tasks = new List<Task>();

            foreach (Account account in Accounts)
            {
                if (Token.IsCancellationRequested) break;

                long PlaceId = PlaceID;
                string JobId = JobID;

                if (!FollowUser)
                {
                    if (!string.IsNullOrEmpty(account.GetField("SavedPlaceId")) && long.TryParse(account.GetField("SavedPlaceId"), out long PID)) PlaceId = PID;
                    if (!string.IsNullOrEmpty(account.GetField("SavedJobId"))) JobId = account.GetField("SavedJobId");
                }

                // Lançar cada conta sem aguardar (paralelo)
                tasks.Add(account.JoinServer(PlaceId, JobId, FollowUser, VIPServer));

                // Registrar acesso no histórico (fire-and-forget)
                if (DebugModeAtivo) AddLog($"🔍 [Debug] LaunchAccounts: registrando histórico para {account.Username}, PlaceID: {PlaceId}");
                _ = SupabaseManager.Instance.LogAccountAccessAsync(account.Username, PlaceId);

                // Pequeno delay entre lançamentos para não sobrecarregar
                await Task.Delay(Delay * 1000);
            }

            // Aguardar todas as contas terminarem de lançar
            await Task.WhenAll(tasks);

            LaunchNext = false;

            Token.Cancel();
            Token.Dispose();
        }

        public void NextAccount() => LaunchNext = true;
        public void CancelLaunching()
        {
            if (LauncherToken != null && !LauncherToken.IsCancellationRequested)
                LauncherToken.Cancel();
        }

        private void infoToolStripMenuItem1_Click(object sender, EventArgs e) =>
            MessageBox.Show("Roblox Account Manager created by ic3w0lf under the GNU GPLv3 license.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

        private void groupsToolStripMenuItem_Click(object sender, EventArgs e) =>
            MessageBox.Show("Groups can be sorted by naming them a number then whatever you want.\nFor example: You can put Group Apple on top by naming it '001 Apple' or '1Apple'.\nThe numbers will be hidden from the name but will be correctly sorted depending on the number.\nAccounts can also be dragged into groups.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

        private void DonateButton_Click(object sender, EventArgs e) =>
            Process.Start("https://ic3w0lf22.github.io/donate.html");

        private void ConfigButton_Click(object sender, EventArgs e)
        {
            SettingsForm ??= new SettingsForm();

            if (SettingsForm.Visible)
            {
                SettingsForm.WindowState = FormWindowState.Normal;
                SettingsForm.BringToFront();
            }
            else
                SettingsForm.Show();

            SettingsForm.StartPosition = FormStartPosition.Manual;
            SettingsForm.Top = Top;
            SettingsForm.Left = Right;
        }

        public void ShowLogsPopup()
        {
            Form logsPopup = new Form();
            logsPopup.Text = "📋 Logs - Blox Brasil";
            logsPopup.Size = new System.Drawing.Size(800, 600);
            logsPopup.StartPosition = FormStartPosition.CenterParent;
            logsPopup.BackColor = ThemeEditor.FormsBackground;
            logsPopup.Icon = this.Icon;

            RichTextBox logsTextBox = new RichTextBox();
            logsTextBox.Dock = DockStyle.Fill;
            logsTextBox.BackColor = ThemeEditor.TextBoxesBackground;
            logsTextBox.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
            logsTextBox.Font = new System.Drawing.Font("Consolas", 10F);
            logsTextBox.ReadOnly = true;
            logsTextBox.BorderStyle = BorderStyle.None;
            logsTextBox.Text = DebugLogTextBox.Text;

            Button closeButton = new Button();
            closeButton.Text = "FECHAR";
            closeButton.Dock = DockStyle.Bottom;
            closeButton.Height = 40;
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.BackColor = ThemeEditor.ButtonsBackground;
            closeButton.ForeColor = ThemeEditor.ButtonsForeground;
            closeButton.Font = new System.Drawing.Font("Segoe UI Semibold", 10F);
            closeButton.Click += (s, args) => logsPopup.Close();

            logsPopup.Controls.Add(logsTextBox);
            logsPopup.Controls.Add(closeButton);
            logsPopup.ShowDialog(this);
        }

        private void DescarteButton_Click(object sender, EventArgs e)
        {
            ShowDescartePopup();
        }

        private void ShowDescartePopup()
        {
            Form popup = new Form();
            popup.Text = "🗑️ Descarte de Contas";
            popup.Size = new System.Drawing.Size(700, 550);
            popup.MinimumSize = new System.Drawing.Size(500, 400);
            popup.StartPosition = FormStartPosition.CenterParent;
            popup.BackColor = ThemeEditor.FormsBackground;
            popup.ForeColor = ThemeEditor.FormsForeground;
            popup.MaximizeBox = false;
            popup.MinimizeBox = false;
            popup.Icon = this.Icon;

            var font = new System.Drawing.Font("Segoe UI", 9F);
            var fontSmall = new System.Drawing.Font("Segoe UI", 7.5F);
            var fontBold = new System.Drawing.Font("Segoe UI Semibold", 9F);

            // ===== Painel superior (formulário) =====
            var formPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 195,
                BackColor = ThemeEditor.FormsBackground
            };

            int y = 8;
            int labelWidth = 55;
            int fieldX = 68;
            int fieldWidth = 120;

            // Login
            var lblLogin = new Label { Text = "Login:", Font = font, Location = new System.Drawing.Point(8, y + 3), Size = new System.Drawing.Size(labelWidth, 20), ForeColor = ThemeEditor.FormsForeground };
            var txtLogin = new TextBox { Font = font, Location = new System.Drawing.Point(fieldX, y), Size = new System.Drawing.Size(fieldWidth, 23), BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            formPanel.Controls.Add(lblLogin);
            formPanel.Controls.Add(txtLogin);
            y += 28;

            // Senha
            var lblSenha = new Label { Text = "Senha:", Font = font, Location = new System.Drawing.Point(8, y + 3), Size = new System.Drawing.Size(labelWidth, 20), ForeColor = ThemeEditor.FormsForeground };
            var txtSenha = new TextBox { Font = font, Location = new System.Drawing.Point(fieldX, y), Size = new System.Drawing.Size(fieldWidth, 23), BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            formPanel.Controls.Add(lblSenha);
            formPanel.Controls.Add(txtSenha);
            y += 28;

            // Cookie
            var lblCookie = new Label { Text = "Cookie:", Font = font, Location = new System.Drawing.Point(8, y + 3), Size = new System.Drawing.Size(labelWidth, 20), ForeColor = ThemeEditor.FormsForeground };
            var txtCookie = new TextBox { Font = font, Location = new System.Drawing.Point(fieldX, y), Size = new System.Drawing.Size(fieldWidth, 23), BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            formPanel.Controls.Add(lblCookie);
            formPanel.Controls.Add(txtCookie);
            y += 28;

            // Motivo
            var lblMotivo = new Label { Text = "Motivo:", Font = font, Location = new System.Drawing.Point(8, y + 3), Size = new System.Drawing.Size(labelWidth, 20), ForeColor = ThemeEditor.FormsForeground };
            var txtMotivo = new TextBox { Font = font, Location = new System.Drawing.Point(fieldX, y), Size = new System.Drawing.Size(fieldWidth, 40), BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground, Multiline = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            formPanel.Controls.Add(lblMotivo);
            formPanel.Controls.Add(txtMotivo);
            y += 48;

            // Botões + Status (na mesma linha)
            var btnSalvar = new Button
            {
                Text = "SALVAR DESCARTE",
                Font = fontBold,
                FlatStyle = FlatStyle.Flat,
                BackColor = System.Drawing.Color.FromArgb(140, 60, 60),
                ForeColor = System.Drawing.Color.White,
                Size = new System.Drawing.Size(150, 28),
                Location = new System.Drawing.Point(8, y),
                Cursor = Cursors.Hand
            };
            btnSalvar.FlatAppearance.BorderSize = 0;

            var btnFechar = new Button
            {
                Text = "FECHAR",
                Font = fontBold,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeEditor.ButtonsBackground,
                ForeColor = ThemeEditor.ButtonsForeground,
                Size = new System.Drawing.Size(90, 28),
                Location = new System.Drawing.Point(165, y),
                Cursor = Cursors.Hand
            };
            btnFechar.FlatAppearance.BorderSize = 0;
            btnFechar.Click += (s, args) => popup.Close();

            var lblStatus = new Label { Text = "", Font = fontSmall, Location = new System.Drawing.Point(265, y + 6), Size = new System.Drawing.Size(400, 18), ForeColor = System.Drawing.Color.FromArgb(52, 211, 153), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            formPanel.Controls.Add(btnSalvar);
            formPanel.Controls.Add(btnFechar);
            formPanel.Controls.Add(lblStatus);

            // ===== Separador =====
            var separator = new Label
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = System.Drawing.Color.FromArgb(60, 60, 70)
            };

            // ===== Label do título da lista =====
            var lblListTitle = new Label
            {
                Text = "Contas Descartadas — carregando...",
                Font = fontBold,
                ForeColor = ThemeEditor.FormsForeground,
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(8, 4, 0, 0)
            };

            // ===== ListView =====
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = ThemeEditor.TextBoxesBackground,
                ForeColor = ThemeEditor.TextBoxesForeground,
                Font = fontSmall,
                BorderStyle = BorderStyle.None,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            listView.Columns.Add("Login", 130);
            listView.Columns.Add("Senha", 100);
            listView.Columns.Add("Cookie", 120);
            listView.Columns.Add("Motivo", 180);
            listView.Columns.Add("Data", 110);

            // Menu de contexto para a lista
            var listContextMenu = new ContextMenuStrip();
            listContextMenu.BackColor = ThemeEditor.ItemBackground;
            listContextMenu.ForeColor = ThemeEditor.FormsForeground;

            var copyLoginItem = new ToolStripMenuItem("📋 Copiar Login(s)");
            copyLoginItem.Click += (s, args) =>
            {
                if (listView.SelectedItems.Count == 0) return;
                var lines = new System.Collections.Generic.List<string>();
                foreach (ListViewItem item in listView.SelectedItems)
                    lines.Add(item.Text);
                try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); } catch { }
            };

            var copySenhaItem = new ToolStripMenuItem("📋 Copiar Senha(s)");
            copySenhaItem.Click += (s, args) =>
            {
                if (listView.SelectedItems.Count == 0) return;
                var lines = new System.Collections.Generic.List<string>();
                foreach (ListViewItem item in listView.SelectedItems)
                    lines.Add(item.SubItems[1].Text);
                try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); } catch { }
            };

            var copyCookieItem = new ToolStripMenuItem("📋 Copiar Cookie(s)");
            copyCookieItem.Click += (s, args) =>
            {
                if (listView.SelectedItems.Count == 0) return;
                var lines = new System.Collections.Generic.List<string>();
                foreach (ListViewItem item in listView.SelectedItems)
                    lines.Add(item.SubItems[2].Text);
                try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); } catch { }
            };

            var copyAllItem = new ToolStripMenuItem("📋 Copiar Tudo (login:senha:cookie)");
            copyAllItem.Click += (s, args) =>
            {
                if (listView.SelectedItems.Count == 0) return;
                var lines = new System.Collections.Generic.List<string>();
                foreach (ListViewItem item in listView.SelectedItems)
                    lines.Add($"{item.Text}:{item.SubItems[1].Text}:{item.SubItems[2].Text}");
                try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); } catch { }
            };

            var deleteItem = new ToolStripMenuItem("🗑️ Remover");
            deleteItem.Click += async (s, args) =>
            {
                if (listView.SelectedItems.Count == 0) return;
                int count = listView.SelectedItems.Count;

                string msg = count == 1
                    ? $"Remover '{listView.SelectedItems[0].Text}' da lista de descarte?"
                    : $"Remover {count} contas da lista de descarte?";

                var confirm = MessageBox.Show(msg, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;

                var toRemove = new System.Collections.Generic.List<ListViewItem>();
                foreach (ListViewItem item in listView.SelectedItems)
                    toRemove.Add(item);

                foreach (var item in toRemove)
                {
                    int id = (int)item.Tag;
                    bool success = await Classes.SupabaseManager.Instance.DeleteDiscardedAccountAsync(id);
                    if (success)
                        listView.Items.Remove(item);
                }

                lblListTitle.Text = $"Contas Descartadas ({listView.Items.Count})";
            };

            listContextMenu.Items.AddRange(new ToolStripItem[] { copyLoginItem, copySenhaItem, copyCookieItem, copyAllItem, new ToolStripSeparator(), deleteItem });
            listView.ContextMenuStrip = listContextMenu;

            // Montar form: ordem importa (Fill deve ser adicionado primeiro)
            popup.Controls.Add(listView);
            popup.Controls.Add(lblListTitle);
            popup.Controls.Add(separator);
            popup.Controls.Add(formPanel);

            // Carregar lista
            Func<System.Threading.Tasks.Task> loadList = async () =>
            {
                var accounts = await Classes.SupabaseManager.Instance.GetDiscardedAccountsAsync();
                listView.Items.Clear();
                foreach (var acc in accounts)
                {
                    var item = new ListViewItem(acc.Login);
                    item.SubItems.Add(acc.Password ?? "");
                    item.SubItems.Add(acc.Cookie ?? "");
                    item.SubItems.Add(acc.Reason ?? "");
                    item.SubItems.Add(acc.CreatedAt.ToLocalTime().ToString("dd/MM/yy HH:mm"));
                    item.Tag = acc.Id;
                    listView.Items.Add(item);
                }
                lblListTitle.Text = $"Contas Descartadas ({accounts.Count})";
            };

            // Salvar
            btnSalvar.Click += async (s, args) =>
            {
                string login = txtLogin.Text.Trim();
                string senha = txtSenha.Text.Trim();
                string cookie = txtCookie.Text.Trim();
                string motivo = txtMotivo.Text.Trim();

                if (string.IsNullOrEmpty(login))
                {
                    MessageBox.Show("Preencha o login.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                btnSalvar.Enabled = false;
                btnSalvar.Text = "Salvando...";
                lblStatus.Text = "";

                try
                {
                    bool success = await Classes.SupabaseManager.Instance.AddDiscardedAccountAsync(login, senha, cookie, motivo);
                    if (success)
                    {
                        lblStatus.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
                        lblStatus.Text = $"'{login}' descartada!";
                        txtLogin.Clear();
                        txtSenha.Clear();
                        txtCookie.Clear();
                        txtMotivo.Clear();
                        txtLogin.Focus();
                        await loadList();
                    }
                    else
                    {
                        lblStatus.ForeColor = System.Drawing.Color.FromArgb(230, 100, 100);
                        lblStatus.Text = "Erro ao salvar no Supabase.";
                    }
                }
                catch (Exception ex)
                {
                    lblStatus.ForeColor = System.Drawing.Color.FromArgb(230, 100, 100);
                    lblStatus.Text = $"Erro: {ex.Message}";
                }
                finally
                {
                    btnSalvar.Enabled = true;
                    btnSalvar.Text = "SALVAR DESCARTE";
                }
            };

            // Carregar ao abrir
            popup.Load += async (s, args) => await loadList();

            popup.ShowDialog(this);
        }

        public void ShowCalculadoraPopup()
        {
            // Load saved config
            string formulaBaixo = General.Get<string>("CalcFormulaBaixo");
            if (string.IsNullOrEmpty(formulaBaixo)) formulaBaixo = "((P*2)*1.5)*1.15";
            string formulaMedio = General.Get<string>("CalcFormulaMedio");
            if (string.IsNullOrEmpty(formulaMedio)) formulaMedio = "((P*2)*1.15)*1.15";
            string formulaAlto = General.Get<string>("CalcFormulaAlto");
            if (string.IsNullOrEmpty(formulaAlto)) formulaAlto = "(P*2)*1.15";
            string savedDolar = General.Get<string>("CalcValorDolar");
            if (string.IsNullOrEmpty(savedDolar)) savedDolar = "5.70";

            string[] currentFormulas = { formulaBaixo, formulaMedio, formulaAlto };
            string[] currentDolar = { savedDolar }; // array for closure mutation

            Form popup = new Form();
            popup.Text = "Calculadora de Preços";
            popup.Size = new System.Drawing.Size(530, 380);
            popup.StartPosition = FormStartPosition.CenterParent;
            popup.BackColor = ThemeEditor.FormsBackground;
            popup.ForeColor = ThemeEditor.FormsForeground;
            popup.MaximizeBox = false;
            popup.MinimizeBox = false;
            popup.FormBorderStyle = FormBorderStyle.FixedDialog;
            popup.Icon = this.Icon;

            var font = new System.Drawing.Font("Segoe UI", 9F);
            var fontBold = new System.Drawing.Font("Segoe UI Semibold", 9F);
            var fontSmall = new System.Drawing.Font("Segoe UI", 7.5F);
            var fontTitle = new System.Drawing.Font("Segoe UI Semibold", 9.5F);
            var ptBR = new System.Globalization.CultureInfo("pt-BR");

            int y = 15;

            // === Dólar display ===
            var lblDolarInfo = new Label { Text = $"Dólar: R$ {savedDolar}", Font = fontSmall, Location = new System.Drawing.Point(12, y), AutoSize = true, ForeColor = System.Drawing.Color.FromArgb(140, 140, 155) };
            popup.Controls.Add(lblDolarInfo);

            // === Config button ===
            var btnConfig = new Button
            {
                Text = "⚙ Configurações",
                Font = fontSmall,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeEditor.ButtonsBackground,
                ForeColor = ThemeEditor.ButtonsForeground,
                Size = new System.Drawing.Size(110, 22),
                Location = new System.Drawing.Point(400, y - 3),
                Cursor = Cursors.Hand
            };
            btnConfig.FlatAppearance.BorderSize = 0;
            popup.Controls.Add(btnConfig);
            y += 25;

            // Separator
            popup.Controls.Add(new Label { Location = new System.Drawing.Point(12, y), Size = new System.Drawing.Size(490, 1), BackColor = System.Drawing.Color.FromArgb(60, 60, 70) });
            y += 10;

            // === PREÇO EM REAL ===
            popup.Controls.Add(new Label { Text = "PREÇO EM REAL (R$):", Font = font, Location = new System.Drawing.Point(12, y + 3), AutoSize = true, ForeColor = ThemeEditor.FormsForeground });
            var txtPrecoReal = new TextBox { Font = font, Location = new System.Drawing.Point(180, y), Size = new System.Drawing.Size(100, 23), BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground };
            popup.Controls.Add(txtPrecoReal);
            y += 32;

            // === PREÇO EM DÓLAR ===
            popup.Controls.Add(new Label { Text = "PREÇO EM DÓLAR ($):", Font = font, Location = new System.Drawing.Point(12, y + 3), AutoSize = true, ForeColor = ThemeEditor.FormsForeground });
            var txtPrecoDolar = new TextBox { Font = font, Location = new System.Drawing.Point(180, y), Size = new System.Drawing.Size(100, 23), BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground };
            popup.Controls.Add(txtPrecoDolar);
            y += 40;

            // Link inputs: typing in one auto-updates the other
            bool _isUpdating = false;
            txtPrecoReal.TextChanged += (s, args) =>
            {
                if (_isUpdating) return;
                _isUpdating = true;
                string dolarStr = currentDolar[0].Replace(",", ".");
                if (double.TryParse(dolarStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dolar) && dolar > 0)
                {
                    string realStr = txtPrecoReal.Text.Replace(",", ".");
                    if (double.TryParse(realStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double real))
                        txtPrecoDolar.Text = (real / dolar).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    else
                        txtPrecoDolar.Text = "";
                }
                _isUpdating = false;
            };
            txtPrecoDolar.TextChanged += (s, args) =>
            {
                if (_isUpdating) return;
                _isUpdating = true;
                string dolarStr = currentDolar[0].Replace(",", ".");
                if (double.TryParse(dolarStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dolar) && dolar > 0)
                {
                    string usdStr = txtPrecoDolar.Text.Replace(",", ".");
                    if (double.TryParse(usdStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double usd))
                        txtPrecoReal.Text = (usd * dolar).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    else
                        txtPrecoReal.Text = "";
                }
                _isUpdating = false;
            };

            // === CALCULAR button ===
            var btnCalcular = new Button
            {
                Text = "CALCULAR",
                Font = fontBold,
                FlatStyle = FlatStyle.Flat,
                BackColor = System.Drawing.Color.FromArgb(0, 110, 70),
                ForeColor = System.Drawing.Color.White,
                Size = new System.Drawing.Size(110, 28),
                Location = new System.Drawing.Point(12, y),
                Cursor = Cursors.Hand
            };
            btnCalcular.FlatAppearance.BorderSize = 0;
            var lblTierIndicator = new Label { Text = "", Font = fontSmall, Location = new System.Drawing.Point(130, y + 8), AutoSize = true, ForeColor = System.Drawing.Color.FromArgb(52, 211, 153) };
            popup.Controls.Add(btnCalcular);
            popup.Controls.Add(lblTierIndicator);
            y += 40;

            // Separator
            popup.Controls.Add(new Label { Location = new System.Drawing.Point(12, y), Size = new System.Drawing.Size(490, 1), BackColor = System.Drawing.Color.FromArgb(60, 60, 70) });
            y += 8;

            // === 3 Tiers (results only) ===
            var lblResults = new Label[3];
            var lblTierTitles = new Label[3];

            string[] tierNames = { "TICKET BAIXO (< $5 / < R$25)", "TICKET MÉDIO ($5 ~ $20)", "TICKET ALTO (> $20)" };

            for (int i = 0; i < 3; i++)
            {
                lblTierTitles[i] = new Label { Text = tierNames[i], Font = fontTitle, Location = new System.Drawing.Point(12, y), AutoSize = true, ForeColor = System.Drawing.Color.FromArgb(170, 170, 185) };
                popup.Controls.Add(lblTierTitles[i]);
                y += 22;

                lblResults[i] = new Label { Text = "PREÇO SUGERIDO: --", Font = fontBold, Location = new System.Drawing.Point(22, y), AutoSize = true, ForeColor = System.Drawing.Color.FromArgb(52, 211, 153) };
                popup.Controls.Add(lblResults[i]);
                y += 25;

                if (i < 2)
                {
                    popup.Controls.Add(new Label { Location = new System.Drawing.Point(12, y), Size = new System.Drawing.Size(490, 1), BackColor = System.Drawing.Color.FromArgb(50, 50, 60) });
                    y += 6;
                }
            }

            // Formula evaluator using DataTable.Compute
            Func<string, double, double> evalFormula = (formula, price) =>
            {
                try
                {
                    string normalized = formula.Replace(",", ".");
                    string expr = normalized.Replace("P", price.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var dt = new System.Data.DataTable();
                    return Convert.ToDouble(dt.Compute(expr, ""));
                }
                catch { return -1; }
            };

            // Calculate action
            Action doCalculate = () =>
            {
                string dolarStr = currentDolar[0].Replace(",", ".");
                if (!double.TryParse(dolarStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dolar) || dolar <= 0)
                {
                    lblTierIndicator.Text = "Configure o valor do dólar";
                    lblTierIndicator.ForeColor = System.Drawing.Color.FromArgb(230, 100, 100);
                    return;
                }

                // Get price in BRL - prefer txtPrecoReal, fallback to converting from USD
                double precoReal = 0;
                string realStr = txtPrecoReal.Text.Replace(",", ".");
                string usdStr = txtPrecoDolar.Text.Replace(",", ".");

                if (!string.IsNullOrWhiteSpace(txtPrecoReal.Text) &&
                    double.TryParse(realStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out precoReal) && precoReal > 0)
                {
                    // OK, precoReal is set
                }
                else if (!string.IsNullOrWhiteSpace(txtPrecoDolar.Text) &&
                    double.TryParse(usdStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double precoUsd) && precoUsd > 0)
                {
                    precoReal = precoUsd * dolar;
                }
                else
                {
                    lblTierIndicator.Text = "Informe o preço";
                    lblTierIndicator.ForeColor = System.Drawing.Color.FromArgb(230, 100, 100);
                    return;
                }

                double precoUSD = precoReal / dolar;

                int activeTier = precoUSD < 5 ? 0 : (precoUSD < 20 ? 1 : 2);
                string[] tierLabels = { "→ Ticket Baixo", "→ Ticket Médio", "→ Ticket Alto" };
                lblTierIndicator.Text = tierLabels[activeTier];
                lblTierIndicator.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);

                for (int i = 0; i < 3; i++)
                {
                    double siteReal = evalFormula(currentFormulas[i], precoReal);
                    if (siteReal < 0)
                    {
                        lblResults[i].Text = "PREÇO SUGERIDO: ERRO";
                        lblResults[i].ForeColor = System.Drawing.Color.FromArgb(230, 100, 100);
                    }
                    else
                    {
                        lblResults[i].Text = $"PREÇO SUGERIDO: R$ {Math.Ceiling(siteReal).ToString("N0", ptBR)}";

                        if (i == activeTier)
                        {
                            lblResults[i].ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
                            lblTierTitles[i].ForeColor = System.Drawing.Color.White;
                        }
                        else
                        {
                            lblResults[i].ForeColor = System.Drawing.Color.FromArgb(100, 100, 110);
                            lblTierTitles[i].ForeColor = System.Drawing.Color.FromArgb(100, 100, 110);
                        }
                    }
                }
            };

            btnCalcular.Click += (s, args) => doCalculate();
            txtPrecoReal.KeyDown += (s, args) => { if (args.KeyCode == Keys.Enter) { doCalculate(); args.SuppressKeyPress = true; } };
            txtPrecoDolar.KeyDown += (s, args) => { if (args.KeyCode == Keys.Enter) { doCalculate(); args.SuppressKeyPress = true; } };

            // Config button opens config popup
            btnConfig.Click += (s, args) =>
            {
                ShowCalculadoraConfigPopup(currentFormulas, currentDolar);
                // Refresh dolar display
                lblDolarInfo.Text = $"Dólar: R$ {currentDolar[0]}";
            };

            popup.ShowDialog(this);
        }

        private void ShowCalculadoraConfigPopup(string[] formulas, string[] dolarRef)
        {
            Form config = new Form();
            config.Text = "Configurações da Calculadora";
            config.Size = new System.Drawing.Size(460, 370);
            config.StartPosition = FormStartPosition.CenterParent;
            config.BackColor = ThemeEditor.FormsBackground;
            config.ForeColor = ThemeEditor.FormsForeground;
            config.MaximizeBox = false;
            config.MinimizeBox = false;
            config.FormBorderStyle = FormBorderStyle.FixedDialog;
            config.Icon = this.Icon;

            var font = new System.Drawing.Font("Segoe UI", 9F);
            var fontBold = new System.Drawing.Font("Segoe UI Semibold", 9F);
            var fontSmall = new System.Drawing.Font("Segoe UI", 7.5F);
            var fontTitle = new System.Drawing.Font("Segoe UI Semibold", 9.5F);

            int y = 15;

            // === Valor do Dólar ===
            config.Controls.Add(new Label { Text = "Valor do Dólar (R$):", Font = font, Location = new System.Drawing.Point(12, y + 3), AutoSize = true, ForeColor = ThemeEditor.FormsForeground });
            var txtDolar = new TextBox { Font = font, Text = dolarRef[0], Location = new System.Drawing.Point(155, y), Size = new System.Drawing.Size(80, 23), BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground };
            config.Controls.Add(txtDolar);

            var btnBuscarDolar = new Button
            {
                Text = "Buscar Online",
                Font = fontSmall,
                FlatStyle = FlatStyle.Flat,
                BackColor = System.Drawing.Color.FromArgb(0, 120, 180),
                ForeColor = System.Drawing.Color.White,
                Size = new System.Drawing.Size(90, 23),
                Location = new System.Drawing.Point(245, y),
                Cursor = Cursors.Hand
            };
            btnBuscarDolar.FlatAppearance.BorderSize = 0;
            var lblDolarStatus = new Label { Text = "", Font = fontSmall, Location = new System.Drawing.Point(342, y + 5), AutoSize = true, ForeColor = System.Drawing.Color.FromArgb(140, 140, 155) };
            config.Controls.Add(btnBuscarDolar);
            config.Controls.Add(lblDolarStatus);

            btnBuscarDolar.Click += async (s, args) =>
            {
                btnBuscarDolar.Enabled = false;
                btnBuscarDolar.Text = "Buscando...";
                lblDolarStatus.Text = "";
                try
                {
                    using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                    {
                        string json = await client.GetStringAsync("https://economia.awesomeapi.com.br/json/last/USD-BRL");
                        // Parse "bid" value: {"USDBRL":{"bid":"5.234",...}}
                        var match = System.Text.RegularExpressions.Regex.Match(json, "\"bid\"\\s*:\\s*\"([^\"]+)\"");
                        if (match.Success)
                        {
                            txtDolar.Text = match.Groups[1].Value;
                            lblDolarStatus.Text = "Atualizado!";
                            lblDolarStatus.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
                        }
                        else
                        {
                            lblDolarStatus.Text = "Erro no parse";
                            lblDolarStatus.ForeColor = System.Drawing.Color.FromArgb(230, 100, 100);
                        }
                    }
                }
                catch
                {
                    lblDolarStatus.Text = "Erro na conexão";
                    lblDolarStatus.ForeColor = System.Drawing.Color.FromArgb(230, 100, 100);
                }
                finally
                {
                    btnBuscarDolar.Enabled = true;
                    btnBuscarDolar.Text = "Buscar Online";
                }
            };

            y += 38;

            // Separator
            config.Controls.Add(new Label { Location = new System.Drawing.Point(12, y), Size = new System.Drawing.Size(420, 1), BackColor = System.Drawing.Color.FromArgb(60, 60, 70) });
            y += 10;

            // === Fórmulas ===
            config.Controls.Add(new Label { Text = "FÓRMULAS DE PREÇO", Font = fontTitle, Location = new System.Drawing.Point(12, y), AutoSize = true, ForeColor = ThemeEditor.FormsForeground });
            y += 22;

            string[] tierNames = { "Ticket Baixo (< $5 / < R$25):", "Ticket Médio ($5 ~ $20):", "Ticket Alto (> $20):" };
            string[] defaultFormulas = { "((P*2)*1.5)*1.15", "((P*2)*1.15)*1.15", "(P*2)*1.15" };
            var txtFormulas = new TextBox[3];

            for (int i = 0; i < 3; i++)
            {
                config.Controls.Add(new Label { Text = tierNames[i], Font = fontSmall, Location = new System.Drawing.Point(12, y + 2), AutoSize = true, ForeColor = System.Drawing.Color.FromArgb(170, 170, 185) });
                y += 18;

                txtFormulas[i] = new TextBox { Font = font, Text = formulas[i], Location = new System.Drawing.Point(12, y), Size = new System.Drawing.Size(340, 23), BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground };
                config.Controls.Add(txtFormulas[i]);

                int idx = i;
                var btnRestore = new Button { Text = "↺", Font = font, FlatStyle = FlatStyle.Flat, Size = new System.Drawing.Size(28, 23), Location = new System.Drawing.Point(358, y), BackColor = ThemeEditor.ButtonsBackground, ForeColor = ThemeEditor.ButtonsForeground, Cursor = Cursors.Hand };
                btnRestore.FlatAppearance.BorderSize = 0;
                btnRestore.Click += (s, args) => txtFormulas[idx].Text = defaultFormulas[idx];
                config.Controls.Add(btnRestore);
                y += 30;
            }

            // Helper text
            config.Controls.Add(new Label { Text = "P = Preço do Fornecedor em REAIS. Operadores: +, -, *, /, ()", Font = fontSmall, Location = new System.Drawing.Point(12, y), AutoSize = true, ForeColor = System.Drawing.Color.FromArgb(100, 100, 115) });
            y += 25;

            // === SALVAR button ===
            var btnSalvar = new Button
            {
                Text = "SALVAR",
                Font = fontBold,
                FlatStyle = FlatStyle.Flat,
                BackColor = System.Drawing.Color.FromArgb(0, 110, 70),
                ForeColor = System.Drawing.Color.White,
                Size = new System.Drawing.Size(120, 30),
                Location = new System.Drawing.Point(12, y),
                Cursor = Cursors.Hand
            };
            btnSalvar.FlatAppearance.BorderSize = 0;
            var lblSaveStatus = new Label { Text = "", Font = fontSmall, Location = new System.Drawing.Point(140, y + 9), AutoSize = true };
            config.Controls.Add(btnSalvar);
            config.Controls.Add(lblSaveStatus);

            btnSalvar.Click += (s, args) =>
            {
                // Update the shared arrays so calculator popup uses new values
                formulas[0] = txtFormulas[0].Text;
                formulas[1] = txtFormulas[1].Text;
                formulas[2] = txtFormulas[2].Text;
                dolarRef[0] = txtDolar.Text;

                // Persist to INI
                General.Set("CalcFormulaBaixo", txtFormulas[0].Text);
                General.Set("CalcFormulaMedio", txtFormulas[1].Text);
                General.Set("CalcFormulaAlto", txtFormulas[2].Text);
                General.Set("CalcValorDolar", txtDolar.Text);
                IniSettings.Save("RAMSettings.ini");

                lblSaveStatus.Text = "Salvo!";
                lblSaveStatus.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
            };

            config.ShowDialog(this);
        }

        private void HistoryIcon_MouseHover(object sender, EventArgs e) => RGForm.ShowForm();


        private void ShowDetailsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "AccountDumps")))
                Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "AccountDumps"));

            foreach (Account Account in AccountsView.SelectedObjects)
            {
                Task.Run(async () =>
                {
                    var UserInfo = await Account.GetUserInfo();
                    double AccountAge = -1;

                    if (DateTime.TryParse(UserInfo["created"].Value<string>(), out DateTime CreationTime))
                        AccountAge = (DateTime.UtcNow - CreationTime).TotalDays;

                    StringBuilder builder = new StringBuilder();

                    builder.AppendLine($"Username: {Account.Username}");
                    builder.AppendLine($"UserId: {Account.UserID}");
                    builder.AppendLine($"Robux: {await Account.GetRobux()}");
                    builder.AppendLine($"Account Age: {(AccountAge >= 0 ? $"{AccountAge:F1}" : "UNKNOWN")}");
                    builder.AppendLine($"Email Status: {await Account.GetEmailJSON()}");
                    builder.AppendLine($"User Info: {UserInfo}");
                    builder.AppendLine($"Other: {await Account.GetMobileInfo()}");
                    builder.AppendLine($"Fields: {JsonConvert.SerializeObject(Account.Fields)}");

                    string FileName = Path.Combine(Environment.CurrentDirectory, "AccountDumps", Account.Username + ".txt");

                    File.WriteAllText(FileName, builder.ToString());

                    Process.Start(FileName);
                });
            }
        }

        CancellationTokenSource PresenceCancellationToken;

        private void AccountsView_Scroll(object sender, ScrollEventArgs e)
        {
            if (PresenceCancellationToken != null || !General.Get<bool>("ShowPresence"))
                PresenceCancellationToken.Cancel();

            PresenceCancellationToken = new CancellationTokenSource();
            var Token = PresenceCancellationToken.Token;

            Task.Run(async () =>
            {
                await Task.Delay(3500); // Wait until the user has stopped scrolling before updating account presence

                if (Token.IsCancellationRequested)
                    return;

                AccountsView.InvokeIfRequired(async () => await UpdatePresence());
            }, PresenceCancellationToken.Token);
        }

        private async Task UpdatePresence()
        {
            if (!General.Get<bool>("ShowPresence")) return;

            List<Account> VisibleAccounts = new List<Account>();

            var Bounds = AccountsView.ClientRectangle;
            int Padding = (int)(AccountsView.HeaderStyle == ColumnHeaderStyle.None ? 4f * Program.Scale : 20f * Program.Scale);

            for (int Y = Padding; Y < Bounds.Height - (Padding / 2); Y += (int)(6f * Program.Scale))
            {
                var Item = AccountsView.GetItemAt(4, Y);

                if (Item != null && AccountsView.GetModelObject(Item.Index) is Account account && !VisibleAccounts.Contains(account))
                    VisibleAccounts.Add(account);
            }

            try { await Presence.UpdatePresence(VisibleAccounts.Select(account => account.UserID).ToArray()); } catch { }
        }

        private void JobID_Click( object sender, EventArgs e )
        {
            JobID.SelectAll(); // Allows quick replacing of the JobID with a click and ctrl-v.
        }

        private void PlaceID_Click( object sender, EventArgs e )
        {
            PlaceID.SelectAll(); // Allows quick replacing of the PlaceID with a click and ctrl-v.
        }

        private void ClearSearchButton_Click(object sender, EventArgs e)
        {
            SearchAccountsTextBox.Clear();
            SearchAccountsTextBox.Focus();
        }

        private void ClearJobIDButton_Click(object sender, EventArgs e)
        {
            JobID.Clear();
            JobID.Focus();
        }

        private void LabelJobID_Click(object sender, EventArgs e)
        {

        }

        private void LabelUserID_Click(object sender, EventArgs e)
        {

        }

        private void HistoryIcon_Click(object sender, EventArgs e)
        {

        }

        private void PasswordPanel_Paint(object sender, PaintEventArgs e)
        {

        }

        private void GameSelectorPanel_Paint(object sender, PaintEventArgs e)
        {

        }

        private void panel2_Paint(object sender, PaintEventArgs e)
        {

        }

        private void TwoFASecretTextBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void DebugLogTextBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void LabelPlaceID_Click(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private async void sellRbxBtn_Click(object sender, EventArgs e)
        {
            // Buscar cookie do Supabase (compartilhado entre todos os usuários)
            string sharedCookie = null;
            try
            {
                sharedCookie = await Classes.SupabaseManager.Instance.GetSharedConfigAsync("topupg_cookie");
            }
            catch { }

            // Fallback: cookie padrão inicial
            if (string.IsNullOrEmpty(sharedCookie))
                sharedCookie = "eyJpdiI6IlQrVUE4NXQ4TkhxT2FzandjbkVqTEE9PSIsInZhbHVlIjoiTDhsWThTNWZxdXR1bGVoakRFdVRjSytmL0twalIzUUM3cVRhMzhqRVZ3eWFwOFFpcVhIUVplUkluQ3c1Nk15cFY0SFZleUVFc0lGREFtTUFGclRWNnJqdGU3ejZmYnZvMjd6Nnc2MklTck5qTzBuVjJOdjFBeExxZXJ3TDl3UDgiLCJtYWMiOiIxNWIzOGIxYWZkZTBhZDYzYTRlNjZkNTUwMzEwYjQzODVmNzMxMWFkOGY5ZGZmMTBkNjVjMjhhYWVlYjRhOWUxIiwidGFnIjoiIn0%3D";

            // URL-decode o cookie (ex: %3D → =)
            sharedCookie = Uri.UnescapeDataString(sharedCookie);

            var popup = new Form
            {
                Text = "SellRBX - TopUpG",
                Size = new Size(420, 330),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = ThemeEditor.FormsBackground,
                ForeColor = ThemeEditor.FormsForeground
            };

            var titleLabel = new Label
            {
                Text = "TopUpG - Dados de Login",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.LimeGreen,
                Location = new System.Drawing.Point(20, 15),
                Size = new Size(370, 30),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Usuário
            var lblUser = new Label { Text = "Usuário:", Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new System.Drawing.Point(20, 60), Size = new Size(80, 20) };
            var txtUser = new TextBox { Text = "contato@robloxbrasil.com.br", Location = new System.Drawing.Point(100, 58), Size = new Size(230, 22), ReadOnly = true, BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground };
            var btnCopyUser = new Button { Text = "Copiar", Location = new System.Drawing.Point(335, 56), Size = new Size(55, 24), FlatStyle = FlatStyle.Flat, BackColor = ThemeEditor.ButtonsBackground, ForeColor = ThemeEditor.ButtonsForeground, Cursor = Cursors.Hand };
            btnCopyUser.Click += (s, ev) => { Clipboard.SetText(txtUser.Text); btnCopyUser.Text = "OK!"; Task.Delay(1000).ContinueWith(_ => popup.Invoke((Action)(() => btnCopyUser.Text = "Copiar"))); };

            // Senha 1
            var lblPass1 = new Label { Text = "Senha 1:", Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new System.Drawing.Point(20, 95), Size = new Size(80, 20) };
            var txtPass1 = new TextBox { Text = "robloxbloxbrasil123", Location = new System.Drawing.Point(100, 93), Size = new Size(230, 22), ReadOnly = true, BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground };
            var btnCopyPass1 = new Button { Text = "Copiar", Location = new System.Drawing.Point(335, 91), Size = new Size(55, 24), FlatStyle = FlatStyle.Flat, BackColor = ThemeEditor.ButtonsBackground, ForeColor = ThemeEditor.ButtonsForeground, Cursor = Cursors.Hand };
            btnCopyPass1.Click += (s, ev) => { Clipboard.SetText(txtPass1.Text); btnCopyPass1.Text = "OK!"; Task.Delay(1000).ContinueWith(_ => popup.Invoke((Action)(() => btnCopyPass1.Text = "Copiar"))); };

            // Senha 2
            var lblPass2 = new Label { Text = "Senha 2:", Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new System.Drawing.Point(20, 130), Size = new Size(80, 20) };
            var txtPass2 = new TextBox { Text = "bloxbrasil321", Location = new System.Drawing.Point(100, 128), Size = new Size(230, 22), ReadOnly = true, BackColor = ThemeEditor.TextBoxesBackground, ForeColor = ThemeEditor.TextBoxesForeground };
            var btnCopyPass2 = new Button { Text = "Copiar", Location = new System.Drawing.Point(335, 126), Size = new Size(55, 24), FlatStyle = FlatStyle.Flat, BackColor = ThemeEditor.ButtonsBackground, ForeColor = ThemeEditor.ButtonsForeground, Cursor = Cursors.Hand };
            btnCopyPass2.Click += (s, ev) => { Clipboard.SetText(txtPass2.Text); btnCopyPass2.Text = "OK!"; Task.Delay(1000).ContinueWith(_ => popup.Invoke((Action)(() => btnCopyPass2.Text = "Copiar"))); };

            // Separador
            var separator = new Label { BorderStyle = BorderStyle.Fixed3D, Location = new System.Drawing.Point(20, 168), Size = new Size(370, 2) };

            // Botão: Abrir com Cookie (auto-login)
            string capturedCookie = sharedCookie;
            var btnOpenWithCookie = new Button
            {
                Text = "Abrir TopUpG com Cookie",
                Location = new System.Drawing.Point(20, 182),
                Size = new Size(370, 40),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnOpenWithCookie.FlatAppearance.BorderSize = 0;
            btnOpenWithCookie.Click += (s, ev) =>
            {
                var browser = new AccountBrowser();
                _ = browser.LaunchBrowser("https://topupg.com/",
                    PostPageCreation: () => browser.page.SetCookieAsync(new PuppeteerSharp.CookieParam
                    {
                        Name = "laravel_session",
                        Domain = "topupg.com",
                        Path = "/",
                        Expires = (DateTime.Now.AddYears(10) - DateTime.MinValue).TotalSeconds,
                        HttpOnly = true,
                        Secure = true,
                        Url = "https://topupg.com",
                        Value = capturedCookie
                    }),
                    PostNavigation: async (page) =>
                    {
                        // Renovar cookie: capturar atualizado e salvar no Supabase para todos
                        try
                        {
                            var cookies = await page.GetCookiesAsync("https://topupg.com");
                            foreach (var c in cookies)
                            {
                                if (c.Name == "laravel_session" && !string.IsNullOrEmpty(c.Value))
                                {
                                    await Classes.SupabaseManager.Instance.SetSharedConfigAsync("topupg_cookie", c.Value);
                                    if (DebugModeAtivo)
                                        AddLog("[TopUpG] Cookie renovado e sincronizado no Supabase");
                                    break;
                                }
                            }
                        }
                        catch { }
                    });
                popup.Close();
            };

            // Botão: Abrir sem Cookie
            var btnOpenNoCookie = new Button
            {
                Text = "Abrir TopUpG sem Cookie",
                Location = new System.Drawing.Point(20, 230),
                Size = new Size(370, 32),
                BackColor = ThemeEditor.ButtonsBackground,
                ForeColor = ThemeEditor.ButtonsForeground,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand
            };
            btnOpenNoCookie.FlatAppearance.BorderSize = 0;
            btnOpenNoCookie.Click += (s, ev) =>
            {
                var browser = new AccountBrowser();
                _ = browser.LaunchBrowser("https://topupg.com/",
                    PostNavigation: async (page) =>
                    {
                        // Se fizer login manual, capturar cookie e salvar no Supabase para todos
                        try
                        {
                            var cookies = await page.GetCookiesAsync("https://topupg.com");
                            foreach (var c in cookies)
                            {
                                if (c.Name == "laravel_session" && !string.IsNullOrEmpty(c.Value))
                                {
                                    await Classes.SupabaseManager.Instance.SetSharedConfigAsync("topupg_cookie", c.Value);
                                    if (DebugModeAtivo)
                                        AddLog("[TopUpG] Cookie capturado e sincronizado no Supabase");
                                    break;
                                }
                            }
                        }
                        catch { }
                    });
                popup.Close();
            };

            popup.Controls.AddRange(new Control[] {
                titleLabel,
                lblUser, txtUser, btnCopyUser,
                lblPass1, txtPass1, btnCopyPass1,
                lblPass2, txtPass2, btnCopyPass2,
                separator,
                btnOpenWithCookie, btnOpenNoCookie
            });

            popup.ShowDialog(this);
        }
    }
}
