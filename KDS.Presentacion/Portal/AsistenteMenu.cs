using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using KDS.PRE.Vistas;

namespace KDS.PRE.Portal
{
    public record MensajeChat(string Rol, string Texto);

    public record RespuestaBot(string Respuesta, List<PlatoModel> Sugeridos, string Nota, string Fuente, Dictionary<int, int>? Pedido = null,
                               bool EnviarPedido = false, string? MetodoPago = null);

    public class AsistenteMenu
    {
        private const string Modelo = "claude-opus-5-5";
        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

        private readonly AnthropicClient? cliente;

        public AsistenteMenu()
        {
            string? clave = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (!string.IsNullOrWhiteSpace(clave))
            {
                cliente = new AnthropicClient { ApiKey = clave };
            }
        }

        public bool UsaClaude => cliente != null;

        public async Task<RespuestaBot> ResponderAsync(string mensaje, IReadOnlyList<MensajeChat> historial, List<PlatoModel> menu, int mesa,
                                                       IReadOnlyDictionary<int, int> carrito, string? metodoPago = null)
        {
            if (cliente != null)
            {
                try
                {
                    return await PreguntarAClaudeAsync(mensaje, historial, menu, mesa, carrito, metodoPago);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Claude no respondió: " + ex.Message);
                }
            }

            return ResponderSinConexion(mensaje, menu, carrito, metodoPago, historial);
        }

