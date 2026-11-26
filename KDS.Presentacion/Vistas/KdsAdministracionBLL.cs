using System;
using System.Collections.Generic;
using System.Linq;

namespace KDS.PRE.Vistas
{
    public class KdsAdministracionBLL
    {
        public static readonly string[] Estados = { "Solicitado", "Pendiente", "Preparando", "Listo", "Entregado" };
        public static readonly string[] MetodosPago = { "Efectivo", "Tarjeta", "Nequi", "Daviplata", "Transferencia" };
        public static readonly string[] EstadosPago = { "Pendiente", "Pagado" };
        public const string SinMetodo = "(sin definir)";

        private readonly KdsPedidoBLL pedidoBll = new();

        public List<PedidoResumen> ListarPedidos(string? estado, DateTime? desde = null, int? mesa = null, string? estadoPago = null) =>
            KdsDatosService.ObtenerTodosLosPedidos(
                Estados.Contains(estado) ? estado : null,
                desde,
                mesa is >= 1 and <= 50 ? mesa : null,
                EstadosPago.Contains(estadoPago) ? estadoPago : null);

        public string GuardarPedido(PedidoResumen original, int mesa, string estado, string? metodoPago, string estadoPago, string referencia)
        {
            if (mesa is < 1 or > 50) return "La mesa debe estar entre 1 y 50.";
            if (!Estados.Contains(estado)) return "Elige un estado válido.";
            string? metodo = NormalizarMetodo(metodoPago);
            if (metodoPago != null && metodoPago != SinMetodo && metodo == null) return "Elige un método de pago válido.";
            if (!EstadosPago.Contains(estadoPago)) return "Elige si el pedido está pagado o pendiente.";
            if (referencia.Length > 60) return "La referencia de pago no puede pasar de 60 caracteres.";
            if (estadoPago == "Pagado" && metodo == null) return "Para marcarlo como pagado, elige cómo pagó el cliente.";

            try
            {
                if (mesa != original.NumeroMesa && !KdsDatosService.ActualizarMesaPedido(original.IdPedido, mesa))
                    return "No se pudo cambiar la mesa.";
                if ((metodo ?? string.Empty) != original.MetodoPago || estadoPago != original.EstadoPago || referencia != original.ReferenciaPago)
                {
                    if (!KdsDatosService.ActualizarPagoPedido(original.IdPedido, metodo, estadoPago, referencia))
                        return "No se pudo guardar el pago.";
                }
                if (estado != original.Estado && !pedidoBll.ActualizarEstado(original.IdPedido, estado))
                    return "No se pudo cambiar el estado.";
                return "OK";
            }
            catch (Exception ex) { return "Error BD: " + ex.Message; }
        }

        public string CrearPedido(int mesa, string estado, string? metodoPago, string estadoPago, string referencia,
                                  PlatoModel? plato, int cantidad, string nota, out int idNuevo)
        {
            idNuevo = 0;
            if (mesa is < 1 or > 50) return "Escribe la mesa (entre 1 y 50) del pedido nuevo.";
            if (!Estados.Contains(estado)) return "Elige el estado del pedido nuevo.";
            if (plato == null) return "Elige el primer plato del pedido nuevo (abajo, en «Plato»).";
            if (cantidad is < 1 or > 20) return "La cantidad debe estar entre 1 y 20.";
            if (nota.Length > 200) return "La nota no puede pasar de 200 caracteres.";
            string? metodo = NormalizarMetodo(metodoPago);
            if (!EstadosPago.Contains(estadoPago)) estadoPago = "Pendiente";
            if (estadoPago == "Pagado" && metodo == null) return "Para marcarlo como pagado, elige cómo pagó el cliente.";

            var pedido = new PedidoModel
            {
                NumeroMesa = mesa,
                Estado = estado,
                FechaHora = DateTime.Now,
                NotificadoVoz = estado is "Listo" or "Entregado",
                MetodoPago = metodo,
                EstadoPago = estadoPago,
                ReferenciaPago = referencia,
                Detalles =
                {
                    new DetallePedidoModel
                    {
                        IdPlato = plato.IdPlato,
                        NombrePlato = plato.Nombre,
                        PrecioUnitario = plato.Precio,
                        Cantidad = cantidad,
                        NotasEspeciales = nota
                    }
                }
            };

            string r = pedidoBll.RegistrarPedido(pedido);
            if (r == "OK") idNuevo = pedido.IdPedido;
            return r;
        }

