using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KDS.PRE.Vistas;

namespace KDS.PRE.Portal
{
    public enum Antojo
    {
        Ninguno,
        Bebida,
        Picar,
        Hambre,
        Barato
    }

    public record CambioPedido(Dictionary<int, int> Cantidades, List<string> NoDisponibles);

    public static class InterpreteColoquial
    {
        private static readonly (string Patron, string Reemplazo)[] Expresiones =
        {
            (@"\bpa['´`]?(?=\s|$)", "para"),
            (@"\bq\b", "que"),
            (@"\bxq\b|\bpq\b", "porque"),
            (@"\btmb\b|\btb\b|\btbn\b", "tambien"),
            (@"\bxfa\b|\bporfa\b|\bporfis\b", "por favor"),
            (@"\bkiero\b|\bqiero\b", "quiero"),
            (@"\bbaratic[oa]s?\b|\bbaratit[oa]s?\b", "barato"),
            (@"\bha?m?bu?rgu?e?s+(a|ita)s?\b|\bburg(u)?er(s)?\b|\bburguesa\b|\bamburguesa\b", "hamburguesa"),
            (@"\bcocas?\b(?!\s?cola)", "gaseosa"),
            (@"\bgaseosita?s?\b|\bgaseocita?s?\b|\bgasiosa\b|\bcoca\s?cola\b|\bcocacola\b|\bpostobon\b", "gaseosa"),
            (@"\bpapita?s\b|\bpapas?\s+a\s+la\s+francesa\b", "papas"),
            (@"\blimonadit[ao]\b", "limonada"),
            (@"\bjuguit[oa]s?\b", "jugo"),
            (@"\bcervecit[ao]s?\b|\bpola(s)?\b", "cerveza"),
            (@"\bquesit[oa]\b", "queso"),
            (@"\bcebollita\b|\bcebolla\s+cabezona\b", "cebolla"),
            (@"\btomatico\b|\btomatito\b", "tomate")
        };

