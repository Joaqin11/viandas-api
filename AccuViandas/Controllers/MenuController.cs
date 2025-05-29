using AccuViandas.Data;
using AccuViandas.Models;
// Controllers/MenuController.cs
using Microsoft.AspNetCore.Mvc; // Para las funcionalidades de API (ApiController, ActionResult, etc.)
using Microsoft.EntityFrameworkCore; // Para trabajar con EF Core (Include, FirstOrDefaultAsync)
using System; // Para DateTime
using System.Collections.Generic; // Para List
using System.Linq; // Para LINQ (Language Integrated Query)

namespace AccuViandas.Controllers
{
    

    [ApiController] // Indica que esta clase es un controlador de API
    [Route("api/[controller]")] // Define la ruta base para este controlador (ej: /api/Menu)
    public class MenuController : ControllerBase // Hereda de ControllerBase para funcionalidades básicas de API
    {
        private readonly MenuDbContext _context; // Campo privado para almacenar el contexto de la base de datos

        // Constructor: Aquí es donde ASP.NET Core inyecta (proporciona) una instancia de MenuDbContext
        // gracias a la configuración que hicimos en Program.cs
        public MenuController(MenuDbContext context)
        {
            _context = context; // Asigna la instancia del contexto al campo privado
        }

        /// <summary>
        /// Obtiene el menú para una fecha específica.
        /// Ejemplo de uso: GET /api/Menu/2025-05-27
        /// </summary>
        /// <param name="date">La fecha del menú en formato YYYY-MM-DD.</param>
        /// <returns>El menú diario para la fecha especificada, o un 404 si no se encuentra.</returns>
        [HttpGet("{date}")] // Define un endpoint GET que acepta un parámetro 'date' en la URL
        public async Task<ActionResult<DailyMenu>> GetMenuByDate(DateTime date)
        {
            // Busca un DailyMenu en la base de datos por la fecha (solo la parte de la fecha)
            // .Include(m => m.Items) carga también los DailyMenuItems asociados a ese DailyMenu
            var menu = await _context.DailyMenus
                                     .Include(m => m.Items)
                                     .FirstOrDefaultAsync(m => m.Date.Date == date.Date);

            // Si no se encuentra ningún menú para esa fecha, devuelve un error 404 Not Found
            if (menu == null)
            {
                return NotFound($"No se encontró menú para la fecha {date.ToShortDateString()}.");
            }

            // Si se encuentra, devuelve el menú con un estado 200 OK
            return Ok(menu);
        }

        /// <summary>
        /// Crea un nuevo menú diario con sus ítems.
        /// Ejemplo de uso: POST /api/Menu
        /// Cuerpo de la petición (JSON):
        /// {
        ///   "date": "2025-05-27",
        ///   "items": [
        ///     { "name": "Milanesas de pollo gratinadas...", "category": "Classic" },
        ///     { "name": "Omelette de espinaca...", "category": "Veggie" },
        ///     { "name": "Tarta de salame milán...", "category": "Express" },
        ///     { "name": "Calabaza rellena...", "category": "Especial" }
        ///   ]
        /// }
        /// </summary>
        /// <param name="dailyMenu">El objeto DailyMenu a crear.</param>
        /// <returns>El menú creado con su ID.</returns>
        [HttpPost] // Define un endpoint POST
        public async Task<ActionResult<DailyMenu>> CreateMenu(DailyMenu dailyMenu)
        {
            // Validación básica: Asegurarse de que la fecha no esté vacía
            if (dailyMenu.Date == DateTime.MinValue)
            {
                return BadRequest("La fecha del menú es obligatoria.");
            }

            // Opcional: Puedes verificar si ya existe un menú para esa fecha antes de agregarlo
            var existingMenu = await _context.DailyMenus
                                             .FirstOrDefaultAsync(m => m.Date.Date == dailyMenu.Date.Date);

            if (existingMenu != null)
            {
                return Conflict($"Ya existe un menú para la fecha {dailyMenu.Date.ToShortDateString()}.");
            }

            // Agrega el nuevo menú y sus ítems a la base de datos
            _context.DailyMenus.Add(dailyMenu);
            await _context.SaveChangesAsync(); // Guarda los cambios en la base de datos

            // Devuelve el menú creado con su ID y la URL para acceder a él (estado 201 Created)
            return CreatedAtAction(nameof(GetMenuByDate), new { date = dailyMenu.Date.Date }, dailyMenu);
        }
    }
}
