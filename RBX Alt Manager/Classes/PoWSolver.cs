using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RBX_Alt_Manager.Classes
{
    /// <summary>
    /// Solver para Roblox Proof of Work
    /// O PoW do Roblox usa exponenciação modular com raiz quadrada
    /// </summary>
    public class PoWSolver
    {
        // Tentar usar a DLL nativa se disponível
        private static bool _nativeDllAvailable = false;

        // Kernel32 para SetDllDirectory
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetLastError();

        [DllImport("pow.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr solve_pow(string n, string a, int iterations, IntPtr progressCallback);

        [DllImport("pow.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void free_string(IntPtr str);

        [DllImport("pow.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr get_solver_version();

        private static string _nativeDllVersion = null;
        private static string _initLog = "";

        static PoWSolver()
        {
            var log = new StringBuilder();
            
            try
            {
                // Obter diretório do executável
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                log.AppendLine($"📁 Diretório do exe: {exeDir}");

                // Verificar se as DLLs existem
                string powPath = Path.Combine(exeDir, "pow.dll");
                string cryptoPath = Path.Combine(exeDir, "libcrypto-3-x64.dll");
                string sslPath = Path.Combine(exeDir, "libssl-3-x64.dll");

                log.AppendLine($"\n🔍 Verificando arquivos:");
                log.AppendLine($"  pow.dll: {(File.Exists(powPath) ? "✅ EXISTE" : "❌ NÃO EXISTE")}");
                log.AppendLine($"  libcrypto-3-x64.dll: {(File.Exists(cryptoPath) ? "✅ EXISTE" : "❌ NÃO EXISTE")}");
                log.AppendLine($"  libssl-3-x64.dll: {(File.Exists(sslPath) ? "✅ EXISTE" : "❌ NÃO EXISTE")}");

                // Definir o diretório de busca de DLLs
                bool setDirResult = SetDllDirectory(exeDir);
                log.AppendLine($"\n⚙️ SetDllDirectory: {(setDirResult ? "✅ OK" : "❌ FALHOU")}");

                // Tentar carregar libcrypto primeiro
                if (File.Exists(cryptoPath))
                {
                    IntPtr cryptoHandle = LoadLibrary(cryptoPath);
                    uint cryptoError = GetLastError();
                    log.AppendLine($"\n📦 LoadLibrary(libcrypto):");
                    log.AppendLine($"  Handle: {cryptoHandle}");
                    log.AppendLine($"  Erro: {cryptoError}");
                    log.AppendLine($"  Status: {(cryptoHandle != IntPtr.Zero ? "✅ CARREGADA" : "❌ FALHOU")}");
                }

                // Tentar carregar libssl
                if (File.Exists(sslPath))
                {
                    IntPtr sslHandle = LoadLibrary(sslPath);
                    uint sslError = GetLastError();
                    log.AppendLine($"\n📦 LoadLibrary(libssl):");
                    log.AppendLine($"  Handle: {sslHandle}");
                    log.AppendLine($"  Erro: {sslError}");
                    log.AppendLine($"  Status: {(sslHandle != IntPtr.Zero ? "✅ CARREGADA" : "❌ FALHOU")}");
                }

                // Tentar carregar pow.dll
                if (File.Exists(powPath))
                {
                    IntPtr powHandle = LoadLibrary(powPath);
                    uint powError = GetLastError();
                    log.AppendLine($"\n📦 LoadLibrary(pow.dll):");
                    log.AppendLine($"  Handle: {powHandle}");
                    log.AppendLine($"  Erro: {powError}");
                    log.AppendLine($"  Status: {(powHandle != IntPtr.Zero ? "✅ CARREGADA" : "❌ FALHOU")}");
                }

                // Agora tentar obter a versão
                log.AppendLine($"\n🔧 Tentando get_solver_version()...");
                var versionPtr = get_solver_version();
                log.AppendLine($"  Ponteiro: {versionPtr}");
                
                if (versionPtr != IntPtr.Zero)
                {
                    _nativeDllVersion = Marshal.PtrToStringAnsi(versionPtr);
                    log.AppendLine($"  Versão: {_nativeDllVersion}");
                    log.AppendLine($"\n✅ DLL NATIVA CARREGADA COM SUCESSO!");
                    _nativeDllAvailable = true;
                }
                else
                {
                    log.AppendLine($"  ❌ Ponteiro nulo!");
                }
            }
            catch (Exception ex)
            {
                log.AppendLine($"\n❌ EXCEÇÃO: {ex.GetType().Name}");
                log.AppendLine($"  Mensagem: {ex.Message}");
                log.AppendLine($"  StackTrace: {ex.StackTrace}");
                _nativeDllAvailable = false;
            }

            _initLog = log.ToString();
            Debug.WriteLine(_initLog);
        }

        /// <summary>
        /// Retorna o log de inicialização para debug
        /// </summary>
        public static string GetInitLog() => _initLog;

        /// <summary>
        /// Verifica se a DLL nativa está disponível
        /// </summary>
        public static bool IsNativeDllAvailable => _nativeDllAvailable;

        /// <summary>
        /// Retorna a versão da DLL nativa ou "N/A"
        /// </summary>
        public static string NativeDllVersion => _nativeDllVersion ?? "N/A (usando C# puro - LENTO!)";

        /// <summary>
        /// Resolve o PoW usando a DLL nativa (se disponível) ou implementação em C#
        /// </summary>
        /// <param name="n">Módulo (número grande em decimal)</param>
        /// <param name="a">Base (número grande em decimal)</param>
        /// <param name="iterations">Número de iterações</param>
        /// <returns>Resultado como string decimal</returns>
        public static string Solve(string n, string a, int iterations)
        {
            if (_nativeDllAvailable)
            {
                return SolveNative(n, a, iterations);
            }
            else
            {
                return SolveManaged(n, a, iterations);
            }
        }

        /// <summary>
        /// Resolve usando a DLL nativa
        /// </summary>
        private static string SolveNative(string n, string a, int iterations)
        {
            try
            {
                Debug.WriteLine($"[POW] Resolvendo com DLL nativa: iterations={iterations}");
                
                IntPtr resultPtr = solve_pow(n, a, iterations, IntPtr.Zero);
                
                if (resultPtr == IntPtr.Zero)
                {
                    Debug.WriteLine("[POW] DLL retornou null, usando fallback");
                    return SolveManaged(n, a, iterations);
                }

                string result = Marshal.PtrToStringAnsi(resultPtr);
                free_string(resultPtr);
                
                Debug.WriteLine($"[POW] Resultado: {result?.Substring(0, Math.Min(50, result?.Length ?? 0))}...");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[POW] Erro na DLL nativa: {ex.Message}");
                return SolveManaged(n, a, iterations);
            }
        }

        /// <summary>
        /// Implementação em C# puro usando BigInteger
        /// O algoritmo é: aplicar quadrado modular repetidamente
        /// result = a^(2^iterations) mod n
        /// </summary>
        private static string SolveManaged(string n, string a, int iterations)
        {
            try
            {
                Debug.WriteLine($"[POW] Resolvendo com C# puro: iterations={iterations}");
                
                BigInteger modulus = BigInteger.Parse(n);
                BigInteger value = BigInteger.Parse(a);
                
                // Aplicar quadrado modular 'iterations' vezes
                // Isso calcula: a^(2^iterations) mod n
                for (int i = 0; i < iterations; i++)
                {
                    value = BigInteger.ModPow(value, 2, modulus);
                    
                    // Log de progresso a cada 10% ou a cada 100000 iterações
                    if (iterations > 100000 && i % (iterations / 10) == 0)
                    {
                        Debug.WriteLine($"[POW] Progresso: {(i * 100 / iterations)}%");
                    }
                }
                
                string result = value.ToString();
                Debug.WriteLine($"[POW] Resultado C#: {result.Substring(0, Math.Min(50, result.Length))}...");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[POW] Erro no solver C#: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Versão assíncrona do solver
        /// </summary>
        public static async Task<string> SolveAsync(string n, string a, int iterations)
        {
            return await Task.Run(() => Solve(n, a, iterations));
        }

        /// <summary>
        /// <summary>
        /// Extrai os parâmetros do PoW do metadata do challenge
        /// O formato esperado é: {"sessionId":"...","artifacts":"n=...&a=...&iterations=..."}
        /// </summary>
        public static (string n, string a, int iterations) ParseChallengeMetadata(string metadataJson)
        {
            try
            {
                var metadata = Newtonsoft.Json.Linq.JObject.Parse(metadataJson);
                string artifacts = metadata["artifacts"]?.ToString();
                
                if (string.IsNullOrEmpty(artifacts))
                {
                    Debug.WriteLine("[POW] Artifacts não encontrado no metadata");
                    return (null, null, 0);
                }

                // Parse dos parâmetros (formato: n=xxx&a=xxx&iterations=xxx)
                string n = null, a = null;
                int iterations = 0;

                foreach (var param in artifacts.Split('&'))
                {
                    var parts = param.Split('=');
                    if (parts.Length == 2)
                    {
                        switch (parts[0].ToLower())
                        {
                            case "n":
                                n = parts[1];
                                break;
                            case "a":
                                a = parts[1];
                                break;
                            case "iterations":
                                int.TryParse(parts[1], out iterations);
                                break;
                        }
                    }
                }

                Debug.WriteLine($"[POW] Parsed: n.Length={n?.Length}, a.Length={a?.Length}, iterations={iterations}");
                return (n, a, iterations);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[POW] Erro ao parsear metadata: {ex.Message}");
                return (null, null, 0);
            }
        }

        /// <summary>
        /// Resolve o PoW completo a partir do metadata do challenge
        /// </summary>
        public static async Task<string> SolveFromMetadataAsync(string metadataJson)
        {
            var (n, a, iterations) = ParseChallengeMetadata(metadataJson);
            
            if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(a) || iterations == 0)
            {
                Debug.WriteLine("[POW] Parâmetros inválidos");
                return null;
            }

            return await SolveAsync(n, a, iterations);
        }
    }
}
