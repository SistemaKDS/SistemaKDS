using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KDS.PRE.IA
{
    public record TurnoGemini(bool EsModelo, string Texto);

    public record FuenteWeb(string Titulo, string Url);

    public record RespuestaGemini(string Texto, List<FuenteWeb> Fuentes, List<string> Busquedas, string? SugerenciasHtml);

    public class ClaveGeminiInvalida : Exception
    {
        public ClaveGeminiInvalida(string detalle) : base(detalle) { }
    }

    public class CuotaGeminiAgotada : Exception
    {
        public CuotaGeminiAgotada(string detalle) : base(detalle) { }
    }

    public static class GeminiCliente
    {
        private const string ModeloPorDefecto = "gemini-3.5-flash-lite";
        private static readonly string[] ModelosRespaldo = { "gemini-3.1-flash-lite", "gemini-2.5-flash-lite", "gemini-3.6-flash" };
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private static readonly TimeSpan EsperaPorIntento = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan EsperaTotal = TimeSpan.FromSeconds(25);

        private static string? Clave =>
            Leer("GEMINI_API_KEY") ?? Leer("GOOGLE_API_KEY");

        public static string Modelo => Leer("GEMINI_MODELO") ?? ModeloPorDefecto;

        public static bool Disponible => Clave != null;

        public static bool BusquedaWeb => Leer("KDS_GEMINI_BUSQUEDA") != "0";

        public const string AyudaClave =
            "La clave de Gemini no es válida. Créala en https://aistudio.google.com/apikey, cópiala completa con el botón de copiar " +
            "y en el cmd escribe: setx GEMINI_API_KEY \"la-clave\" (sin espacios). Luego cierra y abre Visual Studio.";

        public const string AyudaCuota =
            "Gemini dice que se acabó la cuota de tu clave por ahora, así que no puedo responder preguntas libres. " +
            "Revisa tu uso en https://aistudio.google.com/rate-limit: si es el límite por minuto, espera un momento; si es el diario, se renueva mañana. " +
            "Para no quedarte sin cuota, activa la facturación en AI Studio (Get API key → Set up billing). Mientras tanto te respondo lo básico de pedidos, mesas y ventas.";

        public static async Task<string> GenerarAsync(string sistema, IEnumerable<TurnoGemini> turnos, int maxTokens = 1200, CancellationToken cancelar = default) =>
            (await GenerarConFuentesAsync(sistema, turnos, maxTokens, false, cancelar)).Texto;

        public static async Task<RespuestaGemini> GenerarConFuentesAsync(string sistema, IEnumerable<TurnoGemini> turnos, int maxTokens, bool buscarEnWeb, CancellationToken cancelar = default)
        {
            string clave = Clave ?? throw new InvalidOperationException("Falta la variable GEMINI_API_KEY.");

            var contenidos = new List<(string Rol, StringBuilder Texto)>();
            foreach (var t in turnos)
            {
                string texto = (t.Texto ?? string.Empty).Trim();
                if (texto.Length == 0) continue;
                string rol = t.EsModelo ? "model" : "user";
                if (contenidos.Count == 0 && rol == "model") continue;
                if (contenidos.Count > 0 && contenidos[^1].Rol == rol)
                    contenidos[^1].Texto.Append("\n").Append(texto);
                else
                    contenidos.Add((rol, new StringBuilder(texto)));
            }

            if (contenidos.Count == 0)
                throw new InvalidOperationException("No hay mensaje para enviar a Gemini.");

            object ArmarCuerpo(bool conBusqueda)
            {
                var cuerpo = new Dictionary<string, object>
                {
                    ["system_instruction"] = new { parts = new[] { new { text = sistema } } },
                    ["contents"] = contenidos.Select(c => new { role = c.Rol, parts = new[] { new { text = c.Texto.ToString() } } }).ToList(),
                    ["generationConfig"] = new { maxOutputTokens = maxTokens }
                };
                if (conBusqueda)
                    cuerpo["tools"] = new object[] { new Dictionary<string, object> { ["google_search"] = new { } } };
                return cuerpo;
            }

            var modelos = new[] { Modelo }.Concat(ModelosRespaldo.Where(m => m != Modelo)).ToList();
            var intentosBusqueda = buscarEnWeb && BusquedaWeb ? new[] { true, false } : new[] { false };
            string ultimoError = "sin respuesta";
            bool sinCuota = false;
            string? json = null;
            var reloj = System.Diagnostics.Stopwatch.StartNew();

            foreach (string modelo in modelos)
            {
                if (reloj.Elapsed >= EsperaTotal) break;
                bool siguienteModelo = false;
                foreach (bool conBusqueda in intentosBusqueda)
                {
                    if (reloj.Elapsed >= EsperaTotal) break;
                    var (codigo, contenidoJson) = await EnviarAsync(clave, modelo, ArmarCuerpo(conBusqueda), cancelar);
                    if (codigo == 0)
                    {
                        ultimoError = $"{modelo} tardó demasiado";
                        siguienteModelo = true;
                        break;
                    }
                    if (codigo is >= 200 and < 300)
                    {
                        json = contenidoJson;
                        break;
                    }

                    string detalle = MensajeDeError(contenidoJson);
                    if (detalle.Contains("API key", StringComparison.OrdinalIgnoreCase) || detalle.Contains("API_KEY", StringComparison.Ordinal))
                        throw new ClaveGeminiInvalida(detalle);

                    ultimoError = $"{modelo} respondió {codigo}: {detalle}";
                    if (codigo == 429)
                    {
                        sinCuota = true;
                        continue;
                    }
                    if (codigo is 400 && conBusqueda)
                        continue;
                    if (codigo is 404 or 500 or 503 or 400)
                    {
                        siguienteModelo = true;
                        break;
                    }
                    throw new InvalidOperationException("Gemini " + ultimoError);
                }

                if (json != null) break;
                if (!siguienteModelo && !sinCuota) break;
            }

            if (json == null)
            {
                if (sinCuota) throw new CuotaGeminiAgotada(ultimoError);
                throw new InvalidOperationException("Gemini " + ultimoError);
            }

            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;

            var texto2 = new StringBuilder();
            var fuentes = new List<FuenteWeb>();
            var busquedas = new List<string>();
            string? sugerencias = null;
            if (raiz.TryGetProperty("candidates", out var candidatos) && candidatos.ValueKind == JsonValueKind.Array && candidatos.GetArrayLength() > 0)
            {
                var primero = candidatos[0];
                if (primero.TryGetProperty("groundingMetadata", out var meta))
                {
                    if (meta.TryGetProperty("groundingChunks", out var trozos) && trozos.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var trozo in trozos.EnumerateArray())
                        {
                            if (!trozo.TryGetProperty("web", out var web)) continue;
                            string enlace = web.TryGetProperty("uri", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                            string titulo = web.TryGetProperty("title", out var ti) ? ti.GetString() ?? enlace : enlace;
                            if (enlace.Length > 0 && !fuentes.Any(f => f.Url == enlace)) fuentes.Add(new FuenteWeb(titulo, enlace));
                        }
                    }
                    if (meta.TryGetProperty("webSearchQueries", out var consultas) && consultas.ValueKind == JsonValueKind.Array)
                        busquedas.AddRange(consultas.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!));
                    if (meta.TryGetProperty("searchEntryPoint", out var entrada) && entrada.TryGetProperty("renderedContent", out var html))
                        sugerencias = html.GetString();
                }
                if (primero.TryGetProperty("content", out var contenido) && contenido.TryGetProperty("parts", out var partes))
                {
                    foreach (var parte in partes.EnumerateArray())
                    {
                        if (parte.TryGetProperty("thought", out var pensamiento) && pensamiento.ValueKind == JsonValueKind.True) continue;
                        if (parte.TryGetProperty("text", out var t)) texto2.Append(t.GetString());
                    }
                }
            }

            if (texto2.Length == 0)
            {
                string motivo = raiz.TryGetProperty("promptFeedback", out var fb) && fb.TryGetProperty("blockReason", out var br)
                    ? br.GetString() ?? "sin respuesta"
                    : "sin respuesta";
                throw new InvalidOperationException("Gemini no devolvió texto: " + motivo);
            }

            return new RespuestaGemini(texto2.ToString().Trim(), fuentes.Take(5).ToList(), busquedas, sugerencias);
        }

        private static async Task<(int Codigo, string Json)> EnviarAsync(string clave, string modelo, object cuerpo, CancellationToken cancelar)
        {
            string direccion = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelo)}:generateContent";
            using var solicitud = new HttpRequestMessage(HttpMethod.Post, direccion)
            {
                Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json")
            };
            solicitud.Headers.TryAddWithoutValidation("x-goog-api-key", clave);

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelar);
            limite.CancelAfter(EsperaPorIntento);
            try
            {
                using var respuesta = await Http.SendAsync(solicitud, limite.Token);
                string json = await respuesta.Content.ReadAsStringAsync(limite.Token);
                return ((int)respuesta.StatusCode, json);
            }
            catch (OperationCanceledException) when (!cancelar.IsCancellationRequested)
            {
                return (0, string.Empty);
            }
            catch (HttpRequestException ex)
            {
                return (503, ex.Message);
            }
        }

        public static JsonElement? ExtraerJson(string texto)
        {
            int inicio = texto.IndexOf('{');
            int fin = texto.LastIndexOf('}');
            if (inicio < 0 || fin <= inicio) return null;

            try
            {
                using var doc = JsonDocument.Parse(texto[inicio..(fin + 1)]);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string MensajeDeError(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var m))
                    return m.GetString() ?? json;
            }
            catch (JsonException)
            {
            }
            return json.Length > 300 ? json[..300] : json;
        }

        private static string? Leer(string variable)
        {
            string? valor = Environment.GetEnvironmentVariable(variable)?.Trim().Trim('"', '\'', ' ');
            return string.IsNullOrEmpty(valor) ? null : valor;
        }
    }
}
