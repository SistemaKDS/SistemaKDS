using System;
using System.Collections.Generic;
using System.Linq;

namespace KDS.PRE.Portal
{
    public record MetodoPago(string Id, string Nombre, string Icono, string Instrucciones, bool PideReferencia);

    public static class MetodosPago
    {
        public static List<MetodoPago> Disponibles()
        {
            var lista = new List<MetodoPago>
            {
                new("Efectivo", "Efectivo", "💵", "Pagas en efectivo al mesero cuando te entregue el pedido.", false),
                new("Tarjeta", "Tarjeta (datáfono)", "💳", "El mesero te lleva el datáfono a la mesa. Débito o crédito.", false)
            };

            string? nequi = Leer("KDS_PAGO_NEQUI");
            if (nequi != null)
                lista.Add(new("Nequi", "Nequi", "📱", $"Envía el total al Nequi {nequi} y escribe abajo el número de la transacción.", true));

            string? daviplata = Leer("KDS_PAGO_DAVIPLATA");
            if (daviplata != null)
                lista.Add(new("Daviplata", "Daviplata", "📱", $"Envía el total al Daviplata {daviplata} y escribe abajo el número de la transacción.", true));

            string? cuenta = Leer("KDS_PAGO_TRANSFERENCIA");
            if (cuenta != null)
                lista.Add(new("Transferencia", "Transferencia / Bre-B", "🏦", $"Transfiere el total a: {cuenta}. Escribe abajo el número del comprobante.", true));

            return lista;
        }

        public static MetodoPago? Buscar(string? id) =>
            Disponibles().FirstOrDefault(m => string.Equals(m.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static string Describir() =>
            string.Join("; ", Disponibles().Select(m => $"{m.Id} ({m.Instrucciones})"));

        private static string? Leer(string variable)
        {
            string? valor = Environment.GetEnvironmentVariable(variable)?.Trim();
            return string.IsNullOrEmpty(valor) ? null : valor;
        }
    }
}
