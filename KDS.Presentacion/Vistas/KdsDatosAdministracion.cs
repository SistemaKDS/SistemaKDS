using System;
using System.Collections.Generic;
using System.Linq;
using Npgsql;

namespace KDS.PRE.Vistas
{
    public class PedidoResumen
    {
        public int IdPedido { get; set; }
        public int NumeroMesa { get; set; }
        public DateTime FechaHora { get; set; }
        public string Estado { get; set; } = string.Empty;
        public List<DetallePedidoModel> Detalles { get; set; } = new();
        public decimal Total => Detalles.Sum(d => d.PrecioUnitario * d.Cantidad);
        public string Productos => Detalles.Count == 0
            ? "(sin productos)"
            : string.Join(", ", Detalles.Select(d => $"{d.Cantidad} {d.NombrePlato}"));
    }

    public static partial class KdsDatosService
    {
        public static readonly string[] RolesUsuario = { "Mesero", "Cocinero", "Admin" };

        public static List<PedidoResumen> ObtenerTodosLosPedidos(string? estado)
        {
            var pedidos = new Dictionary<int, PedidoResumen>();
            using var con = AbrirConexion();

            string sql = @"
                SELECT p.id_pedido, p.numero_mesa, p.fecha_hora, p.estado,
                       d.id_detalle, d.id_plato, pl.nombre AS nombre_plato, pl.precio, d.cantidad, d.notas_especiales
                FROM pedidos p
                LEFT JOIN detalle_pedidos d ON p.id_pedido = d.id_pedido
                LEFT JOIN platos pl ON d.id_plato = pl.id_plato
                WHERE (@estado IS NULL OR p.estado = @estado)
                ORDER BY p.id_pedido DESC, d.id_detalle ASC;";

            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.Add(new NpgsqlParameter("@estado", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)estado ?? DBNull.Value });
            using var dr = cmd.ExecuteReader();

            while (dr.Read())
            {
                int id = Convert.ToInt32(dr["id_pedido"]);
                if (!pedidos.TryGetValue(id, out var pedido))
                {
                    pedido = new PedidoResumen
                    {
                        IdPedido = id,
                        NumeroMesa = Convert.ToInt32(dr["numero_mesa"]),
                        FechaHora = Convert.ToDateTime(dr["fecha_hora"]),
                        Estado = dr["estado"].ToString() ?? string.Empty
                    };
                    pedidos[id] = pedido;
                }

                if (dr["id_detalle"] is DBNull) continue;

                pedido.Detalles.Add(new DetallePedidoModel
                {
                    IdDetalle = Convert.ToInt32(dr["id_detalle"]),
                    IdPedido = id,
                    IdPlato = Convert.ToInt32(dr["id_plato"]),
                    NombrePlato = dr["nombre_plato"].ToString() ?? string.Empty,
                    PrecioUnitario = dr["precio"] is DBNull ? 0 : Convert.ToDecimal(dr["precio"]),
                    Cantidad = Convert.ToInt32(dr["cantidad"]),
                    NotasEspeciales = dr["notas_especiales"] is DBNull ? string.Empty : dr["notas_especiales"].ToString() ?? string.Empty
                });
            }

            return pedidos.Values.ToList();
        }

        public static bool ActualizarMesaPedido(int idPedido, int mesa)
        {
            using var con = AbrirConexion();
            using var cmd = new NpgsqlCommand("UPDATE pedidos SET numero_mesa = @mesa WHERE id_pedido = @id;", con);
            cmd.Parameters.AddWithValue("@mesa", mesa);
            cmd.Parameters.AddWithValue("@id", idPedido);
            return cmd.ExecuteNonQuery() > 0;
        }

