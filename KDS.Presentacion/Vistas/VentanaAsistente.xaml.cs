using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KDS.PRE.IA;

namespace KDS.PRE.Vistas
{
    public partial class VentanaAsistente : Window
    {
        private static readonly Dictionary<string, VentanaAsistente> Abiertas = new();

        private readonly AsistenteEquipo asistente;
        private readonly SpeechSynthesizer voz = new();
        private bool ocupado;

        public static void Abrir(string rol, DependencyObject origen)
        {
            if (Abiertas.TryGetValue(rol, out var abierta))
            {
                if (abierta.WindowState == WindowState.Minimized) abierta.WindowState = WindowState.Normal;
                abierta.Activate();
                abierta.TxtPregunta.Focus();
                return;
            }

            var ventana = new VentanaAsistente(rol);
            var duena = Window.GetWindow(origen);
            if (duena != null && duena.IsVisible) ventana.Owner = duena;
            else ventana.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Abiertas[rol] = ventana;
            ventana.Closed += (_, _) => Abiertas.Remove(rol);
            ventana.Show();
        }

        public VentanaAsistente(string rol)
        {
            InitializeComponent();
            asistente = new AsistenteEquipo(rol);
            bool cocina = asistente.Rol == AsistenteEquipo.RolCocina;

            Title = cocina ? "MESALINK — Asistente de cocina" : "MESALINK — Asistente del mesero";
            TextoRol.Text = cocina ? "ASISTENTE · COCINA" : "ASISTENTE · MESERO";
            TextoModo.Text = AsistenteEquipo.UsaGemini
                ? $"Con Gemini ({GeminiCliente.Modelo}). Conoce los pedidos, la carta y las ventas del día."
                : "Sin IA: responde lo básico de pedidos, mesas y ventas. Para preguntas libres sobre el restaurante, configura GEMINI_API_KEY.";

            PrepararVoz();
            ChkVoz.IsChecked = cocina;

            var atajos = cocina
                ? new[] { "¿Qué preparo primero?", "¿Cuántas unidades de cada plato faltan?", "¿Hay notas o alergias?", "¿Qué pedido lleva más tiempo?" }
                : new[] { "¿Qué pedidos están listos para entregar?", "¿Hay solicitudes por aprobar?", "¿Cuánto debe la mesa 1?", "¿Qué le recomiendo a un cliente con 30 mil?" };

            foreach (string atajo in atajos)
            {
                var b = new Button
                {
                    Content = atajo,
                    Style = (Style)FindResource("Boton"),
                    Height = 30,
                    Padding = new Thickness(10, 0, 10, 0),
                    Margin = new Thickness(0, 0, 8, 8),
                    FontSize = 11
                };
                b.Click += (_, _) => Enviar(atajo);
                PanelAtajos.Children.Add(b);
            }

            AgregarBurbuja(false, cocina
                ? "Hola, cocina. Pregúntame qué falta por preparar, el orden, las notas de los pedidos o cualquier duda de cocina."
                : "Hola. Pregúntame por pedidos, mesas, cuentas, la carta, cómo usar el programa. Solo respondo temas del restaurante.");

            Loaded += (_, _) => TxtPregunta.Focus();
            Closed += (_, _) =>
            {
                try { voz.SpeakAsyncCancelAll(); } catch (Exception) { }
                voz.Dispose();
            };
        }

        private void PrepararVoz()
        {
            try
            {
                voz.SetOutputToDefaultAudioDevice();
                var espanol = voz.GetInstalledVoices()
                    .Where(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "es")
                    .OrderByDescending(v => v.VoiceInfo.Culture.Name == "es-CO" || v.VoiceInfo.Culture.Name == "es-MX")
                    .FirstOrDefault();
                if (espanol != null) voz.SelectVoice(espanol.VoiceInfo.Name);
            }
            catch (Exception)
            {
                ChkVoz.IsEnabled = false;
            }
        }

        private async void Enviar(string texto)
        {
            texto = texto.Trim();
            if (texto.Length == 0 || ocupado) return;

            ocupado = true;
            BtnEnviar.IsEnabled = false;
            PanelAtajos.IsEnabled = false;
            TxtPregunta.Clear();
            AgregarBurbuja(true, texto);
            var pensando = AgregarBurbuja(false, "Pensando…");

            try
            {
                string respuesta = await asistente.PreguntarAsync(texto);
                PanelChat.Children.Remove(pensando);
                AgregarBurbuja(false, respuesta);
                Hablar(respuesta);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Asistente: " + ex.Message);
                PanelChat.Children.Remove(pensando);
                AgregarBurbuja(false, "Dame un momento y vuelve a preguntarme, por favor.");
            }
            finally
            {
                ocupado = false;
                BtnEnviar.IsEnabled = true;
                PanelAtajos.IsEnabled = true;
                TxtPregunta.Focus();
            }
        }

        private Border AgregarBurbuja(bool esUsuario, string texto)
        {
            var contenido = new TextBox
            {
                Text = texto,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = esUsuario ? new SolidColorBrush(Color.FromRgb(17, 17, 17)) : new SolidColorBrush(Color.FromRgb(238, 238, 238)),
                FontSize = 13,
                Padding = new Thickness(0)
            };

            var burbuja = new Border
            {
                Child = contenido,
                Background = esUsuario ? new SolidColorBrush(Color.FromRgb(245, 166, 35)) : new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = esUsuario ? new Thickness(60, 0, 0, 10) : new Thickness(0, 0, 60, 10),
                HorizontalAlignment = esUsuario ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };

            PanelChat.Children.Add(burbuja);
            Desplazador.ScrollToEnd();
            return burbuja;
        }

        private void Hablar(string texto)
        {
            if (ChkVoz.IsChecked != true) return;
            try
            {
                voz.SpeakAsyncCancelAll();
                voz.SpeakAsync(texto.Replace("#", " número ").Replace("$", " ").Replace("- ", ""));
            }
            catch (Exception)
            {
            }
        }

        private void ChkVoz_Cambio(object sender, RoutedEventArgs e)
        {
            if (ChkVoz.IsChecked != true)
            {
                try { voz.SpeakAsyncCancelAll(); } catch (Exception) { }
            }
        }

        private void BtnEnviar_Click(object sender, RoutedEventArgs e) => Enviar(TxtPregunta.Text);

        private void TxtPregunta_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                Enviar(TxtPregunta.Text);
            }
        }

        private void BtnSalir_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnNueva_Click(object sender, RoutedEventArgs e)
        {
            if (ocupado) return;
            asistente.Reiniciar();
            PanelChat.Children.Clear();
            AgregarBurbuja(false, "Listo, empecemos de nuevo. ¿Qué necesitas?");
        }
    }
}
