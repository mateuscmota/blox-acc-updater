using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace RBX_Alt_Manager.Classes
{
    public static class BloxBrasilUpdater
    {
        // Arquivo que guarda a versão instalada
        private static readonly string VersionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
        
        // URL do repositório GitHub
        private const string VersionFileUrl = "https://raw.githubusercontent.com/mateuscmota/blox-acc-updater/main/version.json";
        
        // Versão atual (lida do arquivo local)
        public static string CurrentVersion
        {
            get
            {
                try
                {
                    if (File.Exists(VersionFilePath))
                        return File.ReadAllText(VersionFilePath).Trim();
                }
                catch { }
                return "0.0.0";
            }
        }
        
        // Salvar versão após atualização
        public static void SaveVersion(string version)
        {
            try
            {
                File.WriteAllText(VersionFilePath, version);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao salvar versão: {ex.Message}");
            }
        }
        
        public static async Task<(bool hasUpdate, string newVersion, string downloadUrl, string changelog)> CheckForUpdateAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "BloxBrasil-Updater");
                    client.DefaultRequestHeaders.Add("Cache-Control", "no-cache, no-store, must-revalidate");
                    client.DefaultRequestHeaders.Add("Pragma", "no-cache");
                    client.Timeout = TimeSpan.FromSeconds(15);
                    
                    // Adiciona timestamp para evitar cache
                    string urlWithNoCache = VersionFileUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    
                    Debug.WriteLine($"[UPDATE] Verificando: {urlWithNoCache}");
                    Debug.WriteLine($"[UPDATE] Versão local: {CurrentVersion}");
                    
                    string json = await client.GetStringAsync(urlWithNoCache);
                    
                    Debug.WriteLine($"[UPDATE] JSON recebido: {json}");
                    
                    JObject versionInfo = JObject.Parse(json);
                    
                    string latestVersion = versionInfo["version"]?.ToString() ?? "0.0.0";
                    string downloadUrl = versionInfo["download_url"]?.ToString() ?? "";
                    string changelog = versionInfo["changelog"]?.ToString() ?? "Melhorias e correções";
                    
                    Debug.WriteLine($"[UPDATE] Versão atual: {CurrentVersion}, Versão remota: {latestVersion}");
                    
                    bool hasUpdate = CompareVersions(latestVersion, CurrentVersion) > 0;
                    
                    Debug.WriteLine($"[UPDATE] Tem atualização: {hasUpdate}");
                    
                    return (hasUpdate, latestVersion, downloadUrl, changelog);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPDATE ERROR] Erro ao verificar atualização: {ex.Message}");
                Debug.WriteLine($"[UPDATE ERROR] StackTrace: {ex.StackTrace}");
                return (false, CurrentVersion, "", "");
            }
        }
        
        private static int CompareVersions(string v1, string v2)
        {
            try
            {
                Version version1 = new Version(v1);
                Version version2 = new Version(v2);
                return version1.CompareTo(version2);
            }
            catch
            {
                return string.Compare(v1, v2, StringComparison.Ordinal);
            }
        }
        
        public static async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, string newVersion, IProgress<int> progress)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "BloxBrasilUpdate.zip");
            string extractPath = Path.Combine(Path.GetTempPath(), "BloxBrasilUpdate");
            string currentExePath = Application.ExecutablePath;
            string currentDir = Path.GetDirectoryName(currentExePath);
            string updaterBatPath = Path.Combine(Path.GetTempPath(), "bloxbrasil_updater.bat");
            
            try
            {
                // Limpar arquivos antigos
                if (File.Exists(tempFile)) File.Delete(tempFile);
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                
                // Baixar o arquivo
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "BloxBrasil-Updater");
                    client.Timeout = TimeSpan.FromMinutes(10);
                    
                    using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        
                        long? totalBytes = response.Content.Headers.ContentLength;
                        
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            long totalRead = 0;
                            int bytesRead;
                            
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;
                                
                                if (totalBytes.HasValue)
                                {
                                    int progressPercent = (int)((totalRead * 100) / totalBytes.Value);
                                    progress?.Report(progressPercent);
                                }
                            }
                        }
                    }
                }
                
                // Extrair o ZIP
                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(tempFile, extractPath);
                
                // Salvar a nova versão ANTES de reiniciar
                SaveVersion(newVersion);
                
                // Criar script de atualização que será executado após fechar o programa
                string batContent = $@"@echo off
