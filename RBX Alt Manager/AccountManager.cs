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
using WebSocketSharp;

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
        public static bool ModoEstoqueAtivo = false;
        private static bool PuppeteerSupported;

        // Google Sheets Integration (legacy)
        // Configurações carregadas de games_config.json
        private Classes.GoogleSheetsIntegration _sheetsIntegration;
        private string SHEETS_ID => Classes.GamesConfig.Instance.SpreadsheetId;
        private string APPS_SCRIPT_URL => Classes.GamesConfig.Instance.AppsScriptUrl;

        // Painel de Inventário (Supabase)
        private Controls.InventoryPanelControl _inventoryPanel;

        // Pusher Integration (tempo real)
        private string PUSHER_APP_ID => Classes.GamesConfig.Instance.Pusher?.AppId ?? "";
        private string PUSHER_KEY => Classes.GamesConfig.Instance.Pusher?.Key ?? "";
        private string PUSHER_SECRET => Classes.GamesConfig.Instance.Pusher?.Secret ?? "";
        private string PUSHER_CLUSTER => Classes.GamesConfig.Instance.Pusher?.Cluster ?? "sa1";
        private WebSocket _pusherSocket;
        private bool _pusherConnected = false;

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
        
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
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
        private static string FormatNumberWithThousands(int number)
        {
            return number.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
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
            if (!General.Exists("UseCefSharpBrowser")) General.Set("UseCefSharpBrowser", "false");
            if (!General.Exists("UseInstalledChrome")) General.Set("UseInstalledChrome", "false");
            if (!General.Exists("EnableMultiRbx")) General.Set("EnableMultiRbx", "true");

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
                    try { AccountsList = JsonConvert.DeserializeObject<List<Account>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(Data, Entropy, DataProtectionScope.CurrentUser))); }
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

            // Inicializar conexão Pusher para atualizações em tempo real
            InitializePusher();

            // Pré-carregar dados do Google Sheets em background (otimização)
            Task.Run(async () =>
            {
                try
                {
                    if (_sheetsIntegration == null)
                        _sheetsIntegration = new Classes.GoogleSheetsIntegration(SHEETS_ID);
                    await _sheetsIntegration.PreloadAllGamesAsync();
                }
                catch { }
            });

            // Inicializar painel de inventário (Supabase)
            InitializeInventoryPanel();
            
            // Inicializar painel de amigos
            InitializeFriendsPanel();

            // Inicializar hotkey 2FA
            string savedHotkey = General.Get<string>("TwoFAHotkey");
            if (!string.IsNullOrEmpty(savedHotkey) && savedHotkey != "Desativado")
                UpdateTwoFAHotkey(savedHotkey);

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
                MessageBox.Show("WARNING: Roblox is currently running, multi roblox will not work until you restart the account manager with roblox closed.", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            int Major = Environment.OSVersion.Version.Major, Minor = Environment.OSVersion.Version.Minor;

            PuppeteerSupported = !(Major < 6 || (Major == 6 && Minor <= 1));

            if (General.Get<bool>("UseCefSharpBrowser")) PuppeteerSupported = false;

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
            else if (!PuppeteerSupported)
            {
                FileInfo Cef = new FileInfo(Path.Combine(Environment.CurrentDirectory, "x86", "CefSharp.dll"));

                if (Cef.Exists)
                {
                    FileVersionInfo Info = FileVersionInfo.GetVersionInfo(Cef.FullName);

                    if (Info.ProductMajorPart != 109)
                        try { Directory.GetParent(Cef.FullName).RecursiveDelete(); } catch { }
                }

                if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "x86")))
                {
                    var Existing = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "x86"));


                    AddUserPassButton.Visible = false;
                    AddCookieButton.Visible = false;

                    Task.Run(async () =>
                    {
                        IsDownloadingChromium = true;

                        using HttpClient client = new HttpClient();

                        string FileName = Path.GetTempFileName(), DownloadUrl = Resources.CefSharpDownload;

                        var TotalDownloadSize = (await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, DownloadUrl))).Content.Headers.ContentLength.Value;

                        using (var file = new FileStream(FileName, FileMode.Create, FileAccess.Write, FileShare.None))

                        if (Existing.Exists) Existing.RecursiveDelete();

                        System.IO.Compression.ZipFile.ExtractToDirectory(FileName, Environment.CurrentDirectory);

                        IsDownloadingChromium = false;

                        this.InvokeIfRequired(() =>
                        {
                            AddUserPassButton.Visible = true;
                            AddCookieButton.Visible = true;
                        });
                    });
                }
            }
        }

        public bool UpdateMultiRoblox()
        {
            bool Enabled = General.Get<bool>("EnableMultiRbx");

            if (Enabled && rbxMultiMutex == null)
                try
                {
                    rbxMultiMutex = new Mutex(true, "ROBLOX_singletonMutex");

                    if (!rbxMultiMutex.WaitOne(TimeSpan.Zero, true))
                        return false;
                }
                catch { return false; }
            else if (!Enabled && rbxMultiMutex != null)
            {
                rbxMultiMutex.Close();
                rbxMultiMutex = null;
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
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                $"Tem certeza que deseja REMOVER TODAS AS AMIZADES da conta '{SelectedAccount.Username}'?\n\nEssa ação não pode ser desfeita!",
                "Confirmar Remoção de Amizades",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (confirm != DialogResult.Yes) return;


            try
            {
                AddLog($"🗑️ Removendo amizades de {SelectedAccount.Username}...");
                
                int removedCount = await DeleteAllFriendsAsync(SelectedAccount);
                
                AddLogSuccess($"✅ {removedCount} amizades removidas de {SelectedAccount.Username}");
                MessageBox.Show($"{removedCount} amizades removidas com sucesso!", "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro ao remover amizades: {ex.Message}");
                MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
            }
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
                CefBrowser.Instance.Login();
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

                // Atualizar na planilha (legacy Google Sheets)
                try
                {
                    await UpdateRobuxInSheetAsync(SelectedAccount.Username, robuxAmount);
                }
                catch (Exception sheetEx)
                {
                    AddLogWarning($"⚠️ Erro ao atualizar planilha (ignorado): {sheetEx.Message}");
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
                
                // Invalidar cache do jogo ROBUX e recarregar painéis
                _sheetsIntegration?.InvalidateCache(660585678);
                
                // Resetar para forçar recarregamento do painel ALTERAR ESTOQUE
                _lastLoadedUsernameSupabase = "";
                
                // Atualizar painel ALTERAR ESTOQUE (agora com dados atualizados no Supabase)
                await LoadProductsFromSupabaseAsync(SelectedAccount.Username);
                
                // Atualizar painel de INVENTÁRIO se estiver visualizando o jogo ROBUX
                if (robuxGameId > 0)
                {
                    _inventoryPanel?.RefreshIfCurrentGame(robuxGameId);
                }
                
                // Se estiver no jogo ROBUX (legacy), atualizar painel de jogos também
                if (_selectedGameGid == 660585678)
                {
                    await ShowGameItemsAsync(660585678, "ROBUX");
                }
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro ao atualizar saldo: {ex.Message}");
            }
        }

        /// <summary>
        /// Atualiza o saldo de Robux de uma conta na planilha
        /// </summary>
        private async Task UpdateRobuxInSheetAsync(string username, long robuxAmount)
        {
            if (string.IsNullOrEmpty(APPS_SCRIPT_URL))
                throw new Exception("Apps Script URL não configurado");

            string url = $"{APPS_SCRIPT_URL}?action=updateRobux" +
                $"&sheetName=Robux" +
                $"&username={Uri.EscapeDataString(username)}" +
                $"&robux={robuxAmount}";

            var response = await _sheetHttpClient.GetStringAsync(url);
            
            if (!response.Contains("\"success\":true"))
            {
                throw new Exception($"Resposta do servidor: {response}");
            }
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

            // Buscar produtos do Supabase (sistema novo)
            _ = LoadProductsFromSupabaseAsync(SelectedAccount.Username);
        }

        /// <summary>
        /// Carrega os produtos de uma conta a partir da planilha Google Sheets
        /// </summary>
        // Proteção contra carregamentos múltiplos
        private bool _isLoadingProducts = false;
        private DateTime _lastProductsLoad = DateTime.MinValue;
        private string _lastLoadedUsername = "";

        private async Task LoadProductsFromSheetAsync(string username)
        {
            try
            {
                // Proteção: não carregar se já está carregando
                if (_isLoadingProducts)
                {
                    return;
                }

                // Proteção: debounce de 2 segundos para o mesmo usuário
                if (username == _lastLoadedUsername && 
                    (DateTime.Now - _lastProductsLoad).TotalSeconds < 2)
                {
                    return;
                }

                _isLoadingProducts = true;
                _lastLoadedUsername = username;
                _lastProductsLoad = DateTime.Now;

                // Inicializar integração se necessário
                if (_sheetsIntegration == null)
                {
                    _sheetsIntegration = new Classes.GoogleSheetsIntegration(SHEETS_ID);
                    AddLog($"📊 Conectando à planilha...");
                }

                int reqAntes = RequestCount;
                
                // Buscar produtos de TODOS os jogos
                var allGameProducts = await _sheetsIntegration.GetAllProductsForUserAsync(username);

                int reqDepois = RequestCount;
                int reqUsadas = reqDepois - reqAntes;

                // Atualizar painel visual
                UpdateEstoquePanel(username, allGameProducts);

                // Buscar 2FA Secret do jogo ROBUX (GID 660585678)
                var robuxGame = allGameProducts.FirstOrDefault(g => g.Gid == 660585678);
                if (robuxGame != null && robuxGame.Products.Count > 0)
                {
                    var productWith2FA = robuxGame.Products.FirstOrDefault(p => !string.IsNullOrEmpty(p.TwoFASecret));
                    if (productWith2FA != null)
                    {
                        TwoFASecretTextBox.Text = productWith2FA.TwoFASecret;
                        AddLog($"🔐 2FA Secret carregado para {username}");
                    }
                }

                if (allGameProducts.Count > 0)
                {
                    int totalProducts = allGameProducts.Sum(g => g.Products.Count);
                    AddLog($"📦 {username}: {totalProducts} produtos em {allGameProducts.Count} jogos (+{reqUsadas} req, total: {reqDepois})");
                }
                else
                {
                    AddLog($"📦 {username}: Nenhum produto (+{reqUsadas} req, total: {reqDepois})");
                }
            }
            catch (Exception ex)
            {
                AddLogError($"[Sheets] Erro: {ex.Message}");
            }
            finally
            {
                _isLoadingProducts = false;
            }
        }

        /// <summary>
        /// Carrega produtos de uma conta do Supabase no painel ALTERAR ESTOQUE
        /// </summary>
        // Proteção contra carregamentos múltiplos do Supabase
        private CancellationTokenSource _estoqueCts;
        private string _lastLoadedUsernameSupabase = "";

        private async Task LoadProductsFromSupabaseAsync(string username)
        {
            // Cancelar carregamento anterior se existir
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

                // Atualizar label do usuário
                EstoqueUserLabel.Text = username;
                EstoqueItemsPanel.Controls.Clear();

                // Buscar inventário, jogos e itens em PARALELO (3 chamadas simultâneas)
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
                        ForeColor = System.Drawing.Color.Gray,
                        Font = new System.Drawing.Font("Segoe UI", 8F),
                        Size = new System.Drawing.Size(145, 20),
                        TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                    };
                    EstoqueItemsPanel.Controls.Add(noItemsLabel);
                    return;
                }

                var gameDict = games.ToDictionary(g => g.Id, g => g.Name);

                // Mapear itens para jogos (dados já carregados, sem chamadas extras)
                var itemToGame = new Dictionary<int, int>();
                var itemNames = new Dictionary<int, string>();

                foreach (var item in allItems)
                {
                    itemToGame[item.Id] = item.GameId;
                    itemNames[item.Id] = item.Name;
                }

                // Agrupar por jogo
                var groupedByGame = inventory
                    .Where(inv => itemToGame.ContainsKey(inv.ItemId))
                    .GroupBy(inv => itemToGame[inv.ItemId])
                    .OrderBy(g => gameDict.ContainsKey(g.Key) ? gameDict[g.Key] : "")
                    .ToList();

                // Mostrar quantidade de jogos
                EstoqueGameLabel.Text = groupedByGame.Count == 1
                    ? (gameDict.ContainsKey(groupedByGame[0].Key) ? gameDict[groupedByGame[0].Key].ToUpper() : "JOGO")
                    : $"{groupedByGame.Count} JOGOS";

                // Largura do painel
                int headerWidth = EstoquePanel.Width - 25;
                if (headerWidth < 120) headerWidth = 135;

                // Limpar painel novamente antes de popular (garante que dados antigos não ficam)
                EstoqueItemsPanel.Controls.Clear();

                // Adicionar produtos por jogo
                foreach (var gameGroup in groupedByGame)
                {
                    string gameName = gameDict.ContainsKey(gameGroup.Key) ? gameDict[gameGroup.Key] : "Jogo Desconhecido";

                    // Header do jogo
                    var gameHeader = new Label
                    {
                        Text = gameName.ToUpper(),
                        Font = new System.Drawing.Font("Segoe UI", 7F, System.Drawing.FontStyle.Bold),
                        ForeColor = System.Drawing.Color.LimeGreen,
                        BackColor = System.Drawing.Color.FromArgb(45, 45, 45),
                        Size = new System.Drawing.Size(headerWidth, 18),
                        TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                        Margin = new Padding(0, 5, 0, 2)
                    };
                    EstoqueItemsPanel.Controls.Add(gameHeader);

                    // Produtos
                    foreach (var inv in gameGroup.OrderBy(i => itemNames.ContainsKey(i.ItemId) ? itemNames[i.ItemId] : ""))
                    {
                        string itemName = itemNames.ContainsKey(inv.ItemId) ? itemNames[inv.ItemId] : $"Item {inv.ItemId}";
                        var itemPanel = CreateSupabaseEstoqueItemPanel(inv, itemName);
                        EstoqueItemsPanel.Controls.Add(itemPanel);
                    }
                }

                AddLog($"📦 {username}: {inventory.Count} produtos em {groupedByGame.Count} jogos (Supabase)");
            }
            catch (Exception ex)
            {
                if (!cts.Token.IsCancellationRequested)
                    AddLogError($"Erro ao carregar estoque: {ex.Message}");
            }
        }

        /// <summary>
        /// Cria um painel para um item do Supabase com botões +/-
        /// </summary>
        private Panel CreateSupabaseEstoqueItemPanel(SupabaseInventoryEntry inventory, string itemName)
        {
            int panelWidth = EstoquePanel.Width - 25;
            if (panelWidth < 120) panelWidth = 135;

            var panel = new Panel
            {
                Size = new System.Drawing.Size(panelWidth, 38),
                BackColor = System.Drawing.Color.FromArgb(35, 35, 35),
                Margin = new Padding(0, 1, 0, 1)
            };

            var nameLabel = new Label
            {
                Text = itemName,
                Font = new System.Drawing.Font("Segoe UI", 7F),
                ForeColor = System.Drawing.Color.LightGray,
                Location = new System.Drawing.Point(2, 1),
                Size = new System.Drawing.Size(panelWidth - 5, 15),
                AutoEllipsis = true
            };

            var minusBtn = new Button
            {
                Text = "-",
                Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(80, 80, 80),
                FlatStyle = FlatStyle.Flat,
                Location = new System.Drawing.Point(7, 17),
                Size = new System.Drawing.Size(30, 15),
                Cursor = Cursors.Hand,
                Tag = inventory
            };
            minusBtn.FlatAppearance.BorderSize = 0;
            minusBtn.Click += SupabaseEstoqueMinus_Click;

            var qtyTextBox = new TextBox
            {
                Text = inventory.Quantity.ToString("N0", new System.Globalization.CultureInfo("pt-BR")),
                Name = $"sqty_{inventory.Id}",
                Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(50, 50, 50),
                BorderStyle = BorderStyle.None,
                Location = new System.Drawing.Point(42, 17),
                Size = new System.Drawing.Size(panelWidth - 90, 15),
                TextAlign = HorizontalAlignment.Center,
                Tag = inventory
            };
            qtyTextBox.KeyDown += SupabaseEstoqueQty_KeyDown;
            qtyTextBox.Leave += SupabaseEstoqueQty_Leave;

            var plusBtn = new Button
            {
                Text = "+",
                Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(0, 120, 0),
                FlatStyle = FlatStyle.Flat,
                Location = new System.Drawing.Point(panelWidth - 38, 17),
                Size = new System.Drawing.Size(30, 15),
                Cursor = Cursors.Hand,
                Tag = inventory
            };
            plusBtn.FlatAppearance.BorderSize = 0;
            plusBtn.Click += SupabaseEstoquePlus_Click;

            panel.Controls.Add(nameLabel);
            panel.Controls.Add(minusBtn);
            panel.Controls.Add(qtyTextBox);
            panel.Controls.Add(plusBtn);

            return panel;
        }

        private void SupabaseEstoqueMinus_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var inventory = btn?.Tag as SupabaseInventoryEntry;
            if (inventory == null || inventory.Quantity <= 0) return;

            inventory.Quantity--;
            var parent = btn.Parent as Panel;
            var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("sqty_"));
            if (qtyTextBox != null) 
                qtyTextBox.Text = inventory.Quantity.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));

            // Atualizar o InventoryPanel também
            AddLog($"🔄 Sincronizando ID={inventory.Id}, Qty={inventory.Quantity}");
            _inventoryPanel?.UpdateInventoryQuantity(inventory.Id, inventory.Quantity);

            ScheduleSupabaseEstoqueUpdate(inventory);
        }

        private void SupabaseEstoquePlus_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var inventory = btn?.Tag as SupabaseInventoryEntry;
            if (inventory == null) return;

            inventory.Quantity++;
            var parent = btn.Parent as Panel;
            var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("sqty_"));
            if (qtyTextBox != null) 
                qtyTextBox.Text = inventory.Quantity.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));

            // Atualizar o InventoryPanel também
            AddLog($"🔄 Sincronizando ID={inventory.Id}, Qty={inventory.Quantity}");
            _inventoryPanel?.UpdateInventoryQuantity(inventory.Id, inventory.Quantity);

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

            string cleanText = textBox.Text.Replace(".", "").Replace(",", "");
            int newQty;
            if (!int.TryParse(cleanText, out newQty)) newQty = 0;
            if (newQty < 0) newQty = 0;

            if (newQty == inventory.Quantity)
            {
                textBox.Text = newQty.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
                return;
            }

            inventory.Quantity = newQty;
            textBox.Text = newQty.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
            
            // Atualizar o InventoryPanel também
            _inventoryPanel?.UpdateInventoryQuantity(inventory.Id, inventory.Quantity);
            
            ScheduleSupabaseEstoqueUpdate(inventory);
        }

        private Dictionary<string, CancellationTokenSource> _supabaseEstoqueDebounce = new Dictionary<string, CancellationTokenSource>();

        private void ScheduleSupabaseEstoqueUpdate(SupabaseInventoryEntry inventory)
        {
            string key = $"estoque_{inventory.Id}";

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
                    await Task.Delay(1500, cts.Token);
                    
                    if (!cts.Token.IsCancellationRequested)
                    {
                        var result = await SupabaseManager.Instance.UpdateInventoryQuantityAsync(inventory.Id, inventory.Quantity);
                        
                        if (result)
                            AddLog($"✅ Estoque atualizado: {inventory.Quantity}");
                        else
                            AddLog($"⚠️ Erro ao salvar estoque");
                        
                        if (_supabaseEstoqueDebounce.ContainsKey(key))
                            _supabaseEstoqueDebounce.Remove(key);
                    }
                }
                catch (TaskCanceledException) { }
            });
        }

        private void ClearEstoquePanel()
        {
            EstoqueUserLabel.Text = "Selecione uma conta";
            EstoqueGameLabel.Text = "";
            EstoqueItemsPanel.Controls.Clear();
        }

        /// <summary>
        /// Atualiza o painel de estoque com os produtos do usuário
        /// </summary>
        private void UpdateEstoquePanel(string username, List<Classes.GameProducts> gameProducts)
        {
            if (EstoquePanel == null) return;

            // Atualizar título com nome da conta
            EstoqueUserLabel.Text = username;
            
            // Limpar itens anteriores
            EstoqueItemsPanel.Controls.Clear();

            if (gameProducts.Count == 0)
            {
                EstoqueGameLabel.Text = "SEM ESTOQUE";
                var noItemsLabel = new Label
                {
                    Text = "Nenhum produto",
                    ForeColor = System.Drawing.Color.Gray,
                    Font = new System.Drawing.Font("Segoe UI", 8F),
                    Size = new System.Drawing.Size(145, 20),
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };
                EstoqueItemsPanel.Controls.Add(noItemsLabel);
                return;
            }

            // ORDENAR jogos por nome (ordem alfabética)
            var sortedGames = gameProducts.OrderBy(g => g.GameName).ToList();

            // Adicionar produtos de cada jogo
            foreach (var game in sortedGames)
            {
                // Largura fixa menor para evitar scroll
                int headerWidth = EstoquePanel.Width - 25;
                if (headerWidth < 120) headerWidth = 135;

                // Buscar PlaceId do jogo no config
                string gamePlaceId = GetPlaceIdForGame(game.GameName);

                // Separador com nome do jogo (CLICÁVEL)
                var gameHeader = new Label
                {
                    Text = game.GameName.ToUpper(),
                    Font = new System.Drawing.Font("Segoe UI", 7F, System.Drawing.FontStyle.Bold),
                    ForeColor = System.Drawing.Color.Cyan,
                    BackColor = System.Drawing.Color.FromArgb(45, 45, 45),
                    Size = new System.Drawing.Size(headerWidth, 18),
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Margin = new Padding(0, 5, 0, 2),
                    Cursor = Cursors.Hand,
                    Tag = gamePlaceId // Guardar PlaceId no Tag
                };
                
                // Evento de click para colocar PlaceId no campo
                gameHeader.Click += (s, e) =>
                {
                    var label = s as Label;
                    var placeId = label?.Tag as string;
                    if (!string.IsNullOrEmpty(placeId))
                    {
                        PlaceID.Text = placeId;
                        AddLog($"🎮 ID do jogo '{label.Text}' definido: {placeId}");
                    }
                };
                
                EstoqueItemsPanel.Controls.Add(gameHeader);

                // ORDENAR produtos por nome (ordem alfabética)
                var sortedProducts = game.Products.OrderBy(p => p.Product).ToList();

                // Adicionar cada produto
                foreach (var product in sortedProducts)
                {
                    var itemPanel = CreateEstoqueItemPanel(product);
                    EstoqueItemsPanel.Controls.Add(itemPanel);
                }
            }

            // Atualizar label do jogo principal
            EstoqueGameLabel.Text = sortedGames.Count > 1 ? $"{sortedGames.Count} JOGOS" : sortedGames[0].GameName.ToUpper();
        }

        /// <summary>
        /// Busca o PlaceId de um jogo pelo nome no games_config.json
        /// </summary>
        private string GetPlaceIdForGame(string gameName)
        {
            try
            {
                var config = Classes.GamesConfig.Instance;
                if (config?.Games != null)
                {
                    var game = config.Games.FirstOrDefault(g => 
                        g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));
                    return game?.PlaceId ?? "";
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Cria um painel para um item de produto com botões + e - e campo editável
        /// </summary>
        private Panel CreateEstoqueItemPanel(Classes.SheetProduct product)
        {
            // Largura fixa menor para evitar scroll
            int panelWidth = EstoquePanel.Width - 25;
            if (panelWidth < 120) panelWidth = 135;

            var panel = new Panel
            {
                Size = new System.Drawing.Size(panelWidth, 38),
                BackColor = System.Drawing.Color.FromArgb(35, 35, 35),
                Margin = new Padding(0, 1, 0, 1)
            };

            // Nome do produto
            var nameLabel = new Label
            {
                Text = product.Product,
                Font = new System.Drawing.Font("Segoe UI", 7F),
                ForeColor = System.Drawing.Color.LightGray,
                Location = new System.Drawing.Point(2, 1),
                Size = new System.Drawing.Size(panelWidth - 5, 15),
                AutoEllipsis = true
            };

            // Calcular posições proporcionais com margens iguais
            int btnWidth = 28;
            int margin = 4; // Margem igual dos dois lados
            int gap = 4;    // Espaço entre botão e textbox
            int textBoxX = margin + btnWidth + gap;
            int textBoxWidth = panelWidth - (btnWidth * 2) - (margin * 2) - (gap * 2);
            int plusBtnX = panelWidth - margin - btnWidth;
            
            // Botão -
            var minusBtn = new Button
            {
                Text = "-",
                Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(255, 128, 128), // VERMELHO
                FlatStyle = FlatStyle.Flat,
                Location = new System.Drawing.Point(margin, 18),
                Size = new System.Drawing.Size(btnWidth, 16),
                Cursor = Cursors.Hand,
                Tag = product
            };
            minusBtn.FlatAppearance.BorderSize = 0;
            minusBtn.Click += EstoqueMinusButton_Click;

            // Quantidade TextBox
            var qtyTextBox = new TextBox
            {
                Text = FormatNumberWithThousands(product.QuantityInt),
                Name = $"qty_{product.RowIndex}_{product.Gid}",
                Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(50, 50, 50),
                BorderStyle = BorderStyle.None,
                Location = new System.Drawing.Point(textBoxX, 18),
                Size = new System.Drawing.Size(textBoxWidth, 15),
                TextAlign = HorizontalAlignment.Center,
                Tag = product
            };
            qtyTextBox.KeyPress += EstoqueQtyTextBox_KeyPress;
            qtyTextBox.Leave += EstoqueQtyTextBox_Leave;
            qtyTextBox.KeyDown += EstoqueQtyTextBox_KeyDown;
            qtyTextBox.Enter += EstoqueQtyTextBox_Enter;

            // Botão +
            var plusBtn = new Button
            {
                Text = "+",
                Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(0, 120, 0),
                FlatStyle = FlatStyle.Flat,
                Location = new System.Drawing.Point(plusBtnX, 18),
                Size = new System.Drawing.Size(btnWidth, 16),
                Cursor = Cursors.Hand,
                Tag = product
            };
            plusBtn.FlatAppearance.BorderSize = 0;
            plusBtn.Click += EstoquePlusButton_Click;

            panel.Controls.Add(nameLabel);
            panel.Controls.Add(minusBtn);
            panel.Controls.Add(qtyTextBox);
            panel.Controls.Add(plusBtn);

            return panel;
        }

        /// <summary>
        /// Só permite números no campo de quantidade
        /// </summary>
        private void EstoqueQtyTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Permitir apenas números, controles (backspace, delete) e separadores
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != ',')
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Salva quando pressiona Enter
        /// </summary>
        private void EstoqueQtyTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                var textBox = sender as TextBox;
                SaveQtyFromTextBox(textBox);
            }
        }

        /// <summary>
        /// Salva quando sai do campo
        /// </summary>
        private void EstoqueQtyTextBox_Leave(object sender, EventArgs e)
        {
            var textBox = sender as TextBox;
            SaveQtyFromTextBox(textBox);
        }

        /// <summary>
        /// Quando entrar no TextBox, mostrar número sem formatação para facilitar edição
        /// </summary>
        private void EstoqueQtyTextBox_Enter(object sender, EventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;
            var product = textBox.Tag as Classes.SheetProduct;
            if (product == null) return;
            
            // Mostrar número sem formatação quando editando
            textBox.Text = product.QuantityInt.ToString();
            textBox.SelectAll();
        }

        /// <summary>
        /// Salva a quantidade digitada no TextBox
        /// </summary>
        private async void SaveQtyFromTextBox(TextBox textBox)
        {
            if (textBox == null) return;
            var product = textBox.Tag as Classes.SheetProduct;
            if (product == null) return;

            // Remover separadores de milhares antes de parsear
            string cleanText = textBox.Text.Replace(".", "").Replace(",", "");
            
            int newQty;
            if (!int.TryParse(cleanText, out newQty)) newQty = 0;
            if (newQty < 0) newQty = 0;

            // Reformatar sempre que sair do campo
            textBox.Text = FormatNumberWithThousands(newQty);
            
            // Se não mudou, não atualiza a planilha
            if (newQty == product.QuantityInt) return;

            product.QuantityInt = newQty;
            product.Quantity = newQty.ToString();

            await UpdateSheetValueAsync(product);
        }

        /// <summary>
        /// Evento do botão - para diminuir quantidade
        /// </summary>
        private void EstoqueMinusButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var product = btn?.Tag as Classes.SheetProduct;
            if (product == null) return;

            if (product.QuantityInt > 0)
            {
                product.QuantityInt--;
                product.Quantity = product.QuantityInt.ToString();
                
                // Atualizar TextBox com formatação
                var parent = btn.Parent as Panel;
                var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("qty_"));
                if (qtyTextBox != null) qtyTextBox.Text = FormatNumberWithThousands(product.QuantityInt);

                // Atualizar planilha com debounce
                ScheduleSaveToSheet(product);
            }
        }

        /// <summary>
        /// Evento do botão + para aumentar quantidade
        /// </summary>
        private void EstoquePlusButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var product = btn?.Tag as Classes.SheetProduct;
            if (product == null) return;

            product.QuantityInt++;
            product.Quantity = product.QuantityInt.ToString();
            
            // Atualizar TextBox com formatação
            var parent = btn.Parent as Panel;
            var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("qty_"));
            if (qtyTextBox != null) qtyTextBox.Text = FormatNumberWithThousands(product.QuantityInt);

            // Atualizar planilha com debounce
            ScheduleSaveToSheet(product);
        }

        // HttpClient reutilizável para melhor performance
        private static readonly HttpClient _sheetHttpClient = new HttpClient(new HttpClientHandler { MaxConnectionsPerServer = 10, UseProxy = false }) 
        { 
            Timeout = TimeSpan.FromSeconds(8) 
        };

        // Sistema de Debounce para economizar requisições
        private Dictionary<string, System.Windows.Forms.Timer> _debounceTimers = new Dictionary<string, System.Windows.Forms.Timer>();
        private Dictionary<string, Classes.SheetProduct> _pendingSaves = new Dictionary<string, Classes.SheetProduct>();

        /// <summary>
        /// Agenda o salvamento após 500ms de inatividade (debounce simples)
        /// </summary>
        private void ScheduleSaveToSheet(Classes.SheetProduct product)
        {
            string key = $"{product.Gid}_{product.RowIndex}";

            // Atualizar produto pendente
            _pendingSaves[key] = product;

            // Se já existe um timer, parar e reiniciar
            if (_debounceTimers.ContainsKey(key))
            {
                _debounceTimers[key].Stop();
                _debounceTimers[key].Start();
            }
            else
            {
                // Criar novo timer
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 500; // 500ms após último clique
                timer.Tick += async (s, e) =>
                {
                    timer.Stop();
                    
                    if (_pendingSaves.ContainsKey(key))
                    {
                        var productToSave = _pendingSaves[key];
                        _pendingSaves.Remove(key);
                        await UpdateSheetValueAsync(productToSave);
                    }
                    
                    // Limpar timer
                    if (_debounceTimers.ContainsKey(key))
                    {
                        _debounceTimers[key].Dispose();
                        _debounceTimers.Remove(key);
                    }
                };
                
                _debounceTimers[key] = timer;
                timer.Start();
            }
        }

        /// <summary>
        /// Evento do botão Atualizar do painel de estoque
        /// </summary>
        private void EstoqueRefreshButton_Click(object sender, EventArgs e)
        {
            if (SelectedAccount != null)
            {
                _ = LoadProductsFromSupabaseAsync(SelectedAccount.Username);
                AddLog("🔄 Atualizando estoque...");
            }
        }

        /// <summary>
        /// Atualiza o valor no Supabase (UPSERT - insere ou atualiza)
        /// Mantido para compatibilidade com o painel de estoque legado
        /// </summary>
        private async Task UpdateSheetValueAsync(Classes.SheetProduct product)
        {
            try
            {
                var startTime = DateTime.Now;
                
                // Buscar username da conta selecionada
                string username = SelectedAccount?.Username ?? "unknown";
                
                // Usar o SupabaseManager para fazer UPSERT na tabela inventory
                // Nota: Para o sistema legado, precisamos criar um item temporário se não existir
                // Por enquanto, apenas logamos a operação
                AddLog($"✅ {product.Product}: {product.QuantityInt} (legacy)");
                
                // Incrementar contador de requisições
                IncrementRequestCount();
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro ao salvar: {ex.Message}");
            }
        }

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

        #region Game Selector Panel (Legacy - Google Sheets)

        private long _selectedGameGid = 0;
        private string _selectedGameName = "";
        private Dictionary<string, int> _productRotationIndex = new Dictionary<string, int>();

        /// <summary>
        /// Inicializa o painel seletor de jogos (LEGACY - usa Google Sheets)
        /// </summary>
        private void InitializeGameSelectorPanel()
        {
            // Limpar rotação ao voltar para lista de jogos
            _productRotationIndex.Clear();
            
            // Esconder busca e voltar primeiro
            GameSelectorSearchTextBox.Visible = false;
            GameSelectorBackButton.Visible = false;
            AddRobuxAccountButton.Visible = false;
            GameSelectorTitleLabel.Text = "JOGOS";
            
            // Limpar painéis expandidos
            _expandedPanels.Clear();;
            
            // Suspender layout para evitar flicker
            GameSelectorButtonsPanel.SuspendLayout();
            GameSelectorButtonsPanel.Controls.Clear();
            
            // Resetar scroll para o topo
            GameSelectorButtonsPanel.AutoScrollPosition = new System.Drawing.Point(0, 0);
            
            // Largura 100% do painel sem espaço na esquerda
            int buttonWidth = GameSelectorPanel.Width - 12;
            if (buttonWidth < 100) buttonWidth = 250;
            
            // Obter jogos da configuração
            var gameSheets = Classes.GoogleSheetsIntegration.GameSheets;
            
            // Log para debug
            AddLog($"🎮 Carregando {gameSheets.Count} jogos (largura: {buttonWidth})...");
            
            if (gameSheets.Count == 0)
            {
                AddLogWarning("⚠️ Nenhum jogo encontrado em games_config.json");
                // Tentar recarregar
                Classes.GamesConfig.Reload();
                gameSheets = Classes.GoogleSheetsIntegration.GameSheets;
                AddLog($"🔄 Após reload: {gameSheets.Count} jogos");
            }
            
            // Ordenar jogos alfabeticamente pelo nome
            foreach (var game in gameSheets.OrderBy(g => g.Key))
            {
                var btn = new Button
                {
                    Text = game.Key.ToUpper(),  // UPPERCASE
                    Tag = game.Value,
                    Size = new System.Drawing.Size(buttonWidth, 30),
                    FlatStyle = FlatStyle.Flat,
                    Font = new System.Drawing.Font("Segoe UI", 9F),
                    ForeColor = System.Drawing.Color.Black,
                    BackColor = System.Drawing.Color.White,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 2, 0, 2)
                };
                btn.FlatAppearance.BorderSize = 1;
                btn.Click += GameButton_Click;
                
                GameSelectorButtonsPanel.Controls.Add(btn);
            }

            _currentGameItems.Clear();
            
            GameSelectorButtonsPanel.ResumeLayout(true);
            GameSelectorButtonsPanel.PerformLayout();
            GameSelectorButtonsPanel.Refresh();
            
            AddLog($"✅ {GameSelectorButtonsPanel.Controls.Count} botões criados");
        }

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
            FriendsListPanel.BackColor = System.Drawing.Color.FromArgb(41, 41, 41); // #292929
            FriendsListPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; // Ancoragem
            
            // Adicionar ao panel4
            panel4.Controls.Add(FriendsListPanel);
            panel4.BackColor = System.Drawing.Color.FromArgb(41, 41, 41); // #292929
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
            
            // Carregar Modo Estoque
            ModoEstoqueAtivo = General.Get<bool>("ModoEstoque");
            
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
                var client = new RestSharp.RestClient("https://friends.roblox.com");
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
                var csrfClient = new RestSharp.RestClient("https://friends.roblox.com");
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

                        var acceptClient = new RestSharp.RestClient("https://friends.roblox.com");
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
                var friendsClient = new RestSharp.RestClient("https://friends.roblox.com");
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
                var presenceClient = new RestSharp.RestClient("https://presence.roblox.com");
                var presenceRequest = new RestSharp.RestRequest("/v1/presence/users", RestSharp.Method.Post);
                presenceRequest.AddHeader("Cookie", $".ROBLOSECURITY={account.SecurityToken}");
                presenceRequest.AddHeader("Content-Type", "application/json");
                presenceRequest.AddJsonBody(new { userIds = friendIds });

                var presenceResponse = await presenceClient.ExecuteAsync(presenceRequest);
                var presenceData = presenceResponse.IsSuccessful
                    ? Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(presenceResponse.Content)?["userPresences"]?.ToObject<List<Newtonsoft.Json.Linq.JObject>>()
                    : null;

                // 3. Buscar usernames
                var usersClient = new RestSharp.RestClient("https://users.roblox.com");
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
            panel.BackColor = System.Drawing.Color.FromArgb(50, 50, 50);
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
            displayNameLabel.ForeColor = System.Drawing.Color.White;
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
            usernameLabel.ForeColor = System.Drawing.Color.FromArgb(180, 180, 180); // Cinza claro
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
                followBtn.Enabled = friend.PlaceId > 0;
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
                var client = new RestSharp.RestClient("https://users.roblox.com");
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

        /// <summary>
        /// Evento ao clicar em um jogo
        /// </summary>
        private async void GameButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            _selectedGameGid = (long)btn.Tag;
            _selectedGameName = btn.Text.ToUpper();

            // Mostrar itens do jogo
            await ShowGameItemsAsync(_selectedGameGid, _selectedGameName);
        }

        /// <summary>
        /// Mostra os itens disponíveis de um jogo
        /// </summary>
        private async Task ShowGameItemsAsync(long gid, string gameName)
        {
            if (_sheetsIntegration == null)
                _sheetsIntegration = new Classes.GoogleSheetsIntegration(SHEETS_ID);

            GameSelectorTitleLabel.Text = gameName;
            GameSelectorButtonsPanel.SuspendLayout();
            GameSelectorButtonsPanel.Controls.Clear();
            GameSelectorBackButton.Visible = true;
            
            // Mostrar botão ADICIONAR CONTA apenas para ROBUX (GID 660585678)
            AddRobuxAccountButton.Visible = (gid == 660585678);
            
            // Forçar só scroll vertical
            GameSelectorButtonsPanel.AutoScroll = false;
            GameSelectorButtonsPanel.HorizontalScroll.Enabled = false;
            GameSelectorButtonsPanel.HorizontalScroll.Visible = false;
            GameSelectorButtonsPanel.AutoScroll = true;
            
            // Mostrar campo de busca e resetar
            GameSelectorSearchTextBox.Visible = true;
            GameSelectorSearchTextBox.Text = "🔍 Buscar...";

            // Buscar produtos com quantidades totais
            var productsWithQty = await _sheetsIntegration.GetProductsWithTotalQuantityAsync(gid);

            // Guardar no cache para filtrar depois
            _currentGameItems = productsWithQty;

            // Calcular largura considerando scrollbar vertical (17px)
            int panelWidth = GameSelectorButtonsPanel.ClientSize.Width - 20;
            if (panelWidth < 100) panelWidth = GameSelectorPanel.Width - 30;

            if (productsWithQty.Count == 0)
            {
                var noItemsLabel = new Label
                {
                    Text = "Nenhum item encontrado",
                    ForeColor = System.Drawing.Color.Gray,
                    Font = new System.Drawing.Font("Segoe UI", 8F),
                    Size = new System.Drawing.Size(panelWidth, 30),
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };
                GameSelectorButtonsPanel.Controls.Add(noItemsLabel);
                GameSelectorButtonsPanel.ResumeLayout();
                return;
            }

            // Exibir todos os itens ordenados alfabeticamente
            foreach (var item in productsWithQty.OrderBy(x => x.Key))
            {
                var itemPanel = CreateGameItemPanel(item.Key, item.Value, panelWidth);
                GameSelectorButtonsPanel.Controls.Add(itemPanel);
            }

            GameSelectorButtonsPanel.ResumeLayout();
            AddLog($"🎮 {gameName}: {productsWithQty.Count} itens");
        }

        /// <summary>
        /// Evento ao clicar em um painel de item
        /// </summary>
        private async void ItemPanel_Click(object sender, EventArgs e)
        {
            Control ctrl = sender as Control;
            string product = ctrl?.Tag?.ToString();
            
            if (string.IsNullOrEmpty(product)) return;

            await SelectAccountWithProduct(product);
        }

        /// <summary>
        /// Seleciona uma conta que tem o produto especificado
        /// </summary>
        private async Task SelectAccountWithProduct(string product)
        {
            if (string.IsNullOrEmpty(product)) return;

            // Buscar contas que têm este produto
            var accountsWithItem = await _sheetsIntegration.GetAccountsWithItemAsync(_selectedGameGid, product);

            if (accountsWithItem.Count == 0)
            {
                AddLogWarning($"⚠️ Nenhuma conta tem '{product}' em estoque");
                return;
            }

            // Obter índice atual de rotação para este produto
            string rotationKey = $"{_selectedGameGid}_{product}";
            if (!_productRotationIndex.ContainsKey(rotationKey))
                _productRotationIndex[rotationKey] = 0;

            // Pegar conta baseada no índice de rotação
            int currentIndex = _productRotationIndex[rotationKey] % accountsWithItem.Count;
            var selectedProduct = accountsWithItem[currentIndex];
            
            // Incrementar índice para próximo clique
            _productRotationIndex[rotationKey]++;
            
            // Encontrar a conta na lista
            var account = AccountsList?.FirstOrDefault(a => 
                a.Username.Equals(selectedProduct.Username, StringComparison.OrdinalIgnoreCase));

            if (account != null)
            {
                // Selecionar a conta no AccountsView
                AccountsView.SelectedObject = account;
                AccountsView.EnsureModelVisible(account);
                
                // Se for o jogo ROBUX (GID 660585678) e tiver 2FA Secret, preencher automaticamente
                if (_selectedGameGid == 660585678 && !string.IsNullOrEmpty(selectedProduct.TwoFASecret))
                {
                    TwoFASecretTextBox.Text = selectedProduct.TwoFASecret;
                    AddLog($"🔐 2FA Secret carregado para {account.Username}");
                }
                
                AddLog($"✅ Selecionado: {account.Username} ({product}: {selectedProduct.QuantityInt}) [{currentIndex + 1}/{accountsWithItem.Count}]");
            }
            else
            {
                // Tentar adicionar a conta automaticamente usando o cookie da planilha
                if (!string.IsNullOrEmpty(selectedProduct.Cookie))
                {
                    AddLog($"🔄 Adicionando conta '{selectedProduct.Username}' automaticamente...");
                    
                    Account newAccount = AddAccount(selectedProduct.Cookie);
                    
                    if (newAccount != null)
                    {
                        AddLogSuccess($"✅ Conta '{newAccount.Username}' adicionada!");
                        
                        // Selecionar a conta recém adicionada
                        AccountsView.SelectedObject = newAccount;
                        AccountsView.EnsureModelVisible(newAccount);
                        
                        // Se for o jogo ROBUX (GID 660585678) e tiver 2FA Secret, preencher automaticamente
                        if (_selectedGameGid == 660585678 && !string.IsNullOrEmpty(selectedProduct.TwoFASecret))
                        {
                            TwoFASecretTextBox.Text = selectedProduct.TwoFASecret;
                            AddLog($"🔐 2FA Secret carregado para {newAccount.Username}");
                        }
                        
                        AddLog($"✅ Selecionado: {newAccount.Username} ({product}: {selectedProduct.QuantityInt}) [{currentIndex + 1}/{accountsWithItem.Count}]");
                    }
                    else
                    {
                        AddLogWarning($"⚠️ Falha ao adicionar conta '{selectedProduct.Username}'");
                    }
                }
                else
                {
                    AddLogWarning($"⚠️ Conta '{selectedProduct.Username}' não encontrada e sem cookie na planilha");
                }
            }
        }

        /// <summary>
        /// Botão Voltar - retorna à lista de jogos
        /// </summary>
        private void GameSelectorBackButton_Click(object sender, EventArgs e)
        {
            InitializeGameSelectorPanel();
        }

        /// <summary>
        /// Botão ADICIONAR CONTA - adiciona a conta selecionada na planilha ROBUX
        /// </summary>
        private async void AddRobuxAccountButton_Click(object sender, EventArgs e)
        {
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Verificar se a conta já existe na planilha
            var existingAccounts = await _sheetsIntegration.GetAccountsWithItemAsync(660585678, "ROBUX");
            var accountExists = existingAccounts.Any(a => 
                a.Username.Equals(SelectedAccount.Username, StringComparison.OrdinalIgnoreCase));
            
            if (accountExists)
            {
                MessageBox.Show($"A conta '{SelectedAccount.Username}' já existe na planilha!", 
                    "Conta já existe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Pedir 2FA Secret
            string twoFASecret = "";
            using (var inputForm = new Form())
            {
                inputForm.Text = "2FA Secret";
                inputForm.Size = new System.Drawing.Size(350, 150);
                inputForm.StartPosition = FormStartPosition.CenterParent;
                inputForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                inputForm.MaximizeBox = false;
                inputForm.MinimizeBox = false;
                inputForm.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);

                var label = new Label
                {
                    Text = "Insira o 2FA Secret (opcional):",
                    Location = new System.Drawing.Point(10, 15),
                    Size = new System.Drawing.Size(320, 20),
                    ForeColor = System.Drawing.Color.White
                };

                var textBox = new TextBox
                {
                    Location = new System.Drawing.Point(10, 40),
                    Size = new System.Drawing.Size(310, 25),
                    BackColor = System.Drawing.Color.FromArgb(50, 50, 50),
                    ForeColor = System.Drawing.Color.White
                };

                var okButton = new Button
                {
                    Text = "ADICIONAR",
                    Location = new System.Drawing.Point(120, 75),
                    Size = new System.Drawing.Size(100, 28),
                    DialogResult = DialogResult.OK,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = System.Drawing.Color.LimeGreen
                };

                inputForm.Controls.AddRange(new Control[] { label, textBox, okButton });
                inputForm.AcceptButton = okButton;

                if (inputForm.ShowDialog() == DialogResult.OK)
                {
                    twoFASecret = textBox.Text.Trim();
                }
                else
                {
                    return; // Cancelou
                }
            }

            AddRobuxAccountButton.Enabled = false;
            AddRobuxAccountButton.Text = "ADICIONANDO...";

            try
            {
                // Buscar quantidade de Robux via API
                long robuxAmount = await GetAccountRobuxAsync(SelectedAccount);
                
                // Adicionar na planilha
                await AddAccountToRobuxSheetAsync(
                    SelectedAccount.SecurityToken,
                    SelectedAccount.Username,
                    robuxAmount,
                    twoFASecret
                );

                AddLogSuccess($"✅ Conta '{SelectedAccount.Username}' adicionada à planilha ROBUX ({robuxAmount} R$)");
                
                // Atualizar campo 2FA se preenchido
                if (!string.IsNullOrEmpty(twoFASecret))
                {
                    TwoFASecretTextBox.Text = twoFASecret;
                }

                // Recarregar itens do jogo
                await ShowGameItemsAsync(660585678, "ROBUX");
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro ao adicionar conta: {ex.Message}");
                MessageBox.Show($"Erro ao adicionar conta:\n{ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                AddRobuxAccountButton.Enabled = true;
                AddRobuxAccountButton.Text = "ADICIONAR CONTA";
            }
        }

        /// <summary>
        /// Busca a quantidade de Robux de uma conta via API do Roblox
        /// </summary>
        private async Task<long> GetAccountRobuxAsync(Account account)
        {
            try
            {
                if (string.IsNullOrEmpty(account.SecurityToken))
                    return 0;

                string cookie = account.SecurityToken;
                long userId = account.UserID;

                var handler = new System.Net.Http.HttpClientHandler
                {
                    UseCookies = true,
                    CookieContainer = new System.Net.CookieContainer(),
                    UseProxy = false
                };
                handler.CookieContainer.Add(new Uri("https://economy.roblox.com"), new System.Net.Cookie(".ROBLOSECURITY", cookie, "/", ".roblox.com"));
                
                using (var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) })
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                    
                    // Se não tem UserId, buscar rapidamente
                    if (userId == 0)
                    {
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

                    if (userId == 0) return 0;

                    // Buscar Robux direto
                    var robuxResponse = await client.GetAsync($"https://economy.roblox.com/v1/users/{userId}/currency");
                    var robuxJson = await robuxResponse.Content.ReadAsStringAsync();
                    
                    var robuxMatch = System.Text.RegularExpressions.Regex.Match(robuxJson, "\"robux\":(\\d+)");
                    if (robuxMatch.Success)
                    {
                        long.TryParse(robuxMatch.Groups[1].Value, out long robux);
                        return robux;
                    }
                    
                    return 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Adiciona uma conta na planilha do Google Sheets na aba Robux
        /// </summary>
        private async Task AddAccountToRobuxSheetAsync(string cookie, string username, long robuxAmount, string twoFASecret)
        {
            if (string.IsNullOrEmpty(APPS_SCRIPT_URL))
                throw new Exception("Apps Script URL não configurado");

            string url = $"{APPS_SCRIPT_URL}?action=addRobuxAccount" +
                $"&sheetName=Robux" +
                $"&cookie={Uri.EscapeDataString(cookie)}" +
                $"&username={Uri.EscapeDataString(username)}" +
                $"&robux={robuxAmount}" +
                $"&twofa={Uri.EscapeDataString(twoFASecret)}";

            AddLog($"📤 Enviando para planilha...");
            var response = await _sheetHttpClient.GetStringAsync(url);
            AddLog($"📥 Resposta: {response}");
            
            if (!response.Contains("\"success\":true"))
            {
                throw new Exception($"Resposta do servidor: {response}");
            }
            
            // Invalidar cache para forçar recarregar
            _sheetsIntegration?.InvalidateCache(660585678);
        }

        // Cache dos itens para filtrar
        private Dictionary<string, int> _currentGameItems = new Dictionary<string, int>();

        /// <summary>
        /// Placeholder do campo de busca - ao entrar
        /// </summary>
        private void GameSelectorSearchTextBox_Enter(object sender, EventArgs e)
        {
            if (GameSelectorSearchTextBox.Text == "🔍 Buscar...")
            {
                GameSelectorSearchTextBox.Text = "";
            }
        }

        /// <summary>
        /// Placeholder do campo de busca - ao sair
        /// </summary>
        private void GameSelectorSearchTextBox_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GameSelectorSearchTextBox.Text))
            {
                GameSelectorSearchTextBox.Text = "🔍 Buscar...";
            }
        }

        /// <summary>
        /// Filtra os itens conforme digita
        /// </summary>
        private void GameSelectorSearchTextBox_TextChanged(object sender, EventArgs e)
        {
            string searchText = GameSelectorSearchTextBox.Text;
            
            // Ignorar placeholder
            if (searchText == "🔍 Buscar..." || _currentGameItems.Count == 0)
                return;

            FilterGameItems(searchText);
        }

        /// <summary>
        /// Filtra e exibe os itens que correspondem à busca
        /// </summary>
        private void FilterGameItems(string searchText)
        {
            GameSelectorButtonsPanel.SuspendLayout();
            GameSelectorButtonsPanel.Controls.Clear();

            // Calcular largura considerando scrollbar vertical
            int panelWidth = GameSelectorButtonsPanel.ClientSize.Width - 20;
            if (panelWidth < 100) panelWidth = GameSelectorPanel.Width - 30;

            var filteredItems = string.IsNullOrWhiteSpace(searchText)
                ? _currentGameItems
                : _currentGameItems.Where(x => x.Key.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToDictionary(x => x.Key, x => x.Value);

            // Ordenar itens filtrados alfabeticamente
            foreach (var item in filteredItems.OrderBy(x => x.Key))
            {
                var itemPanel = CreateGameItemPanel(item.Key, item.Value, panelWidth);
                GameSelectorButtonsPanel.Controls.Add(itemPanel);
            }
            
            GameSelectorButtonsPanel.ResumeLayout();
        }

        /// <summary>
        /// Cria um painel para um item do jogo
        /// </summary>
        private Panel CreateGameItemPanel(string product, int totalQty, int panelWidth = 0)
        {
            // Se não passou largura, calcular
            if (panelWidth <= 0)
            {
                panelWidth = GameSelectorPanel.Width - 30;
                if (panelWidth < 100) panelWidth = 220;
            }

            var itemPanel = new Panel
            {
                Size = new System.Drawing.Size(panelWidth, 22),
                BackColor = System.Drawing.Color.FromArgb(50, 50, 50),
                Margin = new Padding(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Tag = product
            };

            // Seta para expandir (à direita)
            var arrowLabel = new Label
            {
                Text = "▶",
                Font = new System.Drawing.Font("Segoe UI", 7F),
                ForeColor = System.Drawing.Color.Gray,
                Location = new System.Drawing.Point(panelWidth - 18, 3),
                Size = new System.Drawing.Size(15, 16),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Tag = product,
                Cursor = Cursors.Hand
            };
            arrowLabel.Click += ArrowLabel_Click;

            // Quantidade (à direita, antes da seta)
            var qtyLabel = new Label
            {
                Text = FormatNumberWithThousands(totalQty),
                Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Bold),
                ForeColor = totalQty > 0 ? System.Drawing.Color.LimeGreen : System.Drawing.Color.Red,
                Location = new System.Drawing.Point(panelWidth - 85, 2),
                Size = new System.Drawing.Size(65, 18),
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                Tag = product
            };

            // Nome do produto (UPPERCASE)
            var nameLabel = new Label
            {
                Text = product.ToUpper(),
                Font = new System.Drawing.Font("Segoe UI", 8F),
                ForeColor = System.Drawing.Color.White,
                Location = new System.Drawing.Point(3, 2),
                Size = new System.Drawing.Size(panelWidth - 90, 18),
                AutoEllipsis = true,
                Tag = product
            };

            itemPanel.Controls.Add(nameLabel);
            itemPanel.Controls.Add(qtyLabel);
            itemPanel.Controls.Add(arrowLabel);

            itemPanel.Click += ItemPanel_Click;
            nameLabel.Click += ItemPanel_Click;
            qtyLabel.Click += ItemPanel_Click;

            return itemPanel;
        }

        // Dicionário para controlar painéis expandidos
        private Dictionary<string, Panel> _expandedPanels = new Dictionary<string, Panel>();

        /// <summary>
        /// Clique na seta para expandir/colapsar lista de contas
        /// </summary>
        private async void ArrowLabel_Click(object sender, EventArgs e)
        {
            var arrow = sender as Label;
            string product = arrow?.Tag?.ToString();
            if (string.IsNullOrEmpty(product)) return;

            var itemPanel = arrow.Parent as Panel;
            if (itemPanel == null) return;

            string expandKey = $"{_selectedGameGid}_{product}";

            // Se já está expandido, colapsar
            if (_expandedPanels.ContainsKey(expandKey))
            {
                var existingPanel = _expandedPanels[expandKey];
                int index = GameSelectorButtonsPanel.Controls.IndexOf(existingPanel);
                if (index >= 0)
                {
                    GameSelectorButtonsPanel.Controls.Remove(existingPanel);
                    existingPanel.Dispose();
                }
                _expandedPanels.Remove(expandKey);
                arrow.Text = "▶";
                arrow.ForeColor = System.Drawing.Color.Gray;
                return;
            }

            // Expandir - buscar TODAS as contas com este item (incluindo 0)
            var accountsWithItem = await _sheetsIntegration.GetAllAccountsWithItemAsync(_selectedGameGid, product);

            if (accountsWithItem.Count == 0)
            {
                AddLogWarning($"⚠️ Nenhuma conta encontrada para '{product}'");
                return;
            }

            // Criar painel expandido com lista de contas
            int panelWidth = itemPanel.Width;
            int accountHeight = 20;
            int addButtonHeight = ModoEstoqueAtivo ? 22 : 0; // Altura do botão Add (só se Modo Estoque ativo)
            int expandedHeight = accountsWithItem.Count * accountHeight + addButtonHeight + 6; // +6 para espaçamento

            var expandedPanel = new Panel
            {
                Size = new System.Drawing.Size(panelWidth, expandedHeight),
                BackColor = System.Drawing.Color.FromArgb(35, 35, 35),
                Margin = new Padding(0, 0, 0, 2),
                AutoScroll = false
            };

            int yPos = 2;
            foreach (var acc in accountsWithItem)
            {
                var accPanel = new Panel
                {
                    Size = new System.Drawing.Size(panelWidth - 6, accountHeight - 2),
                    BackColor = System.Drawing.Color.FromArgb(45, 45, 45),
                    Location = new System.Drawing.Point(3, yPos),
                    Cursor = Cursors.Hand,
                    Tag = acc.Username
                };

                var accNameLabel = new Label
                {
                    Text = acc.Username,
                    Font = new System.Drawing.Font("Segoe UI", 7F),
                    ForeColor = System.Drawing.Color.Cyan,
                    Location = new System.Drawing.Point(5, 2),
                    Size = new System.Drawing.Size(panelWidth - 80, 14),
                    AutoEllipsis = true,
                    Tag = acc.Username,
                    Cursor = Cursors.Hand
                };

                var accQtyLabel = new Label
                {
                    Text = FormatNumberWithThousands(acc.QuantityInt),
                    Font = new System.Drawing.Font("Segoe UI", 7F, System.Drawing.FontStyle.Bold),
                    ForeColor = acc.QuantityInt > 0 ? System.Drawing.Color.LimeGreen : System.Drawing.Color.Red,
                    Location = new System.Drawing.Point(panelWidth - 70, 2),
                    Size = new System.Drawing.Size(60, 14),
                    TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                    Tag = acc.Username,
                    Cursor = Cursors.Hand
                };

                // Ao clicar, seleciona a conta específica
                var currentAcc = acc; // Capturar para closure
                EventHandler selectAccount = (s, ev) =>
                {
                    SelectAccountByUsername(currentAcc, product);
                };

                accPanel.Click += selectAccount;
                accNameLabel.Click += selectAccount;
                accQtyLabel.Click += selectAccount;

                accPanel.Controls.Add(accNameLabel);
                accPanel.Controls.Add(accQtyLabel);
                expandedPanel.Controls.Add(accPanel);

                yPos += accountHeight;
            }

            // Adicionar botão "Add" no final da lista (APENAS se Modo Estoque ativo)
            if (ModoEstoqueAtivo)
            {
                var addButton = new Button
                {
                    Text = "+ Add",
                    Font = new System.Drawing.Font("Segoe UI", 7F, System.Drawing.FontStyle.Bold),
                    ForeColor = System.Drawing.Color.White,
                    BackColor = System.Drawing.Color.FromArgb(0, 100, 0),
                    FlatStyle = FlatStyle.Flat,
                    Location = new System.Drawing.Point(3, yPos + 2),
                    Size = new System.Drawing.Size(panelWidth - 6, addButtonHeight - 4),
                    Cursor = Cursors.Hand,
                    Tag = new { Product = product, Gid = _selectedGameGid }
                };
                addButton.FlatAppearance.BorderSize = 0;
                addButton.Click += AddEmptyAccountToItem_Click;
                expandedPanel.Controls.Add(addButton);
            }

            // Inserir painel expandido logo após o item
            int itemIndex = GameSelectorButtonsPanel.Controls.IndexOf(itemPanel);
            GameSelectorButtonsPanel.Controls.Add(expandedPanel);
            GameSelectorButtonsPanel.Controls.SetChildIndex(expandedPanel, itemIndex + 1);

            _expandedPanels[expandKey] = expandedPanel;
            arrow.Text = "▼";
            arrow.ForeColor = System.Drawing.Color.White;
        }

        /// <summary>
        /// Adiciona uma conta vazia à lista de um item (busca automaticamente uma conta disponível)
        /// Prioridade: 1) Conta vazia, 2) Conta com item zerado, 3) Conta sem esse item
        /// </summary>
        private async void AddEmptyAccountToItem_Click(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag == null) return;

            dynamic tagData = button.Tag;
            string product = tagData.Product;
            long gid = tagData.Gid;

            // Buscar nome do jogo pelo GID
            string gameName = Classes.GoogleSheetsIntegration.GameSheets
                .FirstOrDefault(x => x.Value == gid).Key ?? "JOGO";

            // Desabilitar botão durante processamento
            button.Enabled = false;
            button.Text = "...";

            try
            {
                // Buscar a melhor conta disponível com priorização
                var (username, existingProduct, priority) = await _sheetsIntegration.GetBestAvailableAccountAsync(gid, product);

                if (string.IsNullOrEmpty(username))
                {
                    AddLogWarning($"⚠️ Todas as contas de '{gameName}' já têm o item '{product}'");
                    return;
                }

                string priorityText = priority switch
                {
                    1 => "conta vazia",
                    2 => "reutilizando linha",
                    3 => "nova linha",
                    _ => ""
                };

                if (existingProduct != null && priority <= 2)
                {
                    // Reutilizar linha existente - apenas mudar o nome do produto
                    await UpdateSheetProductNameAsync(gid, existingProduct.RowIndex, product);
                    AddLog($"✅ '{username}' → '{product}' ({priorityText}, linha {existingProduct.RowIndex})");
                }
                else
                {
                    // Adicionar nova linha
                    await AddAccountToSheetAsync(gid, username, product);
                    AddLog($"✅ '{username}' adicionada ao '{product}' ({priorityText})");
                }

                // Invalidar cache e recarregar
                _sheetsIntegration?.InvalidateCache(gid);
                
                if (_selectedGameGid == gid)
                {
                    string gameNameForRefresh = Classes.GoogleSheetsIntegration.GameSheets
                        .FirstOrDefault(x => x.Value == gid).Key ?? "JOGO";
                    await ShowGameItemsAsync(gid, gameNameForRefresh);
                }
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro ao adicionar conta: {ex.Message}");
            }
            finally
            {
                // Reabilitar botão
                button.Text = "+ Add";
                button.Enabled = true;
            }
        }

        /// <summary>
        /// Atualiza o nome do produto em uma linha existente da planilha
        /// </summary>
        private async Task UpdateSheetProductNameAsync(long gid, int rowIndex, string newProduct)
        {
            string appsScriptUrl = APPS_SCRIPT_URL;
            if (string.IsNullOrEmpty(appsScriptUrl))
            {
                AddLogError("❌ URL do Apps Script não configurada");
                return;
            }

            try
            {
                // Chamar Apps Script com action=rename para mudar o nome do produto
                string url = $"{appsScriptUrl}?action=rename&gid={gid}&row={rowIndex}&product={Uri.EscapeDataString(newProduct)}";
                
                using (var client = new System.Net.Http.HttpClient() { Timeout = TimeSpan.FromSeconds(10) })
                {
                    var response = await client.GetStringAsync(url);
                    
                    if (!response.Contains("\"success\":true"))
                    {
                        AddLogWarning($"⚠️ Resposta: {response}");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro ao renomear produto: {ex.Message}");
            }
        }

        /// <summary>
        /// Adiciona uma conta à planilha via Apps Script
        /// </summary>
        private async Task AddAccountToSheetAsync(long gid, string username, string product)
        {
            string appsScriptUrl = APPS_SCRIPT_URL;
            if (string.IsNullOrEmpty(appsScriptUrl))
            {
                AddLogError("❌ URL do Apps Script não configurada");
                return;
            }

            try
            {
                // Chamar Apps Script com action=add
                string url = $"{appsScriptUrl}?action=add&gid={gid}&username={Uri.EscapeDataString(username)}&product={Uri.EscapeDataString(product)}&value=0";
                
                using (var client = new System.Net.Http.HttpClient() { Timeout = TimeSpan.FromSeconds(10) })
                {
                    var response = await client.GetStringAsync(url);
                    
                    if (response.Contains("\"success\":true"))
                    {
                        AddLog($"✅ Conta '{username}' adicionada ao item '{product}'");
                        
                        // Invalidar cache e recarregar
                        _sheetsIntegration?.InvalidateCache(gid);
                        
                        // Atualizar a lista do jogo atual
                        if (_selectedGameGid == gid)
                        {
                            string gameName = Classes.GoogleSheetsIntegration.GameSheets
                                .FirstOrDefault(x => x.Value == gid).Key ?? "JOGO";
                            await ShowGameItemsAsync(gid, gameName);
                        }
                    }
                    else
                    {
                        AddLogError($"❌ Erro ao adicionar conta: {response}");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro ao adicionar conta: {ex.Message}");
            }
        }

        /// <summary>
        /// Seleciona uma conta específica pelo SheetProduct
        /// </summary>
        private void SelectAccountByUsername(Classes.SheetProduct sheetProduct, string product)
        {
            if (sheetProduct == null || string.IsNullOrEmpty(sheetProduct.Username)) return;

            var account = AccountsList?.FirstOrDefault(a => 
                a.Username.Equals(sheetProduct.Username, StringComparison.OrdinalIgnoreCase));

            if (account != null)
            {
                AccountsView.SelectedObject = account;
                AccountsView.EnsureModelVisible(account);
                
                // Se for o jogo ROBUX (GID 660585678) e tiver 2FA Secret, preencher automaticamente
                if (_selectedGameGid == 660585678 && !string.IsNullOrEmpty(sheetProduct.TwoFASecret))
                {
                    TwoFASecretTextBox.Text = sheetProduct.TwoFASecret;
                    AddLog($"🔐 2FA Secret carregado para {account.Username}");
                }
                
                AddLog($"✅ Selecionado: {account.Username} ({product}: {sheetProduct.QuantityInt})");
            }
            else
            {
                // Tentar adicionar a conta automaticamente usando o cookie da planilha
                if (!string.IsNullOrEmpty(sheetProduct.Cookie))
                {
                    AddLog($"🔄 Adicionando conta '{sheetProduct.Username}' automaticamente...");
                    
                    Account newAccount = AddAccount(sheetProduct.Cookie);
                    
                    if (newAccount != null)
                    {
                        AddLogSuccess($"✅ Conta '{newAccount.Username}' adicionada!");
                        
                        // Selecionar a conta recém adicionada
                        AccountsView.SelectedObject = newAccount;
                        AccountsView.EnsureModelVisible(newAccount);
                        
                        // Se for o jogo ROBUX (GID 660585678) e tiver 2FA Secret, preencher automaticamente
                        if (_selectedGameGid == 660585678 && !string.IsNullOrEmpty(sheetProduct.TwoFASecret))
                        {
                            TwoFASecretTextBox.Text = sheetProduct.TwoFASecret;
                            AddLog($"🔐 2FA Secret carregado para {newAccount.Username}");
                        }
                        
                        AddLog($"✅ Selecionado: {newAccount.Username} ({product}: {sheetProduct.QuantityInt})");
                    }
                    else
                    {
                        AddLogWarning($"⚠️ Falha ao adicionar conta '{sheetProduct.Username}'");
                    }
                }
                else
                {
                    AddLogWarning($"⚠️ Conta '{sheetProduct.Username}' não encontrada e sem cookie na planilha");
                }
            }
        }

        #endregion

        #region Pusher Integration

        /// <summary>
        /// Inicializa a conexão WebSocket com o Pusher para receber atualizações em tempo real
        /// </summary>
        private void InitializePusher()
        {
            try
            {
                string pusherUrl = $"wss://ws-{PUSHER_CLUSTER}.pusher.com/app/{PUSHER_KEY}?protocol=7&client=csharp&version=1.0";
                
                _pusherSocket = new WebSocket(pusherUrl);
                
                _pusherSocket.OnOpen += (sender, e) =>
                {
                    _pusherConnected = true;
                    AddLog("🟢 Pusher conectado (tempo real ativo)");
                    
                    // Inscrever no canal de atualizações da planilha
                    var subscribeMsg = new
                    {
                        @event = "pusher:subscribe",
                        data = new { channel = "sheets-updates" }
                    };
                    _pusherSocket.Send(JsonConvert.SerializeObject(subscribeMsg));
                };

                _pusherSocket.OnMessage += (sender, e) =>
                {
                    try
                    {
                        HandlePusherMessage(e.Data);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Pusher] Erro ao processar mensagem: {ex.Message}");
                    }
                };

                _pusherSocket.OnClose += (sender, e) =>
                {
                    _pusherConnected = false;
                    AddLogWarning("🔴 Pusher desconectado");
                    
                    // Reconectar após 5 segundos
                    Task.Delay(5000).ContinueWith(_ =>
                    {
                        if (!_pusherConnected && _pusherSocket != null)
                        {
                            try { _pusherSocket.Connect(); }
                            catch { }
                        }
                    });
                };

                _pusherSocket.OnError += (sender, e) =>
                {
                    Debug.WriteLine($"[Pusher] Erro: {e.Message}");
                };

                _pusherSocket.ConnectAsync();
            }
            catch (Exception ex)
            {
                AddLogError($"[Pusher] Erro ao inicializar: {ex.Message}");
            }
        }

        /// <summary>
        /// Processa mensagens recebidas do Pusher
        /// </summary>
        private DateTime _lastPusherUpdate = DateTime.MinValue;
        
        private void HandlePusherMessage(string data)
        {
            try
            {
                var message = JObject.Parse(data);
                string eventName = message["event"]?.ToString();

                if (eventName == "sheet-updated")
                {
                    // Debounce: ignorar atualizações muito frequentes (500ms)
                    if ((DateTime.Now - _lastPusherUpdate).TotalMilliseconds < 500)
                    {
                        return;
                    }
                    _lastPusherUpdate = DateTime.Now;

                    var eventData = JObject.Parse(message["data"]?.ToString() ?? "{}");
                    
                    string username = eventData["username"]?.ToString();
                    string product = eventData["product"]?.ToString();
                    int newValue = eventData["value"]?.ToObject<int>() ?? 0;
                    long gid = eventData["gid"]?.ToObject<long>() ?? 0;
                    int row = eventData["row"]?.ToObject<int>() ?? 0;

                    // Atualizar na thread da UI
                    this.Invoke(new Action(() =>
                    {
                        // Atualizar apenas o cache local (sem recarregar UI)
                        _sheetsIntegration?.InvalidateCache();

                        // NÃO recarregar o painel de estoque - isso causa conflito com edições locais
                        // O usuário pode clicar em "Atualizar" se quiser sincronizar
                        
                        AddLog($"📡 Atualização externa: {product} = {newValue}");
                    }));
                }
                else if (eventName == "pusher:subscription_succeeded")
                {
                    AddLog("📡 Inscrito no canal de atualizações");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Pusher] Erro ao processar: {ex.Message}");
            }
        }

        /// <summary>
        /// Desconecta do Pusher ao fechar o aplicativo
        /// </summary>
        private void DisconnectPusher()
        {
            try
            {
                if (_pusherSocket != null && _pusherConnected)
                {
                    _pusherSocket.Close();
                    _pusherSocket = null;
                }
            }
            catch { }
        }

        #endregion


        private void SetDescription_Click(object sender, EventArgs e)
        {
            foreach (Account account in AccountsView.SelectedObjects)

            RefreshView();
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

            if (!PlaceTimer.Enabled && PlaceId > 0)
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
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White
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
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
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
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
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
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
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
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
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

        /// <summary>
        /// Converte texto com notação abreviada para número
        /// Ex: "1k" -> 1000, "2m" -> 2000000, "3b" -> 3000000000
        /// </summary>
        private long ParseAbbreviatedNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            
            text = text.Trim().ToLower().Replace(".", "").Replace(",", "");
            
            long multiplier = 1;
            if (text.EndsWith("k"))
            {
                multiplier = 1_000;
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("m"))
            {
                multiplier = 1_000_000;
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("b"))
            {
                multiplier = 1_000_000_000;
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("t"))
            {
                multiplier = 1_000_000_000_000;
                text = text.Substring(0, text.Length - 1);
            }
            
            if (decimal.TryParse(text, System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out decimal decimalValue))
            {
                return (long)(decimalValue * multiplier);
            }
            
            if (long.TryParse(text, out long longValue))
            {
                return longValue * multiplier;
            }
            
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
            if (SelectedAccount == null)
            {
                MessageBox.Show("Selecione uma conta primeiro!", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                $"Tem certeza que deseja REMOVER TODAS AS AMIZADES da conta '{SelectedAccount.Username}'?\n\nEssa ação não pode ser desfeita!",
                "Confirmar Remoção de Amizades",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (confirm != DialogResult.Yes) return;

            try
            {
                AddLog($"🗑️ Removendo amizades de {SelectedAccount.Username}...");
                
                int removedCount = await DeleteAllFriendsAsync(SelectedAccount);
                
                AddLogSuccess($"✅ {removedCount} amizades removidas de {SelectedAccount.Username}");
                MessageBox.Show($"{removedCount} amizades removidas com sucesso!", "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AddLogError($"❌ Erro ao remover amizades: {ex.Message}");
                MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            // Desconectar do Pusher
            DisconnectPusher();

            AltManagerWS?.Stop();

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

            PlaceTimer.Start();
        }

        private async void PlaceTimer_Tick(object sender, EventArgs e)
        {
            if (EconClient == null) return;

            PlaceTimer.Stop();

            RestRequest request = new RestRequest($"v2/assets/{PlaceID.Text}/details", Method.Get);
            request.AddHeader("Accept", "application/json");
            RestResponse response = await EconClient.ExecuteAsync(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK && response.Content.StartsWith("{") && response.Content.EndsWith("}"))
            {
                ProductInfo placeInfo = JsonConvert.DeserializeObject<ProductInfo>(response.Content);

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
                    // CefSharp sem conta - abre tela de login
                    CefBrowser.Instance.EnterBrowserMode(null, "https://www.roblox.com/login");
                }
                return;
            }

            // Com conta selecionada, abre normalmente
            if (PuppeteerSupported)
                foreach (Account account in AccountsView.SelectedObjects)
                    new AccountBrowser(account);
            else if (!PuppeteerSupported && SelectedAccount != null)
                CefBrowser.Instance.EnterBrowserMode(SelectedAccount);
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
                CefBrowser.Instance.EnterBrowserMode(null, "https://www.roblox.com/login");
            }
        }

        private void customURLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Uri.TryCreate(ShowDialog("URL", "Open Browser"), UriKind.Absolute, out Uri Link))
                if (PuppeteerSupported)
                    foreach (Account account in AccountsView.SelectedObjects)
                        new AccountBrowser(account, Link.ToString(), string.Empty);
                else if (!PuppeteerSupported && SelectedAccount != null)
                    CefBrowser.Instance.EnterBrowserMode(SelectedAccount, Link.ToString());
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

        private void LogsButton_Click(object sender, EventArgs e)
        {
            // Criar popup com logs
            Form logsPopup = new Form();
            logsPopup.Text = "📋 Logs - Blox Brasil";
            logsPopup.Size = new System.Drawing.Size(800, 600);
            logsPopup.StartPosition = FormStartPosition.CenterParent;
            logsPopup.BackColor = System.Drawing.Color.FromArgb(18, 18, 18);
            logsPopup.Icon = this.Icon;

            RichTextBox logsTextBox = new RichTextBox();
            logsTextBox.Dock = DockStyle.Fill;
            logsTextBox.BackColor = System.Drawing.Color.FromArgb(25, 25, 25);
            logsTextBox.ForeColor = System.Drawing.Color.FromArgb(52, 211, 153);
            logsTextBox.Font = new System.Drawing.Font("Consolas", 10F);
            logsTextBox.ReadOnly = true;
            logsTextBox.BorderStyle = BorderStyle.None;
            logsTextBox.Text = DebugLogTextBox.Text;

            // Botão para fechar
            Button closeButton = new Button();
            closeButton.Text = "FECHAR";
            closeButton.Dock = DockStyle.Bottom;
            closeButton.Height = 40;
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.BackColor = System.Drawing.Color.FromArgb(45, 156, 219);
            closeButton.ForeColor = System.Drawing.Color.White;
            closeButton.Font = new System.Drawing.Font("Segoe UI Semibold", 10F);
            closeButton.Click += (s, args) => logsPopup.Close();

            logsPopup.Controls.Add(logsTextBox);
            logsPopup.Controls.Add(closeButton);
            logsPopup.ShowDialog(this);
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
    }
}