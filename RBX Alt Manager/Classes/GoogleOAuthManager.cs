using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RBX_Alt_Manager.Classes
{
    /// <summary>
    /// Gerencia o fluxo de login via Google OAuth 2.0
    /// </summary>
    public class GoogleOAuthManager
    {
        private static readonly string CLIENT_ID = AppSecrets.GoogleClientId;
        private static readonly string CLIENT_SECRET = AppSecrets.GoogleClientSecret;
        private const string REDIRECT_URI = "http://localhost:5834/oauth/callback";
        private const string AUTH_ENDPOINT = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TOKEN_ENDPOINT = "https://oauth2.googleapis.com/token";
        private const string USERINFO_ENDPOINT = "https://www.googleapis.com/oauth2/v2/userinfo";
        private const string SCOPES = "email profile";

        /// <summary>
        /// Inicia o fluxo OAuth: abre navegador e aguarda callback.
        /// Retorna (email, displayName) ou lança exceção em caso de erro.
        /// </summary>
        public async Task<(string email, string displayName)> LoginAsync()
        {
            string state = Guid.NewGuid().ToString("N");

            string authUrl = $"{AUTH_ENDPOINT}" +
                $"?client_id={Uri.EscapeDataString(CLIENT_ID)}" +
                $"&redirect_uri={Uri.EscapeDataString(REDIRECT_URI)}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString(SCOPES)}" +
                $"&state={state}" +
                $"&access_type=offline" +
                $"&prompt=select_account";

            // Iniciar listener antes de abrir o navegador
            var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5834/oauth/");
            listener.Start();

            try
            {
                // Abrir navegador para autenticação
                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });

                // Aguardar callback (timeout de 2 minutos)
                var contextTask = listener.GetContextAsync();
                if (await Task.WhenAny(contextTask, Task.Delay(120000)) != contextTask)
                {
                    throw new TimeoutException("Tempo limite de autenticação excedido.");
                }

                var context = contextTask.Result;
                string code = context.Request.QueryString["code"];
                string returnedState = context.Request.QueryString["state"];
                string error = context.Request.QueryString["error"];

                // Responder ao navegador
                string responseHtml;
                if (!string.IsNullOrEmpty(error))
                {
                    responseHtml = "<html><body style='background:#1e1e1e;color:white;font-family:Segoe UI;text-align:center;padding-top:50px;'>" +
                        "<h2 style='color:#ff4444;'>Autenticação cancelada</h2><p>Você pode fechar esta janela.</p></body></html>";
                    SendResponse(context, responseHtml);
                    throw new OperationCanceledException("Autenticação cancelada pelo usuário.");
                }

                if (returnedState != state)
                {
                    responseHtml = "<html><body style='background:#1e1e1e;color:white;font-family:Segoe UI;text-align:center;padding-top:50px;'>" +
                        "<h2 style='color:#ff4444;'>Erro de segurança</h2><p>Você pode fechar esta janela.</p></body></html>";
                    SendResponse(context, responseHtml);
                    throw new InvalidOperationException("State inválido - possível ataque CSRF.");
                }

                responseHtml = "<html><body style='background:#1e1e1e;color:white;font-family:Segoe UI;text-align:center;padding-top:50px;'>" +
                    "<h2 style='color:#00ff00;'>Login realizado!</h2><p>Você pode fechar esta janela e voltar ao aplicativo.</p></body></html>";
                SendResponse(context, responseHtml);

                // Trocar código por token
                string accessToken = await ExchangeCodeForTokenAsync(code);

                // Obter informações do usuário
                return await GetUserInfoAsync(accessToken);
            }
            finally
            {
                listener.Stop();
                listener.Close();
            }
        }

        private async Task<string> ExchangeCodeForTokenAsync(string code)
        {
            using (var client = new HttpClient())
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "code", code },
                    { "client_id", CLIENT_ID },
                    { "client_secret", CLIENT_SECRET },
                    { "redirect_uri", REDIRECT_URI },
                    { "grant_type", "authorization_code" }
                });

                var response = await client.PostAsync(TOKEN_ENDPOINT, content);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorObj = JObject.Parse(json);
                    throw new HttpRequestException($"Erro ao obter token: {errorObj["error_description"] ?? errorObj["error"]}");
                }

                var tokenData = JObject.Parse(json);
                return tokenData["access_token"]?.ToString()
                    ?? throw new InvalidOperationException("Access token não recebido.");
            }
        }

        private async Task<(string email, string displayName)> GetUserInfoAsync(string accessToken)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

                var response = await client.GetAsync(USERINFO_ENDPOINT);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("Erro ao obter informações do usuário Google.");

                var userInfo = JObject.Parse(json);
                string email = userInfo["email"]?.ToString();
                string name = userInfo["name"]?.ToString();

                if (string.IsNullOrEmpty(email))
                    throw new InvalidOperationException("Email não disponível na conta Google.");

                return (email, name ?? email);
            }
        }

        private void SendResponse(HttpListenerContext context, string html)
        {
            try
            {
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(html);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch { }
        }
    }
}
