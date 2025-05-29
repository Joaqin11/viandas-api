using System.Text.Json.Serialization;

namespace AccuViandas.Models
{
    public class DailyMenuItem
    {
        public int Id { get; set; }
        public string Name { get; set; } // Nombre del plato, por ejemplo: "Milanesas de pollo gratinadas con cuñas de papa, batata y zanahoria."
                                         // Ya no necesitamos la propiedad 'Price' aquí.
        public MenuCategory Category { get; set; } // Classic, Express, Veggie, Especial

        // Clave foránea y propiedad de navegación para relacionar con DailyMenu
        public int DailyMenuId { get; set; }
        
        // Agrega [JsonIgnore] a la propiedad de navegación
        [JsonIgnore]
        public DailyMenu? DailyMenu { get; set; }

        public enum MenuCategory
        {
            Clásica,
            Express,
            Veggie,
            Especial
        }
    }
}