        private static readonly Dictionary<string, int> Numeros = new()
        {
            ["un"] = 1, ["uno"] = 1, ["una"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5,
            ["seis"] = 6, ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10, ["once"] = 11,
            ["doce"] = 12, ["trece"] = 13, ["catorce"] = 14, ["quince"] = 15, ["dieciseis"] = 16,
            ["diecisiete"] = 17, ["dieciocho"] = 18, ["diecinueve"] = 19, ["veinte"] = 20,
            ["veintiuno"] = 21, ["veintiun"] = 21, ["veintidos"] = 22, ["veintitres"] = 23,
            ["veinticuatro"] = 24, ["veinticinco"] = 25, ["veintiseis"] = 26, ["veintisiete"] = 27,
            ["veintiocho"] = 28, ["veintinueve"] = 29, ["treinta"] = 30, ["cuarenta"] = 40,
            ["cincuenta"] = 50, ["sesenta"] = 60, ["setenta"] = 70, ["ochenta"] = 80, ["noventa"] = 90,
            ["cien"] = 100
        };

        private static readonly string[] PalabrasVacias =
        {
            "de", "la", "el", "los", "las", "con", "y", "en", "para", "una", "uno", "un", "del", "al"
        };

        public static string Normalizar(string texto)
        {
            string t = QuitarTildes(texto.ToLowerInvariant());
            t = Regex.Replace(t, @"([a-zñ])\1{2,}", "$1");
            foreach (var (patron, reemplazo) in Expresiones)
                t = Regex.Replace(t, patron, reemplazo);
            return Regex.Replace(t, @"\s+", " ").Trim();
        }

        public static decimal? LeerPresupuesto(string t)
        {
            var conUnidad = Regex.Match(t, @"(\d+(?:[.,]\d+)?)\s*(lucas?|barras?|mil|k|lks)\b");
            if (conUnidad.Success && decimal.TryParse(conUnidad.Groups[1].Value.Replace(',', '.'),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out decimal miles))
            {
                return miles * 1000;
            }

            var completo = Regex.Match(t, @"\$?\s*(\d{1,3}(?:[.,]\d{3})+|\d{4,7})\b");
            if (completo.Success && decimal.TryParse(Regex.Replace(completo.Groups[1].Value, @"[.,]", ""), out decimal valor))
                return valor;

            var enLetras = Regex.Match(t, @"\b([a-z]+)(?:\s+y\s+([a-z]+))?\s+(lucas?|barras?|mil)\b");
            if (enLetras.Success && Numeros.TryGetValue(enLetras.Groups[1].Value, out int baseNum))
            {
                int extra = 0;
                if (enLetras.Groups[2].Success) Numeros.TryGetValue(enLetras.Groups[2].Value, out extra);
                return (baseNum + extra) * 1000m;
            }

            if (Regex.IsMatch(t, @"\b(una|un)\s+(luca|barra)\b")) return 1000;

            return null;
        }

        public static string LeerNotas(string t)
        {
            var notas = new List<string>();
            var patrones = new[]
            {
                @"\bsin\s+(?:nada\s+de\s+)?([a-zñ]+(?:\s+(?!y\b|con\b|por\b|que\b|para\b)[a-zñ]+)?)",
                @"\bno\s+(?:le\s+)?(?:eche|echen|ponga|pongan|heche|hechen|pongas|eches)\s+([a-zñ]+)",
                @"\bque\s+no\s+(?:tenga|lleve|traiga)\s+([a-zñ]+)",
                @"\bbien\s+(asad[oa]|cocid[oa]|tostad[oa]|frit[oa])",
                @"\bextra\s+([a-zñ]+)",
                @"\b(?:con\s+)?(?:harto|hartica|bastante|mucha|mucho)\s+([a-zñ]+)"
            };

            foreach (string patron in patrones)
            {
                foreach (Match m in Regex.Matches(t, patron))
                {
                    string cosa = m.Groups[1].Value.Trim();
                    if (cosa.Length < 3 || cosa is "hambre" or "plata" or "sed" or "afan") continue;

                    string nota = patron.Contains("bien") ? "bien " + cosa
                        : patron.Contains("extra") ? "extra " + cosa
                        : patron.Contains("harto") ? "con bastante " + cosa
                        : "sin " + cosa;

                    if (!notas.Contains(nota)) notas.Add(nota);
                }
            }

            return string.Join(", ", notas);
        }

        public static Antojo LeerAntojo(string t)
        {
            if (Regex.IsMatch(t, @"\b(barato|economic[oa]|poca plata|poquita plata|no tengo (mucha )?plata|estoy pelad[oa]|ando pelad[oa]|lo mas bajo)\b"))
                return Antojo.Barato;
            if (Regex.IsMatch(t, @"\b(sed|tomar|beber|bebida|refresco|gaseosa|jugo|limonada|cerveza|algo frio|algo helado)\b"))
                return Antojo.Bebida;
            if (Regex.IsMatch(t, @"\b(picar|picadita|picoteo|acompanamiento|acompanar|papas|entrada|algo ligero|algo suave)\b"))
                return Antojo.Picar;
            if (Regex.IsMatch(t, @"\b(hambre|hambriento|muerto de hambre|llenador|contundente|algo grande|bien servido|que llene|hamburguesa)\b"))
                return Antojo.Hambre;
            return Antojo.Ninguno;
        }

        public static bool EsSaludo(string t) =>
            Regex.IsMatch(t, @"^(hola|holi|buenas|buenos dias|buenas tardes|buenas noches|quiubo|quibo|que mas|q mas|ey|hey|alo)\b[\s!.,?]*$");

        public static bool EsAgradecimiento(string t) =>
            Regex.IsMatch(t, @"\b(gracias|muchas gracias|mil gracias|listo|de una|bacano|chevere|perfecto)\b") && t.Length < 40;

        public static bool QuiereEnviar(string t)
        {
            if (Regex.IsMatch(t, @"\bno\s+(?:lo\s+|la\s+|me\s+lo\s+)?(?:mandes|mande|envies|envie|pidas|pida|confirmes|confirme)\b|\btodavia no\b|\btodavia falta\b|\bespera\b"))
                return false;

            return Regex.IsMatch(t,
                @"\b(mandal[oa]s?|mandamel[oa]s?|mandemel[oa]s?|mandel[oa]s?|envial[oa]s?|enviamel[oa]s?|enviel[oa]s?|envialo ya|" +
                @"pidel[oa]|pidamel[oa]|pidemel[oa]|ordenal[oa]|" +
                @"(?:haz|has|hagame|haga|hazme|realiza|realizame|monta|montame|registra|registrame)(?:me)?\s+(?:el|mi|este|ese)\s+pedido|" +
                @"(?:mandar|enviar|pedir|confirmar|ordenar|montar)\s+(?:el|mi|ya|este|ese)?\s*pedido|" +
                @"(?:manda|envia|confirma|pide)\s+(?:el|mi|este|ese)\s+pedido|" +
                @"confirmo|confirmalo|confirmad[oa]|eso es todo|eso seria todo|seria todo|es todo|asi esta bien|asi queda bien|asi esta perfecto|" +
                @"ya puedes pedir|ya puedes mandar|ya pide|pide ya|ya manda|manda ya|listo mandalo|listo envialo)\b");
        }

        public static bool EsAfirmacion(string t) =>
            Regex.IsMatch(t, @"^(si|sii|sip|claro|dale|de una|hagale|hagamosle|listo|ok|okay|okey|va|vale|por favor|si por favor|si senor|si senora|obvio|correcto|exacto|eso|asi es)\b[\s!.,]*(por favor|porfa|gracias|mandalo|envialo|de una|dale)?[\s!.,]*$");

        public static string? LeerMetodoPago(string t)
        {
            if (Regex.IsMatch(t, @"\bdavi\s?plata\b")) return "Daviplata";
            if (Regex.IsMatch(t, @"\bnequi\b|\bneki\b")) return "Nequi";
            if (Regex.IsMatch(t, @"\b(transferencia|transfiero|transferir|transfer|bancolombia|bre\s?-?b|llave|pse)\b")) return "Transferencia";
            if (Regex.IsMatch(t, @"\b(tarjeta|datafono|credito|debito|visa|mastercard)\b")) return "Tarjeta";
            if (Regex.IsMatch(t, @"\b(efectivo|cash|billete|billetes|contado|en plata|plata en mano)\b")) return "Efectivo";
            return null;
        }

        public static int LeerPersonas(string t)
        {
            var m = Regex.Match(t, @"\b(?:somos|para)\s+(\d+|[a-z]+)\s*(?:personas|amigos|parceros|ninos|adultos)?\b");
            if (!m.Success) return 1;
            string v = m.Groups[1].Value;
            if (int.TryParse(v, out int n) || Numeros.TryGetValue(v, out n))
                return n is > 1 and <= 10 ? n : 1;
            return 1;
        }

        public static List<PlatoModel> BuscarPlatosNombrados(string t, List<PlatoModel> menu)
        {
            var palabrasCliente = Palabras(t);
            var encontrados = new List<(PlatoModel Plato, int Coincidencias)>();

            foreach (var plato in menu)
            {
                var palabrasPlato = Palabras(Normalizar(plato.Nombre));
                if (palabrasPlato.Count == 0) continue;

                int coincidencias = palabrasPlato.Count(pp => palabrasCliente.Any(pc => SePareceA(pc, pp)));
                bool nombreCompleto = coincidencias == palabrasPlato.Count;
                bool palabraFuerte = palabrasPlato.Any(pp => pp.Length >= 5 && palabrasCliente.Any(pc => SePareceA(pc, pp)));

                if (nombreCompleto || palabraFuerte)
                    encontrados.Add((plato, coincidencias));
            }

            if (encontrados.Count == 0) return new();
            int mejor = encontrados.Max(e => e.Coincidencias);
            return encontrados.Where(e => e.Coincidencias == mejor).Select(e => e.Plato).ToList();
        }

        private static readonly string[] CosasDeComer =
        {
            "agua", "jugo", "cerveza", "limonada", "gaseosa", "hamburguesa", "papas", "pizza", "perro", "salchipapa",
            "arepa", "empanada", "cafe", "tinto", "malteada", "pasta", "lasagna", "pollo", "costilla", "ensalada", "postre", "helado"
        };

        private static readonly string[] Conectores =
        {
            "pero", "ahora", "tambien", "bueno", "entonces", "mejor", "osea", "luego", "despues", "favor", "mas", "todo", "toda",
            "nada", "solo", "sola", "bien", "quiero", "dame", "porfa", "ponme", "otra", "otro", "vez", "por"
        };

        private enum Accion { Agregar, Quitar }

        public static CambioPedido? InterpretarPedido(string t, List<PlatoModel> menu, IReadOnlyDictionary<int, int> carritoActual)
        {
            var palabras = Regex.Matches(t, @"[a-zñ]+|\d+").Select(m => m.Value).ToList();
            var nombres = menu.ToDictionary(p => p, p => Palabras(Normalizar(p.Nombre)));
            var cantidades = carritoActual.Where(c => c.Value > 0).ToDictionary(c => c.Key, c => c.Value);
            var noHay = new List<string>();

            var accion = Accion.Agregar;
            int? cantidad = null;
            bool huboCambio = false;
            bool huboIntencion = false;
            int sueltas = 0;

            for (int i = 0; i < palabras.Count; i++)
            {
                string w = palabras[i];
                string siguiente = i + 1 < palabras.Count ? palabras[i + 1] : string.Empty;

                if (accion == Accion.Quitar && sueltas >= 3)
                {
                    accion = Accion.Agregar;
                    sueltas = 0;
                }

                if (w is "sin" or "menos")
                {
                    if (PlatoQueEmpiezaEn(palabras, i + 1, nombres).Plato != null)
                    {
                        accion = Accion.Quitar;
                        sueltas = 0;
                    }
                    else
                    {
                        i++;
                    }
                    continue;
                }

                if (w is "no" && siguiente is "quiero" or "quiere" or "queremos")
                {
                    sueltas = 0;
                    accion = Accion.Quitar;
                    huboIntencion = true;
                    i++;
                    continue;
                }

                if (Regex.IsMatch(w, @"^(quit|sac|elimin|borr|cancel|retir)[a-z]*$") || w is "cambia" or "cambiale" or "cambiame" or "vez")
                {
                    accion = Accion.Quitar;
                    sueltas = 0;
                    huboIntencion = true;
                    continue;
                }

                if (w is "quiero" or "queremos" or "dame" or "deme" or "denme" or "ponme" or "pon" or "ponle" or "traeme" or "traiganme" or "regalame"
                    || w is "agrega" or "agregale" or "agregame" or "anade" or "anademe" or "suma" or "tambien" or "por" or "mejor" or "pidame" or "pideme")
                {
                    accion = Accion.Agregar;
                    huboIntencion = huboIntencion || w is not "por" and not "mejor";
                    continue;
                }

                if (w is "otra" or "otro" or "otras" or "otros")
                {
                    accion = Accion.Agregar;
                    cantidad ??= 1;
                    huboIntencion = true;
                    continue;
                }

                if (int.TryParse(w, out int n) || Numeros.TryGetValue(w, out n))
                {
                    if (n is > 0 and <= 20) cantidad = n;
                    continue;
                }

                var (plato, usadas, puntos) = PlatoQueEmpiezaEn(palabras, i, nombres);
                if (plato != null && puntos == 1 && PlatoQueEmpiezaEn(palabras, i + 1, nombres) is { Plato: not null, Puntos: >= 2 } mejorSiguiente
                    && mejorSiguiente.Plato != plato)
                {
                    continue;
                }
                if (plato == null)
                {
                    if (!PalabrasVacias.Contains(w) && w.Length >= 3) sueltas++;
                    if (!Conectores.Contains(w) && CosasDeComer.Any(c => SePareceA(w, c)) && (huboIntencion || cantidad != null))
                    {
                        if (!noHay.Contains(w.TrimEnd('s'))) noHay.Add(w.TrimEnd('s'));
                        cantidad = null;
                    }
                    continue;
                }

                i += usadas - 1;
                cantidades.TryGetValue(plato.IdPlato, out int actual);

                if (accion == Accion.Quitar)
                {
                    int nuevo = cantidad is int q ? Math.Max(0, actual - q) : 0;
                    if (nuevo == 0) cantidades.Remove(plato.IdPlato); else cantidades[plato.IdPlato] = nuevo;
                    if (siguiente is "por") accion = Accion.Agregar;
                }
                else
                {
                    cantidades[plato.IdPlato] = Math.Min(20, actual + (cantidad ?? 1));
                }

                huboCambio = true;
                cantidad = null;
                sueltas = 0;
            }

            if (!huboCambio || (!huboIntencion && !palabras.Any(p => int.TryParse(p, out _) || Numeros.ContainsKey(p))))
                return null;

            return new CambioPedido(cantidades, noHay);
        }

        private static (PlatoModel? Plato, int Usadas, int Puntos) PlatoQueEmpiezaEn(List<string> palabras, int inicio, Dictionary<PlatoModel, List<string>> nombres)
        {
            if (inicio >= palabras.Count) return (null, 0, 0);
            string w = palabras[inicio];
            if (w.Length < 3 || PalabrasVacias.Contains(w) || Conectores.Contains(w)) return (null, 0, 0);

            PlatoModel? mejor = null;
            int mejorPuntos = 0, mejorUsadas = 0;

            foreach (var (plato, partes) in nombres)
            {
                if (!partes.Any(pp => SePareceA(w, pp))) continue;

                int puntos = 1, usadas = 1;
                for (int j = inicio + 1; j < palabras.Count && j <= inicio + 3; j++)
                {
                    string otra = palabras[j];
                    if (PalabrasVacias.Contains(otra)) { usadas++; continue; }
                    if (partes.Any(pp => SePareceA(otra, pp))) { puntos++; usadas = j - inicio + 1; }
                    else break;
                }

                if (puntos > mejorPuntos)
                {
                    mejor = plato;
                    mejorPuntos = puntos;
                    mejorUsadas = usadas;
                }
            }

            if (mejor == null) return (null, 0, 0);
            while (mejorUsadas > 1 && PalabrasVacias.Contains(palabras[inicio + mejorUsadas - 1])) mejorUsadas--;
            return (mejor, mejorUsadas, mejorPuntos);
        }

        private static List<string> Palabras(string texto)
        {
            return Regex.Matches(texto, @"[a-zñ]+")
                .Select(m => m.Value)
                .Where(p => p.Length >= 3 && !PalabrasVacias.Contains(p))
                .ToList();
        }

        private static bool SePareceA(string a, string b)
        {
            if (a == b) return true;
            if (a.Length < 4 || b.Length < 4) return false;
            if (a.TrimEnd('s') == b.TrimEnd('s')) return true;
            int tolerancia = Math.Max(a.Length, b.Length) >= 7 ? 2 : 1;
            return Math.Abs(a.Length - b.Length) <= tolerancia && Distancia(a, b) <= tolerancia;
        }

        private static int Distancia(string a, string b)
        {
            var previa = new int[b.Length + 1];
            var actual = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) previa[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                actual[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int costo = a[i - 1] == b[j - 1] ? 0 : 1;
                    actual[j] = Math.Min(Math.Min(actual[j - 1] + 1, previa[j] + 1), previa[j - 1] + costo);
                }
                (previa, actual) = (actual, previa);
            }

            return previa[b.Length];
        }

        private static string QuitarTildes(string texto)
        {
            var sb = new StringBuilder();
            foreach (char c in texto.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark || c == '̃')
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