        public static bool BorrarPedidoCompleto(int idPedido)
        {
            using var con = AbrirConexion();
            using var tx = con.BeginTransaction();

            using (var cmdDet = new NpgsqlCommand("DELETE FROM detalle_pedidos WHERE id_pedido = @id;", con, tx))
            {
                cmdDet.Parameters.AddWithValue("@id", idPedido);
                cmdDet.ExecuteNonQuery();
            }

            int borrados;
            using (var cmd = new NpgsqlCommand("DELETE FROM pedidos WHERE id_pedido = @id;", con, tx))
            {
                cmd.Parameters.AddWithValue("@id", idPedido);
                borrados = cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return borrados > 0;
        }

        public static bool AgregarDetalle(int idPedido, int idPlato, int cantidad, string nota)
        {
            using var con = AbrirConexion();
            const string sql = @"INSERT INTO detalle_pedidos (id_pedido, id_plato, cantidad, notas_especiales)
                                 VALUES (@pedido, @plato, @cantidad, @nota);";
            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@pedido", idPedido);
            cmd.Parameters.AddWithValue("@plato", idPlato);
            cmd.Parameters.AddWithValue("@cantidad", cantidad);
            cmd.Parameters.AddWithValue("@nota", string.IsNullOrWhiteSpace(nota) ? DBNull.Value : nota.Trim());
            return cmd.ExecuteNonQuery() > 0;
        }

        public static bool ActualizarDetalle(int idDetalle, int cantidad, string nota)
        {
            using var con = AbrirConexion();
            const string sql = "UPDATE detalle_pedidos SET cantidad = @cantidad, notas_especiales = @nota WHERE id_detalle = @id;";
            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@cantidad", cantidad);
            cmd.Parameters.AddWithValue("@nota", string.IsNullOrWhiteSpace(nota) ? DBNull.Value : nota.Trim());
            cmd.Parameters.AddWithValue("@id", idDetalle);
            return cmd.ExecuteNonQuery() > 0;
        }

        public static bool EliminarDetalle(int idDetalle)
        {
            using var con = AbrirConexion();
            using var cmd = new NpgsqlCommand("DELETE FROM detalle_pedidos WHERE id_detalle = @id;", con);
            cmd.Parameters.AddWithValue("@id", idDetalle);
            return cmd.ExecuteNonQuery() > 0;
        }

        public static List<UsuarioModel> ObtenerUsuarios()
        {
            var lista = new List<UsuarioModel>();
            using var con = AbrirConexion();
            using var cmd = new NpgsqlCommand("SELECT id_usuario, nombre, rol, clave_hash FROM usuarios ORDER BY id_usuario ASC;", con);
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                lista.Add(new UsuarioModel
                {
                    IdUsuario = Convert.ToInt32(dr["id_usuario"]),
                    Nombre = dr["nombre"].ToString() ?? string.Empty,
                    Rol = dr["rol"].ToString() ?? string.Empty,
                    ClaveHash = dr["clave_hash"] is DBNull ? null : dr["clave_hash"].ToString()
                });
            }
            return lista;
        }

        public static bool InsertarUsuario(string nombre, string rol)
        {
            using var con = AbrirConexion();
            using var cmd = new NpgsqlCommand("INSERT INTO usuarios (nombre, rol) VALUES (@nombre, @rol);", con);
            cmd.Parameters.AddWithValue("@nombre", nombre.Trim());
            cmd.Parameters.AddWithValue("@rol", rol);
            return cmd.ExecuteNonQuery() > 0;
        }

        public static bool ActualizarUsuario(int idUsuario, string nombre, string rol)
        {
            using var con = AbrirConexion();
            using var cmd = new NpgsqlCommand("UPDATE usuarios SET nombre = @nombre, rol = @rol WHERE id_usuario = @id;", con);
            cmd.Parameters.AddWithValue("@nombre", nombre.Trim());
            cmd.Parameters.AddWithValue("@rol", rol);
            cmd.Parameters.AddWithValue("@id", idUsuario);
            return cmd.ExecuteNonQuery() > 0;
        }

        public static bool EliminarUsuario(int idUsuario)
        {
            using var con = AbrirConexion();
            using var cmd = new NpgsqlCommand("DELETE FROM usuarios WHERE id_usuario = @id;", con);
            cmd.Parameters.AddWithValue("@id", idUsuario);
            return cmd.ExecuteNonQuery() > 0;
        }
    }
}
