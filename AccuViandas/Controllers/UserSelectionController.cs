// Controllers/UserSelectionController.cs

using AccuViandas.Data;
using AccuViandas.Models; // Necesario para User, DailyMenu, UserMenuSelection, MenuCategory
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization; // NEW: Necesario para el atributo [Authorize]


namespace AccuViandas.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Ruta base: /api/UserSelection
    [Authorize] // NEW: Requiere que el usuario esté autenticado para cualquier endpoint en este controlador
    public class UserSelectionController : ControllerBase
    {
        private readonly MenuDbContext _context;

        public UserSelectionController(MenuDbContext context)
        {
            _context = context;
        }

        // --- DTOs (Data Transfer Objects) para las peticiones y respuestas ---

        public class CreateSelectionDto
        {
            public int UserId { get; set; }
            public int DailyMenuId { get; set; } // ID del DailyMenu (fecha)
            public string SelectedCategory { get; set; } // "Clasica", "Veggie", etc.

            // --- NEW: Propiedad para la Observación ---
            public string Observation { get; set; } // Puede ser null
                                                    // --- END NEW ---
        }

        public class UserSelectionDto
        {
            public int Id { get; set; }
            public int UserId { get; set; }
            public string Username { get; set; } // Para mostrar en el resumen
            public DateTime MenuDate { get; set; }
            public string SelectedCategory { get; set; }
            public DateTime SelectionDateTime { get; set; }
            public bool IsActive { get; set; }
            public string SelectedMenuItemName { get; set; } // Nombre del plato elegido
            public string Observation { get; set; } // Incluir la observación en la respuesta
        }

        // --- Endpoints para Selecciones ---

        /// <summary>
        /// Permite a un usuario hacer una selección de menú para un día específico.
        /// Siempre crea una nueva selección.
        /// </summary>
        /// <param name="dto">Datos de la selección: UserId, DailyMenuId, SelectedCategory, Observation.</param>
        /// <returns>La selección creada.</returns>
        [HttpPost]
        public async Task<ActionResult<UserSelectionDto>> SelectMenu([FromBody] CreateSelectionDto dto)
        {
            // 1. Validar la existencia del usuario
            var user = await _context.Users.FindAsync(dto.UserId);
            if (user == null)
            {
                return NotFound($"Usuario con ID {dto.UserId} no encontrado.");
            }

            // 2. Validar la existencia del DailyMenu (fecha)
            var dailyMenu = await _context.DailyMenus.Include(dm => dm.Items).FirstOrDefaultAsync(dm => dm.Id == dto.DailyMenuId);
            if (dailyMenu == null)
            {
                return NotFound($"Menú diario con ID {dto.DailyMenuId} no encontrado.");
            }

            // 3. Validar que la categoría sea válida y exista en el DailyMenu para ese día
            if (!Enum.TryParse(dto.SelectedCategory, true, out DailyMenuItem.MenuCategory selectedCategoryEnum))
            {
                return BadRequest($"Categoría '{dto.SelectedCategory}' no válida.");
            }

            // Verificar si el DailyMenu tiene un item de esa categoría
            if (!dailyMenu.Items.Any(item => item.Category == selectedCategoryEnum))
            {
                return BadRequest($"El menú del {dailyMenu.Date.ToShortDateString()} no ofrece la categoría '{dto.SelectedCategory}'.");
            }
            var selectedMenuItem = dailyMenu.Items.First(item => item.Category == selectedCategoryEnum);

            // 4.  Siempre crea una nueva selección (ya no buscamos una existente)
            var userSelection = new UserMenuSelection
            {
                UserId = dto.UserId,
                DailyMenuId = dto.DailyMenuId,
                SelectedCategory = selectedCategoryEnum,
                SelectionDateTime = DateTime.Now,
                IsActive = true, // Por defecto es activa
                Observation = dto.Observation // Guardar la observación
            };
            _context.UserMenuSelections.Add(userSelection);
            await _context.SaveChangesAsync();

            // Mapear a DTO de respuesta para incluir el nombre del ítem y la observación
            var responseDto = new UserSelectionDto
            {
                Id = userSelection.Id,
                UserId = userSelection.UserId,
                Username = user.Username,
                MenuDate = dailyMenu.Date,
                SelectedCategory = userSelection.SelectedCategory.ToString(),
                SelectionDateTime = userSelection.SelectionDateTime,
                IsActive = userSelection.IsActive,
                SelectedMenuItemName = selectedMenuItem.Name,
                Observation = userSelection.Observation // Incluir la observación
            };

            return CreatedAtAction(nameof(GetUserSelectionById), new { id = userSelection.Id }, responseDto);
        }

        /// <summary>
        /// Obtiene una selección de menú específica por su ID.
        /// </summary>
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")] // Solo Admin pueden consultar por id
        public async Task<ActionResult<UserSelectionDto>> GetUserSelectionById(int id)
        {
            var selection = await _context.UserMenuSelections
                                            .Include(ums => ums.User)
                                            .Include(ums => ums.DailyMenu)
                                                .ThenInclude(dm => dm.Items) // Incluir ítems del DailyMenu
                                            .FirstOrDefaultAsync(ums => ums.Id == id);

            if (selection == null)
            {
                return NotFound();
            }

            // Si solo los Admin pueden acceder a este endpoint, la validación interna
            // de user ID vs token ID ya no es estrictamente necesaria aquí si siempre
            // esperas que sea un Admin quien lo use. Sin embargo, no hace daño.
            // string userIdFromToken = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            // if (userIdFromToken == null || int.Parse(userIdFromToken) != selection.UserId)
            // {
            //     if (User.FindFirst(ClaimTypes.Role)?.Value != "Admin")
            //     {
            //         return Forbid("No tienes permiso para ver esta selección.");
            //     }
            // }

            // Mapear a DTO de respuesta
            var selectedMenuItem = selection.DailyMenu.Items.FirstOrDefault(item => item.Category == selection.SelectedCategory);

            var responseDto = new UserSelectionDto
            {
                Id = selection.Id,
                UserId = selection.UserId,
                Username = selection.User.Username,
                MenuDate = selection.DailyMenu.Date,
                SelectedCategory = selection.SelectedCategory.ToString(),
                SelectionDateTime = selection.SelectionDateTime,
                IsActive = selection.IsActive,
                SelectedMenuItemName = selectedMenuItem?.Name, // Puede ser null si el item no se encuentra
                Observation = selection.Observation // Incluir la observación
            };

            return Ok(responseDto);
        }

        /// <summary>
        /// Cancela (marca como inactiva) una selección de menú existente.
        /// Solo el usuario que hizo la selección puede cancelarla (lógica simple aquí).
        /// </summary>
        /// <param name="selectionId">ID de la selección a cancelar.</param>
        /// <param name="userId">ID del usuario que intenta cancelar (para validación).</param>
        /// <returns>Estado de la operación.</returns>
        [HttpPut("cancel/{selectionId}")] // Usa PUT para actualizar un recurso existente
        public async Task<ActionResult> CancelMenuSelection(int selectionId, [FromQuery] int userId)
        {
            var selection = await _context.UserMenuSelections.FindAsync(selectionId);

            if (selection == null)
            {
                return NotFound($"Selección con ID {selectionId} no encontrada.");
            }

            // Validar que el usuario que intenta cancelar es el propietario de la selección
            // En un sistema real, esto se haría a través de tokens de autenticación
            if (selection.UserId != userId)
            {
                return Forbid("No tienes permiso para cancelar esta selección."); // 403 Forbidden
            }

            if (!selection.IsActive)
            {
                return BadRequest("Esta selección ya está cancelada.");
            }

            selection.IsActive = false; // Marcar como inactiva
            await _context.SaveChangesAsync();

            return Ok("Selección cancelada exitosamente.");
        }

        // --- Endpoints para Resúmenes ---

        /// <summary>
        /// Obtiene un resumen semanal de menús seleccionados por un usuario específico.
        /// </summary>
        /// <param name="userId">ID del usuario.</param>
        /// <param name="startDate">Fecha de inicio de la semana (ej. 2025-05-26).</param>
        /// <returns>Lista de selecciones activas para el usuario en la semana.</returns>
        [HttpGet("summary/user/{userId}")]
        public async Task<ActionResult<IEnumerable<UserSelectionDto>>> GetUserWeeklySummary(int userId, [FromQuery] DateTime startDate)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound($"Usuario con ID {userId} no encontrado.");
            }

            // Asegurarse de que startDate sea el inicio de la semana (ej. Lunes)
            // Esto es una simplificación; la lógica real de "semana" depende de tu definición
            // Por ejemplo, para lunes, si la semana comienza el lunes:
            // while (startDate.DayOfWeek != DayOfWeek.Monday) startDate = startDate.AddDays(-1);

            var endDate = startDate.AddDays(6).Date.AddHours(23).AddMinutes(59).AddSeconds(59); // Fin del domingo

            var selections = await _context.UserMenuSelections
                                            .Where(ums => ums.UserId == userId &&
                                                          ums.IsActive &&
                                                          ums.DailyMenu.Date.Date >= startDate.Date &&
                                                          ums.DailyMenu.Date.Date <= endDate.Date)
                                            .Include(ums => ums.DailyMenu)
                                                .ThenInclude(dm => dm.Items)
                                            .OrderBy(ums => ums.DailyMenu.Date)
                                            .Select(ums => new UserSelectionDto
                                            {
                                                Id = ums.Id,
                                                UserId = ums.UserId,
                                                Username = ums.User.Username, // Necesitaría .Include(ums => ums.User) si no se pasa el userId.
                                                MenuDate = ums.DailyMenu.Date,
                                                SelectedCategory = ums.SelectedCategory.ToString(),
                                                SelectionDateTime = ums.SelectionDateTime,
                                                IsActive = ums.IsActive,
                                                SelectedMenuItemName = ums.DailyMenu.Items.FirstOrDefault(item => item.Category == ums.SelectedCategory).Name,
                                                Observation = ums.Observation // Incluir la observación
                                            })
                                            .ToListAsync();

            return Ok(selections);
        }

        /// <summary>
        /// Obtiene un resumen de la cantidad de cada tipo de menú seleccionado por día para una semana.
        /// </summary>
        /// <param name="startDate">Fecha de inicio de la semana (ej. 2025-05-26).</param>
        /// <returns>Un resumen de conteos por día y categoría.</returns>
        [HttpGet("summary/daily")]
        [Authorize(Roles = "Admin")] // Solo Admin pueden consultar
        public async Task<ActionResult> GetDailySummary([FromQuery] DateTime startDate)
        {
            var endDate = startDate.AddDays(6).Date.AddHours(23).AddMinutes(59).AddSeconds(59);

            var summary = await _context.UserMenuSelections
                                        .Where(ums => ums.IsActive &&
                                                      ums.DailyMenu.Date.Date >= startDate.Date &&
                                                      ums.DailyMenu.Date.Date <= endDate.Date)
                                        .GroupBy(ums => new { Date = ums.DailyMenu.Date.Date, Category = ums.SelectedCategory })
                                        .Select(g => new
                                        {
                                            Date = g.Key.Date,
                                            Category = g.Key.Category.ToString(),
                                            Count = g.Count()
                                        })
                                        .OrderBy(x => x.Date)
                                        .ThenBy(x => x.Category)
                                        .ToListAsync();

            // Formato de salida deseado: { "Date": "2025-05-27", "Clasica": 5, "Veggie": 3, ... }
            var formattedSummary = summary.GroupBy(x => x.Date)
                                        .Select(g =>
                                        {
                                            var obj = new Dictionary<string, object>
                                            {
                                            { "Date", g.Key.ToShortDateString() },
                                            { "Total", g.Sum(x => x.Count) } // Total para el día
                                            };
                                            foreach (var item in g)
                                            {
                                                obj[item.Category] = item.Count;
                                            }
                                            return obj;
                                        })
                                        .ToList();

            return Ok(formattedSummary);
        }

        /// <summary>
        /// Obtiene un resumen total semanal de cada tipo de menú seleccionado.
        /// </summary>
        /// <param name="startDate">Fecha de inicio de la semana (ej. 2025-05-26).</param>
        /// <returns>Un resumen de conteos totales por categoría para la semana.</returns>
        [HttpGet("summary/total")]
        [Authorize(Roles = "Admin")] // Solo Admin pueden consultar
        public async Task<ActionResult> GetTotalWeeklySummary([FromQuery] DateTime startDate)
        {
            var endDate = startDate.AddDays(6).Date.AddHours(23).AddMinutes(59).AddSeconds(59);

            var totalSummary = await _context.UserMenuSelections
                                            .Where(ums => ums.IsActive &&
                                                          ums.DailyMenu.Date.Date >= startDate.Date &&
                                                          ums.DailyMenu.Date.Date <= endDate.Date)
                                            .GroupBy(ums => ums.SelectedCategory)
                                            .Select(g => new
                                            {
                                                Category = g.Key.ToString(),
                                                TotalCount = g.Count()
                                            })
                                            .OrderBy(x => x.Category)
                                            .ToListAsync();

            return Ok(totalSummary);
        }
    }
}
