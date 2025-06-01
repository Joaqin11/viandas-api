// Models/UserMenuSelection.cs
using System;

namespace AccuViandas.Models
{

    public class UserMenuSelection
    {
        public int Id { get; set; } // Clave primaria para la selección

        // --- Relación con el Usuario ---
        public int UserId { get; set; }
        public User User { get; set; } // Propiedad de navegación al Usuario

        // --- Relación con el Menú Diario (para la fecha) ---
        // Aunque tenemos la fecha de DailyMenu, vinculamos al DailyMenuId
        // para asegurar la integridad referencial y facilidad de consultas.
        public int DailyMenuId { get; set; }
        public DailyMenu DailyMenu { get; set; } // Propiedad de navegación al DailyMenu

        // --- Detalles de la Selección ---
        public DailyMenuItem.MenuCategory SelectedCategory { get; set; } // La categoría elegida (Clásica, Veggie, etc.)
        public DateTime SelectionDateTime { get; set; } // Fecha y hora en que se hizo la selección

        // --- Estado de la Selección (para "borrar" o "cancelar") ---
        // 'true' si la selección está activa, 'false' si está cancelada/borrada
        public bool IsActive { get; set; } = true;

        // --- NEW: Columna para observaciones ---
        public string Observation { get; set; } // Puede ser null
        // --- END NEW ---
    }
}