        private async Task<RespuestaBot> PreguntarAClaudeAsync(string mensaje, IReadOnlyList<MensajeChat> historial, List<PlatoModel> menu, int mesa,
                                                               IReadOnlyDictionary<int, int> carrito, string? metodoPago)
        {
            var carta = new StringBuilder();
            foreach (var p in menu)
            {
                carta.AppendLine($"- id {p.IdPlato}: {p.Nombre} [{CategoriasMenu.De(p)}] (${p.Precio.ToString("N0", Cultura)} COP)");
            }

            string sistema =
                "Eres el mesero virtual de un restaurante colombiano y atiendes a un cliente desde su celular. " +
                $"El cliente está en la mesa {mesa}. Responde en español, en tono cercano y en máximo tres frases cortas. " +
                "Solo puedes recomendar platos de esta carta, con sus precios reales en pesos colombianos:\n" +
                carta +
                "Si el cliente menciona un presupuesto, no sugieras combinaciones que lo superen y di cuánto suman. " +
                "Si pide quitar o cambiar un ingrediente (por ejemplo 'sin cebolla'), escribe esa indicación corta en nota_cocina " +
                "para que viaje con el pedido; si no hay indicación, deja nota_cocina vacía. " +
                "En ids_sugeridos pon solo ids de la carta que recomiendas en esta respuesta. " +
                "No inventes ingredientes ni platos; si no sabes algo de un plato, dilo y ofrece preguntarle al mesero. " +
                "El cliente suele escribir informal, con jerga colombiana, abreviaturas, sin tildes o con errores de ortografía; " +
                "interpreta lo que quiere decir y no le corrijas la forma de escribir. Por ejemplo: 'lucas', 'barras' o 'mil' son miles de pesos " +
                "('25 lucas' = $25.000), 'pa'' es 'para', 'algo pa' la sed' es una bebida, 'algo pa' picar' es un acompañamiento, " +
                "'estoy pelado' significa que tiene poca plata, 'somos 3' significa que el presupuesto se reparte entre tres, " +
                "los diminutivos ('una gaseosita', 'unas papitas') se refieren al plato normal, y 'q', 'xq', 'tmb', 'porfa' son abreviaturas. " +
                "Si el mensaje es ambiguo, haz una sola pregunta corta. Contesta con calidez y naturalidad colombiana, sin exagerar la jerga. " +
                "Pedido actual del cliente: " + DescribirCarrito(carrito, menu) + ". " +
                "Si el cliente pide agregar, quitar o cambiar productos (aunque cambie de idea varias veces en el mismo mensaje, por ejemplo " +
                "'quiero 3 hamburguesas con una coca cola, quítale una hamburguesa, mejor no la coca cola sino agua, bueno ponme la coca cola también'), " +
                "aplica cada cambio en orden sobre el pedido actual, pon cambia_pedido en true y escribe en pedido_final el pedido completo resultante " +
                "(cada id de la carta con su cantidad total, sin los que quedaron en cero). Si pide algo que no está en la carta, dile amablemente que no lo hay. " +
                "En la respuesta repite cómo quedó el pedido y el total. Si el mensaje no cambia el pedido, pon cambia_pedido en false y pedido_final vacío. " +
                "Lee con cuidado todo el mensaje y la conversación anterior: el cliente puede pedir varias cosas a la vez, usar números en letras " +
                "('un par' = 2, 'media docena' = 6), referirse a algo dicho antes ('otra igual', 'lo mismo para mi amigo', 'quita lo último') " +
                "o dictar por voz con errores de transcripción ('am burguesa', 'limo nada', 'coca cola' por gaseosa). Asocia cada cosa al plato más parecido de la carta. " +
                "TÚ PUEDES HACER EL PEDIDO POR EL CLIENTE. Métodos de pago que acepta el restaurante: " + MetodosPago.Describir() + ". " +
                "Método de pago elegido hasta ahora: " + (metodoPago ?? "ninguno") + ". " +
                "Si el cliente dice cómo va a pagar ('pago con nequi', 'en efectivo', 'con tarjeta'), pon ese método en metodo_pago; si no lo dice, déjalo vacío. " +
                "Si el cliente pide que envíes, mandes, confirmes o hagas el pedido ('mándalo', 'hazme el pedido', 'eso es todo', 'sí, envíalo' después de que tú le preguntaste), " +
                "y el pedido resultante tiene al menos un producto y ya hay método de pago (elegido antes o en este mensaje), pon enviar_pedido en true " +
                "y en la respuesta dile qué se envió, el total y que el mesero lo confirma en un momento. " +
                "Si quiere enviarlo pero falta el método de pago, pon enviar_pedido en false y pregúntale cómo va a pagar nombrando las opciones. " +
                "Si está vacío el pedido, dile que primero escoja algo. Nunca envíes el pedido si el cliente no lo pidió claramente; " +
                "cuando el cliente termine de armar su pedido, pregúntale '¿Te lo envío al mesero?'. " +
                "Nunca pidas ni recibas números de tarjeta, claves o códigos: el pago con tarjeta se hace con el datáfono en la mesa.";

            var mensajes = new List<BetaMessageParam>();
            foreach (var m in historial.TakeLast(16))
            {
                if (string.IsNullOrWhiteSpace(m.Texto)) continue;
                if (m.Rol == "bot")
                    mensajes.Add(new BetaAssistantMessageParam { Content = m.Texto });
                else
                    mensajes.Add(new BetaUserMessageParam { Content = m.Texto });
            }
            mensajes.Add(new BetaUserMessageParam { Content = mensaje });

            while (mensajes.Count > 1 && mensajes[0].Role != "user")
                mensajes.RemoveAt(0);

            var idsPago = MetodosPago.Disponibles().Select(m => m.Id).Prepend(string.Empty).ToArray();
            var esquema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
            {
              "type": "object",
              "properties": {
                "respuesta": { "type": "string" },
                "ids_sugeridos": { "type": "array", "items": { "type": "integer" } },
                "nota_cocina": { "type": "string" },
                "cambia_pedido": { "type": "boolean" },
                "enviar_pedido": { "type": "boolean" },
                "metodo_pago": { "type": "string" },
                "pedido_final": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": { "id": { "type": "integer" }, "cantidad": { "type": "integer" } },
                    "required": ["id", "cantidad"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["respuesta", "ids_sugeridos", "nota_cocina", "cambia_pedido", "pedido_final", "enviar_pedido", "metodo_pago"],
              "additionalProperties": false
            }
            """)!;
            var propiedades = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(esquema["properties"].GetRawText())!;
            propiedades["metodo_pago"] = JsonSerializer.SerializeToElement(new { type = "string", @enum = idsPago });
            esquema["properties"] = JsonSerializer.SerializeToElement(propiedades);

            var parametros = new MessageCreateParams
            {
                Model = Modelo,
                MaxTokens = 2000,
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                System = sistema,
                Messages = mensajes,
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.Medium,
                    Format = new BetaJsonOutputFormat { Schema = esquema }
                }
            };

            BetaMessage respuesta = await cliente!.Beta.Messages.Create(parametros);

            if (respuesta.StopReason == "refusal")
            {
                return new RespuestaBot("Esa pregunta no la puedo responder, pero con gusto te ayudo a escoger algo de la carta.", new(), "", "claude");
            }

            string json = string.Concat(respuesta.Content
                .Select(b => b.TryPickText(out var t) ? t.Text : string.Empty));

            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;

            var ids = raiz.GetProperty("ids_sugeridos").EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number)
                .Select(e => e.GetInt32())
                .ToHashSet();

            Dictionary<int, int>? pedido = null;
            if (raiz.TryGetProperty("cambia_pedido", out var cambia) && cambia.ValueKind == JsonValueKind.True)
            {
                var validos = menu.Select(p => p.IdPlato).ToHashSet();
                pedido = new Dictionary<int, int>();
                foreach (var item in raiz.GetProperty("pedido_final").EnumerateArray())
                {
                    int id = item.GetProperty("id").GetInt32();
                    int cantidad = item.GetProperty("cantidad").GetInt32();
                    if (validos.Contains(id) && cantidad > 0) pedido[id] = Math.Min(cantidad, 20);
                }
            }

            string? metodoNuevo = raiz.TryGetProperty("metodo_pago", out var mp) ? MetodosPago.Buscar(mp.GetString())?.Id : null;
            bool enviar = raiz.TryGetProperty("enviar_pedido", out var env) && env.ValueKind == JsonValueKind.True;
            var final = pedido ?? carrito.Where(c => c.Value > 0).ToDictionary(c => c.Key, c => c.Value);
            if (enviar && (final.Count == 0 || (metodoNuevo ?? metodoPago) == null)) enviar = false;

            return new RespuestaBot(
                raiz.GetProperty("respuesta").GetString() ?? string.Empty,
                menu.Where(p => ids.Contains(p.IdPlato)).ToList(),
                raiz.GetProperty("nota_cocina").GetString() ?? string.Empty,
                "claude",
                pedido,
                enviar,
                metodoNuevo);
        }

        private static string DescribirCarrito(IReadOnlyDictionary<int, int> carrito, List<PlatoModel> menu)
        {
            var partes = carrito.Where(c => c.Value > 0)
                .Select(c => (Plato: menu.FirstOrDefault(p => p.IdPlato == c.Key), Cantidad: c.Value))
                .Where(c => c.Plato != null)
                .Select(c => $"{c.Cantidad} x {c.Plato!.Nombre} (id {c.Plato.IdPlato})")
                .ToList();
            return partes.Count == 0 ? "vacío" : string.Join(", ", partes);
        }

        private static string OpcionesPago()
        {
            var nombres = MetodosPago.Disponibles().Select(m => m.Nombre.Split(' ')[0].ToLowerInvariant()).ToList();
            return nombres.Count == 1 ? nombres[0] : string.Join(", ", nombres.Take(nombres.Count - 1)) + " o " + nombres[^1];
        }

        private static string NombrePago(string id) => MetodosPago.Buscar(id)?.Nombre.Split(' ')[0].ToLowerInvariant() ?? id;

        private static string ListarPedido(IReadOnlyDictionary<int, int> pedido, List<PlatoModel> menu)
        {
            var partes = pedido.Where(c => c.Value > 0)
                .Select(c => (Plato: menu.FirstOrDefault(p => p.IdPlato == c.Key), Cantidad: c.Value))
                .Where(c => c.Plato != null)
                .Select(c => $"{c.Cantidad} {c.Plato!.Nombre}")
                .ToList();
            return partes.Count switch
            {
                0 => "nada",
                1 => partes[0],
                _ => string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[^1]
            };
        }

        private static decimal TotalPedido(IReadOnlyDictionary<int, int> pedido, List<PlatoModel> menu) =>
            pedido.Sum(c => (menu.FirstOrDefault(p => p.IdPlato == c.Key)?.Precio ?? 0) * c.Value);

        private static string ResumirPedido(Dictionary<int, int> pedido, List<PlatoModel> menu)
        {
            var lineas = pedido.Where(c => c.Value > 0)
                .Select(c => (Plato: menu.FirstOrDefault(p => p.IdPlato == c.Key), Cantidad: c.Value))
                .Where(c => c.Plato != null)
                .ToList();

            if (lineas.Count == 0) return "Listo, tu pedido quedó vacío.";

            var partes = lineas.Select(l => $"{l.Cantidad} {l.Plato!.Nombre}").ToList();
            string lista = partes.Count == 1 ? partes[0] : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[^1];
            decimal total = lineas.Sum(l => l.Plato!.Precio * l.Cantidad);
            return $"Listo, tu pedido queda así: {lista}. Total {Pesos(total)}.";
        }

        public const string PreguntaPago = "¿Cómo vas a pagar?";
        public const string PreguntaEnvio = "¿Te lo envío al mesero?";

        public static RespuestaBot ResponderSinConexion(string mensaje, List<PlatoModel> menu, IReadOnlyDictionary<int, int>? carrito = null,
                                                        string? metodoActual = null, IReadOnlyList<MensajeChat>? historial = null)
        {
            string texto = InterpreteColoquial.Normalizar(mensaje);
            string nota = InterpreteColoquial.LeerNotas(texto);
            string avisoNota = nota.Length > 0 ? $" Le dejo dicho a cocina: {nota}." : string.Empty;

            if (menu.Count == 0)
                return new RespuestaBot("Uy, en este momento no hay platos disponibles. Llama al mesero y él te ayuda.", new(), nota, "local");

            string ultimoBot = historial?.LastOrDefault(h => h.Rol == "bot")?.Texto ?? string.Empty;
            bool preguntePago = ultimoBot.Contains(PreguntaPago);
            bool pregunteEnvio = ultimoBot.Contains(PreguntaEnvio);

            string? metodoDicho = MetodosPago.Buscar(InterpreteColoquial.LeerMetodoPago(texto))?.Id;
            string? metodoNoAceptado = metodoDicho == null ? InterpreteColoquial.LeerMetodoPago(texto) : null;
            string? metodo = metodoDicho ?? metodoActual;
            bool afirma = InterpreteColoquial.EsAfirmacion(texto);
            bool quiereEnviar = InterpreteColoquial.QuiereEnviar(texto)
                || (pregunteEnvio && afirma)
                || (preguntePago && metodoDicho != null);

            var actual = (carrito ?? new Dictionary<int, int>()).Where(c => c.Value > 0).ToDictionary(c => c.Key, c => c.Value);
            var cambio = InterpreteColoquial.InterpretarPedido(texto, menu, actual);
            var final = cambio?.Cantidades ?? actual;

            string faltantes = cambio != null && cambio.NoDisponibles.Count > 0
                ? $" Eso sí, no tenemos {string.Join(" ni ", cambio.NoDisponibles)}."
                : string.Empty;
            string avisoMetodo = metodoNoAceptado != null
                ? $" Por ahora no recibimos {metodoNoAceptado}; puedes pagar con {OpcionesPago()}."
                : string.Empty;

            if (quiereEnviar)
            {
                if (final.Count == 0)
                    return new RespuestaBot("Todavía no tienes nada en el pedido. Dime qué se te antoja y te lo armo.", new(), nota, "local", cambio?.Cantidades, false, metodoDicho);

                if (metodo == null)
                    return new RespuestaBot($"{ResumirPedido(final, menu)}{faltantes}{avisoMetodo} {PreguntaPago} Puede ser {OpcionesPago()}.{avisoNota}",
                        new(), nota, "local", cambio?.Cantidades, false, null);

                return new RespuestaBot(
                    $"¡Hecho! Le envío al mesero: {ListarPedido(final, menu)}. Total {Pesos(TotalPedido(final, menu))}, pagas con {NombrePago(metodo)}.{faltantes}{avisoNota} En un momento lo confirma.",
                    new(), nota, "local", cambio?.Cantidades, true, metodoDicho);
            }

            if (cambio != null)
            {
                string resumen = ResumirPedido(cambio.Cantidades, menu) + faltantes;
                if (cambio.NoDisponibles.Count > 0) resumen += " ¿Te provoca otra cosa de la carta?";
                else if (cambio.Cantidades.Count > 0) resumen += " " + PreguntaEnvio;
                return new RespuestaBot(resumen + avisoMetodo + avisoNota, new(), nota, "local", cambio.Cantidades, false, metodoDicho);
            }

            if (metodoDicho != null || metodoNoAceptado != null)
            {
                string texto2 = metodoDicho == null
                    ? avisoMetodo.Trim()
                    : actual.Count > 0
                        ? $"Anotado, pagas con {NombrePago(metodoDicho)}. {PreguntaEnvio}"
                        : $"Anotado, pagas con {NombrePago(metodoDicho)}. Ahora dime qué quieres pedir.";
                return new RespuestaBot(texto2, new(), nota, "local", null, false, metodoDicho);
            }

            if (InterpreteColoquial.EsSaludo(texto))
                return new RespuestaBot("¡Quiubo! Cuéntame cuánto quieres gastar o qué se te antoja (algo pa' la sed, pa' picar o bien llenador) y te ayudo.", new(), nota, "local");

            if (InterpreteColoquial.EsAgradecimiento(texto) && nota.Length == 0)
                return new RespuestaBot("¡Con gusto! Cuando tengas todo, toca «Ver pedido» y se lo mandamos al mesero.", new(), nota, "local");

            decimal? presupuesto = InterpreteColoquial.LeerPresupuesto(texto);
            int personas = InterpreteColoquial.LeerPersonas(texto);
            var nombrados = InterpreteColoquial.BuscarPlatosNombrados(texto, menu);

            if (nombrados.Count > 0 && presupuesto == null)
            {
                bool variosDistintos = nombrados.Select(p => CategoriasMenu.De(p)).Distinct().Count() == nombrados.Count;
                string cuales = Enumerar(nombrados.Take(3).ToList(), variosDistintos ? "y" : "o");
                string frase = nombrados.Count == 1
                    ? $"¡De una! {cuales}. Tócalo abajo y te lo agrego al pedido."
                    : variosDistintos
                        ? $"¡De una! {cuales}. Tócalos abajo y te los agrego al pedido."
                        : $"Tengo {cuales}. ¿Cuál te provoca?";
                return new RespuestaBot(frase + avisoNota, nombrados.Take(3).ToList(), nota, "local");
            }

            var antojo = InterpreteColoquial.LeerAntojo(texto);
            var candidatos = FiltrarPorAntojo(antojo, menu);

            if (presupuesto is decimal total)
            {
                decimal tope = personas > 1 ? Math.Floor(total / personas) : total;
                string porPersona = personas > 1 ? $" (unos {Pesos(tope)} por persona)" : string.Empty;
                var alcanza = candidatos.Where(p => p.Precio <= tope).OrderByDescending(p => p.Precio).ToList();

                if (alcanza.Count == 0)
                {
                    var masBarato = menu.OrderBy(p => p.Precio).First();
                    return new RespuestaBot(
                        $"Con {Pesos(total)}{porPersona} no alcanza pa' ningún plato, lo más económico es {masBarato.Nombre} a {Pesos(masBarato.Precio)}.",
                        new() { masBarato }, nota, "local");
                }

                var combo = antojo is Antojo.Ninguno or Antojo.Hambre ? MejorPareja(FiltrarPorAntojo(Antojo.Ninguno, menu).Where(p => p.Precio <= tope).ToList(), tope) : null;
                if (combo != null)
                {
                    var (a, b) = combo.Value;
                    return new RespuestaBot(
                        $"Con {Pesos(total)}{porPersona} te alcanza pa' {a.Nombre} y {b.Nombre}, en total {Pesos(a.Precio + b.Precio)}" +
                        (personas > 1 ? " cada uno." : ".") + avisoNota,
                        new() { a, b }, nota, "local");
                }

                var opciones = alcanza.Take(3).ToList();
                return new RespuestaBot($"Con {Pesos(total)}{porPersona} te recomiendo {Enumerar(opciones)}.{avisoNota}", opciones, nota, "local");
            }

            List<PlatoModel> sugeridos;
            string respuesta;

            switch (antojo)
            {
                case Antojo.Barato:
                    sugeridos = menu.OrderBy(p => p.Precio).Take(3).ToList();
                    respuesta = $"Lo más económico es {Enumerar(sugeridos)}.";
                    break;
                case Antojo.Bebida:
                    sugeridos = candidatos.OrderBy(p => p.Precio).Take(3).ToList();
                    respuesta = $"Pa' la sed tengo {Enumerar(sugeridos)}.";
                    break;
                case Antojo.Picar:
                    sugeridos = candidatos.OrderBy(p => p.Precio).Take(3).ToList();
                    respuesta = $"Pa' picar te va bien {Enumerar(sugeridos)}.";
                    break;
                case Antojo.Hambre:
                    sugeridos = candidatos.OrderByDescending(p => p.Precio).Take(2).ToList();
                    respuesta = $"Si vienes con hambre, lo más llenador es {Enumerar(sugeridos)}.";
                    break;
                default:
                    sugeridos = candidatos.OrderByDescending(p => p.Precio).Take(3).ToList();
                    respuesta = $"Te puedo recomendar {Enumerar(sugeridos)}. Si me dices cuánto quieres gastar (por ejemplo «tengo 25 lucas») te armo algo a la medida.";
                    break;
            }

            if (personas > 1)
                respuesta += $" Como son {personas}, agrega {personas} de cada uno.";

            return new RespuestaBot(respuesta + avisoNota, sugeridos, nota, "local");
        }

        private static List<PlatoModel> FiltrarPorAntojo(Antojo antojo, List<PlatoModel> menu)
        {
            string? categoria = antojo switch
            {
                Antojo.Bebida => CategoriasMenu.Bebida,
                Antojo.Picar => CategoriasMenu.Acompanamiento,
                Antojo.Hambre => CategoriasMenu.Comida,
                _ => null
            };

            if (categoria == null) return menu;

            var filtrados = menu.Where(p => CategoriasMenu.De(p) == categoria).ToList();
            return filtrados.Count > 0 ? filtrados : menu;
        }

        private static (PlatoModel, PlatoModel)? MejorPareja(List<PlatoModel> platos, decimal tope)
        {
            (PlatoModel, PlatoModel)? mejor = null;
            decimal mejorSuma = 0;

            for (int i = 0; i < platos.Count; i++)
            {
                for (int j = i + 1; j < platos.Count; j++)
                {
                    decimal suma = platos[i].Precio + platos[j].Precio;
                    bool distintos = CategoriasMenu.De(platos[i]) != CategoriasMenu.De(platos[j]);
                    if (suma <= tope && distintos && suma > mejorSuma)
                    {
                        mejorSuma = suma;
                        mejor = (platos[i], platos[j]);
                    }
                }
            }

            return mejor;
        }

        private static string Enumerar(List<PlatoModel> platos, string union = "o")
        {
            var partes = platos.Select(p => $"{p.Nombre} ({Pesos(p.Precio)})").ToList();
            return partes.Count switch
            {
                0 => "algo de la carta",
                1 => partes[0],
                _ => string.Join(", ", partes.Take(partes.Count - 1)) + $" {union} " + partes[^1]
            };
        }

        private static string Pesos(decimal valor) => "$" + valor.ToString("N0", Cultura);
    }
}
