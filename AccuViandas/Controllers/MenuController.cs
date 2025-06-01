using AccuViandas.Data;
using AccuViandas.Models; // Controllers/MenuController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc; // Para las funcionalidades de API (ApiController, ActionResult, etc.)
using Microsoft.EntityFrameworkCore; // Para trabajar con EF Core (Include, FirstOrDefaultAsync)
using System.Globalization;

namespace AccuViandas.Controllers
{
    [ApiController] // Indica que esta clase es un controlador de API
    [Route("api/[controller]")] // Define la ruta base para este controlador (ej: /api/Menu)
    [Authorize]
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
        [Authorize(Roles = "Admin")]
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

        /// <summary>
        /// Sube el contenido de un archivo de texto con menús y los guarda en la base de datos.
        /// Ejemplo de uso: POST /api/Menu/upload-text-file
        /// En Swagger, usa el campo "file" para subir tu archivo de texto.
        /// </summary>
        /// <param name="file">El archivo de texto a subir.</param>
        /// <returns>Un resumen de los menús procesados y guardados.</returns>
        [HttpPost("upload-text-file")] // Nuevo endpoint
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult> UploadMenuFromFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No se ha seleccionado ningún archivo o el archivo está vacío.");
            }

            // Validar que el archivo sea de texto
            if (file.ContentType != "text/plain")
            {
                // Esto es una validación básica, puedes hacerla más robusta si es necesario.
                return BadRequest("Solo se permiten archivos de texto plano (.txt).");
            }

            var processedMenus = new List<DailyMenu>();
            var errors = new List<string>();

            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                string fileContent = await reader.ReadToEndAsync();
                var parsedMenus = ParseMenuText(fileContent);

                foreach (var parsedMenu in parsedMenus)
                {
                    try
                    {
                        // Verificar si ya existe un menú para esta fecha
                        var existingMenu = await _context.DailyMenus
                                                         .FirstOrDefaultAsync(m => m.Date.Date == parsedMenu.Date.Date);

                        if (existingMenu != null)
                        {
                            errors.Add($"Error: Ya existe un menú para la fecha {parsedMenu.Date.ToShortDateString()}. Se omitió el menú de esa fecha.");
                            continue; // Saltar este menú y pasar al siguiente
                        }

                        _context.DailyMenus.Add(parsedMenu);
                        processedMenus.Add(parsedMenu);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Error al procesar el menú para la fecha tentativa {parsedMenu.Date.ToShortDateString()}: {ex.Message}");
                    }
                }

                await _context.SaveChangesAsync(); // Guarda todos los menús válidos de una vez
            }

            if (processedMenus.Any())
            {
                return Ok(new { Message = $"Se procesaron y guardaron {processedMenus.Count} menú(s) exitosamente.", ProcessedDates = processedMenus.Select(m => m.Date.ToShortDateString()), Errors = errors });
            }
            else if (errors.Any())
            {
                return BadRequest(new { Message = "No se pudieron guardar menús nuevos.", Errors = errors });
            }
            else
            {
                return BadRequest("El archivo no contenía menús válidos para procesar.");
            }
        }


        // --- Método auxiliar para parsear el contenido del archivo de texto ---
        private List<DailyMenu> ParseMenuText(string textContent)
        {
            var menus = new List<DailyMenu>();
            var lines = textContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            DailyMenu currentMenu = null;
            foreach (var line in lines)
            {
                // Intentar parsear la fecha (Ej: "Lunes 18/03")
                // Se espera un formato como "DíaDeLaSemana DD/MM"
                // Necesitamos determinar el año actual o el próximo año si la fecha ya pasó
                if (line.Contains("/"))
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length >= 2)
                    {
                        string datePart = parts[parts.Length - 1]; // Toma "18/03"
                        try
                        {
                            // Intentar parsear la fecha asumiendo el año actual
                            DateTime parsedDate;
                            // Formato esperado: "dd/MM"
                            // Se añade el año actual por defecto
                            if (DateTime.TryParseExact(datePart, "dd/MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                            {
                                int currentYear = DateTime.Today.Year;
                                // Asigna el año al parsedDate
                                parsedDate = new DateTime(currentYear, parsedDate.Month, parsedDate.Day);

                                // Si la fecha ya pasó en el año actual, asume el próximo año
                                if (parsedDate.Date < DateTime.Today.Date)
                                {
                                    parsedDate = parsedDate.AddYears(1);
                                }

                                currentMenu = new DailyMenu { Date = parsedDate };
                                menus.Add(currentMenu);
                            }
                            else
                            {
                                // Si no se puede parsear la fecha, ignorar la línea o registrar un error.
                                // Por simplicidad, se ignora y se asume que la línea no es una fecha de menú.
                            }
                        }
                        catch (FormatException)
                        {
                            // Ignorar líneas que no se puedan parsear como fecha
                        }
                    }
                }
                else if (currentMenu != null && !string.IsNullOrWhiteSpace(line))
                {
                    // Si ya estamos procesando un menú, parsear los ítems
                    string[] itemParts = line.Split(':', 2); // Divide solo en el primer ':'
                    if (itemParts.Length == 2)
                    {
                        string categoryStr = itemParts[0].Trim();
                        string name = itemParts[1].Trim();

                        // Convertir la categoría de string a enum
                        if (Enum.TryParse(categoryStr, true, out DailyMenuItem.MenuCategory category)) // 'true' para ignorar mayúsculas/minúsculas
                        {
                            currentMenu.Items.Add(new DailyMenuItem
                            {
                                Name = name,
                                Category = category
                            });
                        }
                        // Si la categoría no se puede parsear, se ignora el ítem por ahora.
                        // Podrías añadir un log aquí si quisieras.
                    }
                }
            }
            return menus;
        }
    }
}