chcp 65001 >nul
echo Aguardando o programa fechar...
timeout /t 2 /nobreak >nul

:waitloop
tasklist /FI ""IMAGENAME eq {Path.GetFileName(currentExePath)}"" 2>NUL | find /I ""{Path.GetFileName(currentExePath)}"" >NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >nul
    goto waitloop
)

echo Finalizando processos relacionados...
taskkill /F /IM CefSharp.BrowserSubprocess.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo Preservando dados do usuário...
if exist ""{currentDir}\AccountData.json"" copy /y ""{currentDir}\AccountData.json"" ""{extractPath}\AccountData.json.bak"" >nul 2>&1
if exist ""{currentDir}\RAMSettings.ini"" copy /y ""{currentDir}\RAMSettings.ini"" ""{extractPath}\RAMSettings.ini.bak"" >nul 2>&1
if exist ""{currentDir}\RAMTheme.ini"" copy /y ""{currentDir}\RAMTheme.ini"" ""{extractPath}\RAMTheme.ini.bak"" >nul 2>&1
if exist ""{currentDir}\PrivateServers.json"" copy /y ""{currentDir}\PrivateServers.json"" ""{extractPath}\PrivateServers.json.bak"" >nul 2>&1
if exist ""{currentDir}\themes"" xcopy /s /e /y /i ""{currentDir}\themes"" ""{extractPath}\themes.bak\"" >nul 2>&1
if exist ""{currentDir}\wallpapers"" xcopy /s /e /y /i ""{currentDir}\wallpapers"" ""{extractPath}\wallpapers.bak\"" >nul 2>&1

echo Removendo dados de desenvolvimento do pacote...
if exist ""{extractPath}\AccountData.json"" del ""{extractPath}\AccountData.json"" >nul 2>&1
if exist ""{extractPath}\RAMSettings.ini"" del ""{extractPath}\RAMSettings.ini"" >nul 2>&1
if exist ""{extractPath}\RAMTheme.ini"" del ""{extractPath}\RAMTheme.ini"" >nul 2>&1
if exist ""{extractPath}\PrivateServers.json"" del ""{extractPath}\PrivateServers.json"" >nul 2>&1

echo Instalando atualização...
xcopy /s /e /y /i ""{extractPath}\*"" ""{currentDir}\"" >nul 2>&1

echo Restaurando dados do usuário...
if exist ""{extractPath}\AccountData.json.bak"" (
    copy /y ""{extractPath}\AccountData.json.bak"" ""{currentDir}\AccountData.json"" >nul 2>&1
)
if exist ""{extractPath}\RAMSettings.ini.bak"" (
    copy /y ""{extractPath}\RAMSettings.ini.bak"" ""{currentDir}\RAMSettings.ini"" >nul 2>&1
)
if exist ""{extractPath}\RAMTheme.ini.bak"" (
    copy /y ""{extractPath}\RAMTheme.ini.bak"" ""{currentDir}\RAMTheme.ini"" >nul 2>&1
)
if exist ""{extractPath}\themes.bak"" (
    xcopy /s /e /y /i ""{extractPath}\themes.bak\*"" ""{currentDir}\themes\"" >nul 2>&1
)
if exist ""{extractPath}\PrivateServers.json.bak"" (
    copy /y ""{extractPath}\PrivateServers.json.bak"" ""{currentDir}\PrivateServers.json"" >nul 2>&1
)
if exist ""{extractPath}\wallpapers.bak"" (
    xcopy /s /e /y /i ""{extractPath}\wallpapers.bak\*"" ""{currentDir}\wallpapers\"" >nul 2>&1
)

echo Limpando arquivos temporários...
rmdir /s /q ""{extractPath}"" >nul 2>&1
del ""{tempFile}"" >nul 2>&1

echo Iniciando o programa...
start """" ""{currentExePath}""

del ""%~f0""
";
                
                File.WriteAllText(updaterBatPath, batContent, System.Text.Encoding.UTF8);
                
                // Executar o script e fechar o programa
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = updaterBatPath,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                
                Process.Start(psi);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao instalar atualização: {ex.Message}");
                MessageBox.Show($"Erro ao baixar atualização: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}