        public static string? NormalizarMetodo(string? metodo) =>
            MetodosPago.FirstOrDefault(m => string.Equals(m, metodo?.Trim(), StringComparison.OrdinalIgnoreCase));

        public string EliminarPedido(int idPedido)
        {
            try { return KdsDatosService.BorrarPedidoCompleto(idPedido) ? "OK" : "Ese pedido ya no existe."; }
            catch (Exception ex) { return "Error BD: " + ex.Message; }
        }

        public string AgregarProducto(int idPedido, PlatoModel? plato, int cantidad, string nota)
        {
            if (plato == null) return "Elige el plato que quieres agregar.";
            if (cantidad is < 1 or > 20) return "La cantidad debe estar entre 1 y 20.";
            if (nota.Length > 200) return "La nota no puede pasar de 200 caracteres.";
            try { return KdsDatosService.AgregarDetalle(idPedido, plato.IdPlato, cantidad, nota) ? "OK" : "No se pudo agregar."; }
            catch (Exception ex) { return "Error BD: " + ex.Message; }
        }

        public string GuardarProducto(DetallePedidoModel? detalle, int cantidad, string nota)
        {
            if (detalle == null) return "Selecciona un producto del pedido.";
            if (cantidad is < 1 or > 20) return "La cantidad debe estar entre 1 y 20.";
            if (nota.Length > 200) return "La nota no puede pasar de 200 caracteres.";
            try { return KdsDatosService.ActualizarDetalle(detalle.IdDetalle, cantidad, nota) ? "OK" : "No se pudo guardar."; }
            catch (Exception ex) { return "Error BD: " + ex.Message; }
        }

        public string QuitarProducto(DetallePedidoModel? detalle)
        {
            if (detalle == null) return "Selecciona un producto del pedido.";
            try { return KdsDatosService.EliminarDetalle(detalle.IdDetalle) ? "OK" : "Ese producto ya no existe."; }
            catch (Exception ex) { return "Error BD: " + ex.Message; }
        }

        public List<UsuarioModel> ListarUsuarios() => KdsDatosService.ObtenerUsuarios();

        public string CrearUsuario(string nombre, string? rol)
        {
            string? error = ValidarUsuario(nombre, rol);
            if (error != null) return error;
            try { return KdsDatosService.InsertarUsuario(nombre, rol!) ? "OK" : "No se pudo crear el usuario."; }
            catch (Exception ex) { return "Error BD: " + ex.Message; }
        }

        public string EditarUsuario(UsuarioModel? usuario, string nombre, string? rol)
        {
            if (usuario == null) return "Selecciona un usuario de la lista.";
            string? error = ValidarUsuario(nombre, rol);
            if (error != null) return error;
            if (usuario.Rol == "Admin" && rol != "Admin" && ContarDuenos() <= 1)
                return "Debe quedar al menos un dueño (Admin).";
            try { return KdsDatosService.ActualizarUsuario(usuario.IdUsuario, nombre, rol!) ? "OK" : "No se pudo guardar."; }
            catch (Exception ex) { return "Error BD: " + ex.Message; }
        }

        public string EliminarUsuario(UsuarioModel? usuario, UsuarioModel duenoActual)
        {
            if (usuario == null) return "Selecciona un usuario de la lista.";
            if (usuario.IdUsuario == duenoActual.IdUsuario) return "No puedes eliminar el usuario con el que entraste.";
            if (usuario.Rol == "Admin" && ContarDuenos() <= 1) return "Debe quedar al menos un dueño (Admin).";
            try { return KdsDatosService.EliminarUsuario(usuario.IdUsuario) ? "OK" : "Ese usuario ya no existe."; }
            catch (Exception ex) { return "Error BD: " + ex.Message; }
        }

        private static int ContarDuenos() => KdsDatosService.ObtenerUsuarios().Count(u => u.Rol == "Admin");

        private static string? ValidarUsuario(string nombre, string? rol)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return "El nombre es obligatorio.";
            if (nombre.Trim().Length > 100) return "El nombre no puede pasar de 100 caracteres.";
            if (rol == null || !KdsDatosService.RolesUsuario.Contains(rol)) return "Elige el rol: Mesero, Cocinero o Admin.";
            return null;
        }
    }
}
