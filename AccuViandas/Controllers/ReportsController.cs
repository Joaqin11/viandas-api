// Controllers/ReportsController.cs
using AccuViandas.Data;
using ClosedXML.Excel; // Necesario para ClosedXML
using ClosedXML.Excel.Drawings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization; // Para CultureInfo (días en español)
using System.Linq;

namespace AccuViandas.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")] // Solo administradores pueden acceder a este controlador
    public class ReportsController : ControllerBase
    {
        private readonly MenuDbContext _context;

        public ReportsController(MenuDbContext context)
        {
            _context = context;
        }

        [HttpGet("weekly-menu-summary-excel")]
        public async Task<IActionResult> DownloadWeeklyMenuSummaryExcel(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            // Si no se proporcionan fechas, usamos la semana actual (Lunes a Domingo)
            if (!startDate.HasValue || !endDate.HasValue)
            {
                var today = DateTime.Today;
                // Obtener el inicio de la semana actual (Lunes)
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                startDate = today.AddDays(-1 * diff).Date;
                endDate = startDate.Value.AddDays(6).Date;
            }

            if (startDate.Value > endDate.Value)
            {
                return BadRequest("La fecha de inicio no puede ser posterior a la fecha de fin.");
            }

            // Obtener todas las selecciones de menú para el rango de fechas,
            // incluyendo información del usuario y del menú diario
            var menuSelections = await _context.UserMenuSelections
                .Where(ums => ums.IsActive &&
                              ums.DailyMenu.Date.Date >= startDate.Value.Date &&
                              ums.DailyMenu.Date.Date <= endDate.Value.Date)
                .Include(ums => ums.User)
                .Include(ums => ums.DailyMenu)
                    .ThenInclude(dm => dm.Items)
                .OrderBy(ums => ums.User.Username) // Ordenar por usuario para facilitar el agrupamiento
                .ThenBy(ums => ums.DailyMenu.Date)   // Luego por fecha
                .ToListAsync();

            if (!menuSelections.Any())
            {
                return NotFound("No se encontraron selecciones de menú para el rango de fechas especificado.");
            }

            // --- Transformar los datos al formato de tabla pivote deseado ---
            var reportRows = menuSelections
                .GroupBy(s => new { s.User.Id, s.User.Username, s.User.EmailAddress }) // Agrupar por usuario
                .Select(userGroup =>
                {
                    var dailySelections = new Dictionary<DayOfWeek, List<string>>();
                    var allObservations = new List<string>();

                    foreach (var selection in userGroup)
                    {
                        // 1. Recopilar selecciones diarias concatenadas
                        var day = selection.DailyMenu.Date.DayOfWeek;
                        if (!dailySelections.ContainsKey(day))
                        {
                            dailySelections[day] = new List<string>();
                        }
                        var selectedMenuItem = selection.DailyMenu.Items.FirstOrDefault(item => item.Category == selection.SelectedCategory);
                        if (selectedMenuItem != null)
                        {
                            // Formato: CATEGORÍA + PLATO
                            dailySelections[day].Add($"{selection.SelectedCategory.ToString().ToUpper()}"); // + {selectedMenuItem.Name.ToUpper()}");
                        }

                        // 2. Recopilar todas las observaciones
                        if (!string.IsNullOrEmpty(selection.Observation))
                        {
                            allObservations.Add(selection.Observation);
                        }
                    }

                    return new // Objeto anónimo para la fila del reporte
                    {
                        UserName = userGroup.Key.Username,
                        DailySelections = dailySelections,
                        AllObservations = string.Join(" | ", allObservations.Distinct()) // Unir observaciones únicas con " | "
                    };
                })
                .OrderBy(r => r.UserName) // Ordenar las filas por nombre de usuario
                .ToList();

            // --- Generar el archivo Excel con ClosedXML ---
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Resumen de Pedidos");

                // Fila 1 (vacía según la imagen)
                // No hacemos nada, por defecto estará vacía.

                // Fila 2: Encabezados según la imagen
                worksheet.Cell(2, 1).Value = "USUARIOS"; // Columna A

                var reportDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
                var esCulture = new CultureInfo("es-ES"); // Para obtener los nombres de los días en español

                int col = 2; // Empezamos desde la columna B
                foreach (var day in reportDays)
                {
                    worksheet.Cell(2, col++).Value = esCulture.DateTimeFormat.GetDayName(day).ToUpper(); // CORRECCIÓN
                    //worksheet.Cell(2, col++).Value = day.ToString("dddd", esCulture).ToUpper(); // Nombres de los días en español, mayúsculas
                }
                worksheet.Cell(2, col).Value = "OBSERVACIONES"; // Última columna (G)

                // Estilo de encabezados (fila 2) según la imagen
                var headerRange = worksheet.Range("A2", $"G2"); // Asumiendo que 'G' es la última columna
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F4F4F"); // Gris oscuro
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;//XLHorizontalAlignmentValues.Center; // Centrar el texto

                // Llenar datos (desde la fila 3)
                int currentRow = 3;
                foreach (var rowData in reportRows)
                {
                    worksheet.Cell(currentRow, 1).Value = rowData.UserName; // Columna A: Usuario

                    col = 2; // Reiniciar columna para los días
                    foreach (var day in reportDays)
                    {
                        if (rowData.DailySelections.TryGetValue(day, out var selectionsForDay))
                        {
                            // Unir todas las selecciones de un día con " + "
                            worksheet.Cell(currentRow, col).Value = string.Join(" + ", selectionsForDay);
                        }
                        else
                        {
                            worksheet.Cell(currentRow, col).Value = ""; // Celda vacía si no hay selecciones para ese día
                        }
                        col++;
                    }
                    worksheet.Cell(currentRow, col).Value = rowData.AllObservations; // Columna G: Observaciones
                    currentRow++;
                }

                // Aplicar bordes a toda la tabla (desde A2 hasta la última celda de datos)
                var dataRange = worksheet.Range("A2", $"G{currentRow - 1}"); // Desde A2 hasta la última fila llena
                dataRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin); // Borde exterior
                dataRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);   // Bordes interiores

                // Autoajustar columnas para que el contenido sea visible
                worksheet.Columns().AdjustToContents();

                // Devolver el archivo
                var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0; // Resetear la posición del stream

                var fileName = $"Resumen_Pedidos_{startDate.Value.ToShortDateString()}_a_{endDate.Value.ToShortDateString()}.xlsx";
                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
        }

        [HttpGet("total-menu-quantities-excel")]
        [Authorize(Roles = "Admin")] // Solo administradores pueden acceder a este endpoint
        public async Task<IActionResult> DownloadTotalMenuQuantitiesExcel(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
        {
            // Si no se proporcionan fechas, usamos la semana actual (Lunes a Domingo)
            if (!startDate.HasValue || !endDate.HasValue)
            {
                var today = DateTime.Today;
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                startDate = today.AddDays(-1 * diff).Date;
                endDate = startDate.Value.AddDays(6).Date;
            }

            if (startDate.Value > endDate.Value)
            {
                return BadRequest("La fecha de inicio no puede ser posterior a la fecha de fin.");
            }

            // --- Definir categorías de menú y el orden en que aparecerán en el Excel ---
            var menuCategories = new List<string> { "Clásica", "Vegetariana", "Express", "Especial" }; // Asegúrate que estos coincidan con tus categorías reales

            // Obtener las selecciones de menú activas para el rango de fechas
            var menuSelections = await _context.UserMenuSelections
                .Where(ums => ums.IsActive &&
                              ums.DailyMenu.Date.Date >= startDate.Value.Date &&
                              ums.DailyMenu.Date.Date <= endDate.Value.Date)
                .Select(ums => new
                {
                    Date = ums.DailyMenu.Date.Date,
                    Category = ums.SelectedCategory
                })
                .ToListAsync();

            if (!menuSelections.Any())
            {
                return NotFound("No se encontraron selecciones de menú para el rango de fechas especificado.");
            }

            // --- Agrupar y contar las selecciones por día y categoría ---
            var dailyCounts = menuSelections
                .GroupBy(s => new { s.Date, s.Category })
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.Category,
                    Count = g.Count()
                })
                .ToList();

            // --- Preparar los datos para el Excel ---
            // Queremos una estructura que permita fácil acceso a Counts[Day][Category]
            var countsByDayAndCategory = new Dictionary<DayOfWeek, Dictionary<string, int>>();

            foreach (var date in Enumerable.Range(0, (endDate.Value - startDate.Value).Days + 1).Select(d => startDate.Value.AddDays(d).Date))
            {
                var dayOfWeek = date.DayOfWeek;
                if (!countsByDayAndCategory.ContainsKey(dayOfWeek))
                {
                    countsByDayAndCategory[dayOfWeek] = new Dictionary<string, int>();
                    foreach (var cat in menuCategories)
                    {
                        countsByDayAndCategory[dayOfWeek][cat] = 0; // Inicializar en 0
                    }
                }
            }

            foreach (var item in dailyCounts)
            {
                if (countsByDayAndCategory.ContainsKey(item.Date.DayOfWeek) && menuCategories.Contains(item.Category.ToString()))
                {
                    countsByDayAndCategory[item.Date.DayOfWeek][item.Category.ToString()] = item.Count;
                }
            }

            // --- Generar el archivo Excel con ClosedXML ---
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Total de Pedidos");

                // Fila 1 (vacía en la imagen)
                // No hacemos nada, por defecto estará vacía.

                // Fila 2: Encabezados principales (LUNES, MARTES, etc.)
                var reportDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
                var esCulture = new CultureInfo("es-ES");

                worksheet.Cell(2, 1).Value = "Clientes"; // Celda A2

                int currentColumn = 2; // Columna B
                foreach (var day in reportDays)
                {
                    // Cada día ocupa N columnas (CL, VE, EX, ES)
                    worksheet.Cell(2, currentColumn).Value = esCulture.DateTimeFormat.GetDayName(day).ToUpper();
                    worksheet.Range(2, currentColumn, 2, currentColumn + menuCategories.Count - 1).Merge(); // Combinar celdas para el día

                    // Estilo de encabezados de día
                    var dayHeaderRange = worksheet.Range(2, currentColumn, 2, currentColumn + menuCategories.Count - 1);
                    dayHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    dayHeaderRange.Style.Font.Bold = true;
                    dayHeaderRange.Style.Font.FontColor = XLColor.White;
                    dayHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FF7F00"); // Naranja oscuro (similar a la imagen)
                    dayHeaderRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

                    currentColumn += menuCategories.Count; // Avanzar las columnas por la cantidad de categorías
                }
                // Ajustar el ancho de la columna A para "Clientes"
                worksheet.Column(1).Width = 15;


                // Fila 3: Sub-encabezados (CL, VE, EX, ES)
                currentColumn = 2; // Empezamos de nuevo desde la columna B
                foreach (var day in reportDays)
                {
                    foreach (var category in menuCategories)
                    {
                        worksheet.Cell(3, currentColumn++).Value = category.Substring(0, 2).ToUpper(); // CL, VE, EX, ES
                    }
                }
                // Estilo de sub-encabezados (fila 3)
                var subHeaderRange = worksheet.Range(3, 1, 3, currentColumn - 1); // Desde A3 hasta la última columna de sub-encabezado
                subHeaderRange.Style.Font.Bold = true;
                subHeaderRange.Style.Font.FontColor = XLColor.White;
                subHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F4F4F"); // Gris oscuro
                subHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                subHeaderRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                subHeaderRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);


                // --- Llenar los datos de totales por día y categoría ---
                // Fila para "Totales" (o "Clientes" si necesitas algo ahí, aunque la imagen muestra solo "Totales")
                int dataRow = 4; // Fila donde comenzarán los datos (debajo de los encabezados)
                worksheet.Cell(dataRow, 1).Value = "Totales"; // Celda A4 para "Totales"

                currentColumn = 2; // Empezar desde la columna B
                var totalCategoriesByDay = new Dictionary<DayOfWeek, Dictionary<string, int>>();

                foreach (var day in reportDays)
                {
                    foreach (var category in menuCategories)
                    {
                        int count = countsByDayAndCategory.ContainsKey(day) && countsByDayAndCategory[day].ContainsKey(category)
                                    ? countsByDayAndCategory[day][category]
                                    : 0; // Obtener el conteo o 0 si no hay
                        worksheet.Cell(dataRow, currentColumn++).Value = count;

                        // Sumar para los totales generales
                        if (!totalCategoriesByDay.ContainsKey(day))
                        {
                            totalCategoriesByDay[day] = new Dictionary<string, int>();
                        }
                        if (!totalCategoriesByDay[day].ContainsKey(category))
                        {
                            totalCategoriesByDay[day][category] = 0;
                        }
                        totalCategoriesByDay[day][category] += count;
                    }
                }

                // Estilo para la fila de Totales (fila 4)
                var totalsRowRange = worksheet.Range(dataRow, 1, dataRow, currentColumn - 1);
                totalsRowRange.Style.Font.Bold = true;
                totalsRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFF00"); // Amarillo
                totalsRowRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                totalsRowRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                totalsRowRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; // Centrar valores numéricos


                // --- Aplicar bordes al área de datos (si hubiera más filas de clientes) ---
                // En este formato específico, solo hay una fila de "Totales" en tu imagen,
                // así que los bordes ya se aplicaron en la fila 4.
                // Si en el futuro agregaras filas por cliente, esto se ajustaría aquí.


                // Autoajustar columnas, excepto la primera que tiene ancho fijo
                worksheet.Columns().AdjustToContents();
                worksheet.Column(1).Width = 15; // Mantener ancho fijo para "Clientes"


                // Devolver el archivo
                var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileName = $"Cantidades_Totales_Menus_{startDate.Value.ToShortDateString()}_a_{endDate.Value.ToShortDateString()}.xlsx";
                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }

        }
    }
}