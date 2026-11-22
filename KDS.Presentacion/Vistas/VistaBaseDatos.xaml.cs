using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace KDS.PRE.Vistas
{
    public partial class VistaBaseDatos : Window
    {
        private const string TodosLosEstados = "Todos";
        private static readonly System.Globalization.CultureInfo Cultura = System.Globalization.CultureInfo.GetCultureInfo("es-CO");
        private static readonly string[] Periodos = { "Hoy", "Últimos 7 días", "Este mes", "Todo" };

        private readonly KdsAdministracionBLL bll = new();
        private readonly UsuarioModel dueno;
        private readonly DispatcherTimer refresco = new() { Interval = TimeSpan.FromSeconds(5) };
        private PedidoResumen? pedidoActual;
        private bool cargando;
        private bool creandoPedido;

        public bool HuboCambiosEnPlatos { get; private set; }

        public VistaBaseDatos(UsuarioModel dueno)
        {
            InitializeComponent();
            this.dueno = dueno;
            TextoDueno.Text = "DUEÑO · " + dueno.Nombre.ToUpper();

            cargando = true;
            CmbFiltroEstado.ItemsSource = new[] { TodosLosEstados }.Concat(KdsAdministracionBLL.Estados).ToList();
            CmbFiltroEstado.SelectedIndex = 0;
            CmbEstado.ItemsSource = KdsAdministracionBLL.Estados;
            CmbFiltroPeriodo.ItemsSource = Periodos;
            CmbFiltroPeriodo.SelectedIndex = Periodos.Length - 1;
            CmbFiltroPago.ItemsSource = new[] { TodosLosEstados }.Concat(KdsAdministracionBLL.EstadosPago).ToList();
            CmbFiltroPago.SelectedIndex = 0;
            CmbMetodoPago.ItemsSource = new[] { KdsAdministracionBLL.SinMetodo }.Concat(KdsAdministracionBLL.MetodosPago).ToList();
            CmbEstadoPago.ItemsSource = KdsAdministracionBLL.EstadosPago;
            CmbRol.ItemsSource = KdsDatosService.RolesUsuario;
            cargando = false;

            refresco.Tick += (_, _) => { if (!creandoPedido) CargarPedidos(silencioso: true); };
            Loaded += (_, _) =>
            {
                CargarPlatos();
                CargarPedidos();
                CargarUsuarios();
                refresco.Start();
            };
            Closed += (_, _) => refresco.Stop();
        }

        private void CargarPlatos()
        {
            try
            {
                var anterior = (CmbPlato.SelectedItem as PlatoModel)?.IdPlato;
                var platos = KdsDatosService.ObtenerPlatosActivos();
                CmbPlato.ItemsSource = platos;
                CmbPlato.SelectedItem = platos.FirstOrDefault(p => p.IdPlato == anterior);
            }
            catch (Exception ex)
            {
                Avisar("No se pudieron leer los platos: " + ex.Message);
            }
        }

        private void CargarPedidos(bool silencioso = false, int? seleccionar = null)
        {
            if (cargando) return;

            List<PedidoResumen> pedidos;
            try
            {
                pedidos = bll.ListarPedidos(CmbFiltroEstado.SelectedItem as string, DesdeFiltro(), MesaFiltro(), CmbFiltroPago.SelectedItem as string);
            }
            catch (Exception ex)
            {
                if (!silencioso) Avisar("No se pudo leer la base de datos: " + ex.Message);
                return;
            }

            int? id = seleccionar ?? pedidoActual?.IdPedido;
            if (silencioso && GrillaPedidos.ItemsSource is List<PedidoResumen> actuales && MismoContenido(actuales, pedidos))
                return;

            cargando = true;
            GrillaPedidos.ItemsSource = pedidos;
            var elegido = pedidos.FirstOrDefault(p => p.IdPedido == id);
            GrillaPedidos.SelectedItem = elegido;
            cargando = false;

            MostrarTotales(pedidos);

            bool mismoPedido = elegido != null && pedidoActual != null && elegido.IdPedido == pedidoActual.IdPedido;
            if (silencioso && mismoPedido)
            {
                var detalleElegido = (GrillaDetalle.SelectedItem as DetallePedidoModel)?.IdDetalle;
                pedidoActual = elegido;
                GrillaDetalle.ItemsSource = elegido!.Detalles;
                GrillaDetalle.SelectedItem = elegido.Detalles.FirstOrDefault(d => d.IdDetalle == detalleElegido);
                TituloPedido.Text = $"PEDIDO #{elegido.IdPedido} · $ {elegido.Total:N0}";
            }
            else if (!creandoPedido)
            {
                MostrarPedido(elegido);
            }
        }

        private DateTime? DesdeFiltro() => (CmbFiltroPeriodo.SelectedItem as string) switch
        {
            "Hoy" => DateTime.Today,
            "Últimos 7 días" => DateTime.Today.AddDays(-6),
            "Este mes" => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
            _ => null
        };

        private int? MesaFiltro() => int.TryParse(TxtFiltroMesa.Text.Trim(), out int m) && m > 0 ? m : null;

        private static string Pesos(decimal valor) => "$ " + valor.ToString("N0", Cultura);

        private void MostrarTotales(List<PedidoResumen> pedidos)
        {
            decimal ventas = pedidos.Sum(p => p.Total);
            decimal cobrado = pedidos.Where(p => p.EstadoPago == "Pagado").Sum(p => p.Total);

            TotalPedidos.Text = pedidos.Count.ToString("N0", Cultura);
            int unidades = pedidos.Sum(p => p.Unidades);
            TotalUnidades.Text = unidades == 1 ? "1 producto" : $"{unidades:N0} productos";
            TotalVentas.Text = Pesos(ventas);
            TotalPromedio.Text = "Promedio " + Pesos(pedidos.Count == 0 ? 0 : Math.Round(ventas / pedidos.Count));
            TotalCobrado.Text = Pesos(cobrado);
            TotalPorCobrar.Text = Pesos(ventas - cobrado);

            var porMetodo = pedidos.Where(p => p.EstadoPago == "Pagado" && p.MetodoPago.Length > 0)
                .GroupBy(p => p.MetodoPago)
                .Select(g => $"{g.Key} {Pesos(g.Sum(p => p.Total))}")
                .ToList();
            TotalPorMetodo.Text = porMetodo.Count == 0 ? "Sin pagos registrados" : string.Join(" · ", porMetodo);
            TotalPorMetodo.ToolTip = string.Join("\n", porMetodo);

            var porEstado = KdsAdministracionBLL.Estados
                .Select(e => (Estado: e, Cantidad: pedidos.Count(p => p.Estado == e)))
                .Where(x => x.Cantidad > 0)
                .Select(x => $"{x.Estado} {x.Cantidad}")
                .ToList();
            TotalPorEstado.Text = porEstado.Count == 0 ? "Sin pedidos" : string.Join(" · ", porEstado);
            TotalPorEstado.ToolTip = string.Join("\n", porEstado);
        }

        private static bool MismoContenido(List<PedidoResumen> a, List<PedidoResumen> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].IdPedido != b[i].IdPedido || a[i].NumeroMesa != b[i].NumeroMesa ||
                    a[i].Estado != b[i].Estado || a[i].Productos != b[i].Productos ||
                    a[i].MetodoPago != b[i].MetodoPago || a[i].EstadoPago != b[i].EstadoPago || a[i].ReferenciaPago != b[i].ReferenciaPago ||
                    string.Join("|", a[i].Detalles.Select(d => d.NotasEspeciales)) != string.Join("|", b[i].Detalles.Select(d => d.NotasEspeciales)))
                    return false;
            }
            return true;
        }

        private void MostrarPedido(PedidoResumen? pedido)
        {
            if (pedido != null && creandoPedido) SalirDeNuevoPedido();
            pedidoActual = pedido;
            bool hay = pedido != null;

            BtnGuardarPedido.IsEnabled = hay;
            BtnEliminarPedido.IsEnabled = hay;
            BtnAgregarProducto.IsEnabled = hay;

            if (pedido == null)
            {
                TituloPedido.Text = "SELECCIONA UN PEDIDO";
                TxtMesa.Text = string.Empty;
                CmbEstado.SelectedItem = null;
                CmbMetodoPago.SelectedItem = null;
                CmbEstadoPago.SelectedItem = null;
                TxtReferenciaPago.Text = string.Empty;
                GrillaDetalle.ItemsSource = null;
            }
            else
            {
                TituloPedido.Text = $"PEDIDO #{pedido.IdPedido} · $ {pedido.Total:N0}";
                TxtMesa.Text = pedido.NumeroMesa.ToString();
                CmbEstado.SelectedItem = pedido.Estado;
                CmbMetodoPago.SelectedItem = pedido.MetodoPago.Length == 0 ? KdsAdministracionBLL.SinMetodo : pedido.MetodoPago;
                CmbEstadoPago.SelectedItem = pedido.EstadoPago;
                TxtReferenciaPago.Text = pedido.ReferenciaPago;
                GrillaDetalle.ItemsSource = pedido.Detalles;
            }
            MostrarProducto(null);
        }

        private void MostrarProducto(DetallePedidoModel? detalle)
        {
            bool hay = detalle != null;
            BtnGuardarProducto.IsEnabled = hay;
            BtnQuitarProducto.IsEnabled = hay;
            TxtCantidad.Text = hay ? detalle!.Cantidad.ToString() : "1";
            TxtNota.Text = hay ? detalle!.NotasEspeciales : string.Empty;
            if (hay)
                CmbPlato.SelectedItem = (CmbPlato.ItemsSource as List<PlatoModel>)?.FirstOrDefault(p => p.IdPlato == detalle!.IdPlato);
        }

        private void CargarUsuarios(int? seleccionar = null)
        {
            try
            {
                var usuarios = bll.ListarUsuarios();
                GrillaUsuarios.ItemsSource = usuarios;
                GrillaUsuarios.SelectedItem = usuarios.FirstOrDefault(u => u.IdUsuario == seleccionar);
                if (GrillaUsuarios.SelectedItem == null) LimpiarUsuario();
            }
            catch (Exception ex)
            {
                Avisar("No se pudieron leer los usuarios: " + ex.Message);
            }
        }

        private void LimpiarUsuario()
        {
            GrillaUsuarios.SelectedItem = null;
            TituloUsuario.Text = "NUEVO USUARIO";
            TxtNombreUsuario.Text = string.Empty;
            CmbRol.SelectedItem = "Mesero";
            BtnGuardarUsuario.IsEnabled = false;
            BtnEliminarUsuario.IsEnabled = false;
        }

        private void Avisar(string texto) => TextoEstado.Text = $"{DateTime.Now:HH:mm} · {texto}";

        private bool Resultado(string resultado, string exito)
        {
            if (resultado == "OK")
            {
                Avisar(exito);
                return true;
            }
            MessageBox.Show(resultado, "Revisa", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private static bool Confirmar(string pregunta) =>
            MessageBox.Show(pregunta, "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        private static int LeerNumero(TextBox caja) => int.TryParse(caja.Text.Trim(), out int n) ? n : 0;

        private void BtnPlatos_Click(object sender, RoutedEventArgs e)
        {
            var admin = new VistaAdminPlatos(dueno) { Owner = this };
            admin.ShowDialog();
            if (!admin.HuboCambios) return;

            HuboCambiosEnPlatos = true;
            CargarPlatos();
            CargarPedidos();
            Avisar("Menú actualizado. Los cambios ya salen en el menú web.");
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            CargarPlatos();
            CargarPedidos();
            CargarUsuarios((GrillaUsuarios.SelectedItem as UsuarioModel)?.IdUsuario);
            Avisar("Datos actualizados.");
        }

        private void Filtro_SelectionChanged(object sender, SelectionChangedEventArgs e) => CargarPedidos();

        private void TxtFiltroMesa_TextChanged(object sender, TextChangedEventArgs e) => CargarPedidos();

        private void BtnNuevoPedido_Click(object sender, RoutedEventArgs e)
        {
            Pestanas.SelectedIndex = 0;
            cargando = true;
            GrillaPedidos.SelectedItem = null;
            cargando = false;
            MostrarPedido(null);

            creandoPedido = true;
            TituloPedido.Text = "NUEVO PEDIDO";
            TxtMesa.Text = MesaFiltro()?.ToString() ?? string.Empty;
            CmbEstado.SelectedItem = "Pendiente";
            CmbMetodoPago.SelectedItem = KdsAdministracionBLL.SinMetodo;
            CmbEstadoPago.SelectedItem = "Pendiente";
            TxtReferenciaPago.Text = string.Empty;
            TxtCantidad.Text = "1";
            TxtNota.Text = string.Empty;

            BotonesPedido.Visibility = Visibility.Collapsed;
            EtiquetaDetalle.Visibility = Visibility.Collapsed;
            CajaDetalle.Visibility = Visibility.Collapsed;
            BotonesProducto.Visibility = Visibility.Collapsed;
            BtnAgregarProducto.Visibility = Visibility.Collapsed;
            AyudaNuevoPedido.Visibility = Visibility.Visible;
            BtnCrearPedido.Visibility = Visibility.Visible;
            BtnCancelarNuevo.Visibility = Visibility.Visible;
            TxtMesa.Focus();
        }

        private void SalirDeNuevoPedido()
        {
            creandoPedido = false;
            BotonesPedido.Visibility = Visibility.Visible;
            EtiquetaDetalle.Visibility = Visibility.Visible;
            CajaDetalle.Visibility = Visibility.Visible;
            BotonesProducto.Visibility = Visibility.Visible;
            BtnAgregarProducto.Visibility = Visibility.Visible;
            AyudaNuevoPedido.Visibility = Visibility.Collapsed;
            BtnCrearPedido.Visibility = Visibility.Collapsed;
            BtnCancelarNuevo.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelarNuevo_Click(object sender, RoutedEventArgs e)
        {
            SalirDeNuevoPedido();
            MostrarPedido(null);
        }

        private void BtnCrearPedido_Click(object sender, RoutedEventArgs e)
        {
            var plato = CmbPlato.SelectedItem as PlatoModel;
            string r = bll.CrearPedido(
                LeerNumero(TxtMesa),
                CmbEstado.SelectedItem as string ?? string.Empty,
                CmbMetodoPago.SelectedItem as string,
                CmbEstadoPago.SelectedItem as string ?? "Pendiente",
                TxtReferenciaPago.Text.Trim(),
                plato,
                LeerNumero(TxtCantidad),
                TxtNota.Text.Trim(),
                out int idNuevo);

            if (!Resultado(r, $"Pedido #{idNuevo} creado. Ahora puedes agregarle más productos."))
                return;

            SalirDeNuevoPedido();
            pedidoActual = null;
            cargando = true;
            if (CmbFiltroEstado.SelectedItem is string filtro && filtro != TodosLosEstados && filtro != (CmbEstado.SelectedItem as string))
                CmbFiltroEstado.SelectedIndex = 0;
            if (CmbFiltroPago.SelectedItem is string filtroPago && filtroPago != TodosLosEstados && filtroPago != (CmbEstadoPago.SelectedItem as string))
                CmbFiltroPago.SelectedIndex = 0;
            if (MesaFiltro() is int mesaFiltro && mesaFiltro != LeerNumero(TxtMesa))
                TxtFiltroMesa.Text = string.Empty;
            cargando = false;
            CargarPedidos(seleccionar: idNuevo);
        }

        private void GrillaPedidos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cargando) return;
            MostrarPedido(GrillaPedidos.SelectedItem as PedidoResumen);
        }

        private void GrillaDetalle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cargando) return;
            MostrarProducto(GrillaDetalle.SelectedItem as DetallePedidoModel);
        }

        private void GrillaUsuarios_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GrillaUsuarios.SelectedItem is not UsuarioModel usuario) return;
            TituloUsuario.Text = $"USUARIO #{usuario.IdUsuario}";
            TxtNombreUsuario.Text = usuario.Nombre;
            CmbRol.SelectedItem = usuario.Rol;
            BtnGuardarUsuario.IsEnabled = true;
            BtnEliminarUsuario.IsEnabled = true;
        }

        private void SoloNumeros_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
            e.Handled = !e.Text.All(char.IsDigit);

        private void BtnGuardarPedido_Click(object sender, RoutedEventArgs e)
        {
            if (pedidoActual == null) return;
            int id = pedidoActual.IdPedido;
            string estado = CmbEstado.SelectedItem as string ?? string.Empty;
            string r = bll.GuardarPedido(pedidoActual, LeerNumero(TxtMesa), estado,
                CmbMetodoPago.SelectedItem as string, CmbEstadoPago.SelectedItem as string ?? "Pendiente", TxtReferenciaPago.Text.Trim());
            if (Resultado(r, $"Pedido #{id} guardado."))
            {
                pedidoActual = null;
                CargarPedidos(seleccionar: id);
            }
        }

        private void BtnEliminarPedido_Click(object sender, RoutedEventArgs e)
        {
            if (pedidoActual == null) return;
            int id = pedidoActual.IdPedido;
            if (!Confirmar($"¿Eliminar el pedido #{id} de la mesa {pedidoActual.NumeroMesa} con todos sus productos? Esto no se puede deshacer."))
                return;

            if (Resultado(bll.EliminarPedido(id), $"Pedido #{id} eliminado."))
            {
                pedidoActual = null;
                CargarPedidos();
            }
        }

        private void BtnAgregarProducto_Click(object sender, RoutedEventArgs e)
        {
            if (pedidoActual == null) return;
            int id = pedidoActual.IdPedido;
            var plato = CmbPlato.SelectedItem as PlatoModel;
            if (Resultado(bll.AgregarProducto(id, plato, LeerNumero(TxtCantidad), TxtNota.Text.Trim()), $"{plato?.Nombre} agregado al pedido #{id}."))
            {
                pedidoActual = null;
                CargarPedidos(seleccionar: id);
            }
        }

        private void BtnGuardarProducto_Click(object sender, RoutedEventArgs e)
        {
            if (pedidoActual == null) return;
            int id = pedidoActual.IdPedido;
            var detalle = GrillaDetalle.SelectedItem as DetallePedidoModel;
            if (Resultado(bll.GuardarProducto(detalle, LeerNumero(TxtCantidad), TxtNota.Text.Trim()), "Producto guardado."))
            {
                pedidoActual = null;
                CargarPedidos(seleccionar: id);
            }
        }

        private void BtnQuitarProducto_Click(object sender, RoutedEventArgs e)
        {
            if (pedidoActual == null || GrillaDetalle.SelectedItem is not DetallePedidoModel detalle) return;
            int id = pedidoActual.IdPedido;
            if (!Confirmar($"¿Quitar {detalle.Cantidad} {detalle.NombrePlato} del pedido #{id}?"))
                return;

            if (Resultado(bll.QuitarProducto(detalle), $"{detalle.NombrePlato} quitado del pedido #{id}."))
            {
                pedidoActual = null;
                CargarPedidos(seleccionar: id);
            }
        }

        private void BtnAgregarUsuario_Click(object sender, RoutedEventArgs e)
        {
            string nombre = TxtNombreUsuario.Text.Trim();
            if (Resultado(bll.CrearUsuario(nombre, CmbRol.SelectedItem as string), $"Usuario {nombre} creado."))
            {
                var nuevo = bll.ListarUsuarios().Where(u => u.Nombre == nombre).OrderByDescending(u => u.IdUsuario).FirstOrDefault();
                CargarUsuarios(nuevo?.IdUsuario);
            }
        }

        private void BtnGuardarUsuario_Click(object sender, RoutedEventArgs e)
        {
            var usuario = GrillaUsuarios.SelectedItem as UsuarioModel;
            if (Resultado(bll.EditarUsuario(usuario, TxtNombreUsuario.Text.Trim(), CmbRol.SelectedItem as string), "Usuario guardado."))
                CargarUsuarios(usuario?.IdUsuario);
        }

        private void BtnEliminarUsuario_Click(object sender, RoutedEventArgs e)
        {
            if (GrillaUsuarios.SelectedItem is not UsuarioModel usuario) return;
            if (!Confirmar($"¿Eliminar al usuario {usuario.Nombre} ({usuario.Rol})?"))
                return;

            if (Resultado(bll.EliminarUsuario(usuario, dueno), $"Usuario {usuario.Nombre} eliminado."))
                CargarUsuarios();
        }

        private void BtnLimpiarUsuario_Click(object sender, RoutedEventArgs e) => LimpiarUsuario();
    }
}
