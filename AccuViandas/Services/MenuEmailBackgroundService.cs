// Services/MenuEmailBackgroundService.cs
using AccuViandas.Data;
using AccuViandas.Models; // Para DailyMenu, UserMenuSelection, User
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic; // Para List
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AccuViandas.Services
{
    public class MenuEmailBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration; // NEW: Para leer configuración de la semana

        // NEW: Variable para rastrear la semana para la que se envió el último recordatorio
        // En un entorno de producción real, esto debería persistirse en una base de datos.
        // Para esta implementación, se reseteará al reiniciar la aplicación.
        private DateTime? _lastReminderSentForNextWeekStart = null;
        private DateTime? _lastSummarySentForNextWeekStart = null; // MODIFICADO: Ahora rastrea la próxima semana


        public MenuEmailBackgroundService(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Leer el intervalo de ejecución (ej. cada 24 horas, o 1 hora)
            // Se podría configurar en appsettings.json: "EmailTasks:IntervalHours": 24
            //var intervalHours = _configuration.GetValue<int>("EmailTasks:IntervalHours", 24); // Por defecto 24 horas
            //var interval = TimeSpan.FromHours(intervalHours);
            var pollingIntervalMinutes = _configuration.GetValue<int>("EmailTasks:PollingIntervalMinutes", 60); // Por defecto 60 minutos
            var interval = TimeSpan.FromMinutes(pollingIntervalMinutes); // Usar FromMinutes


            // --- NEW: Leer el día y la hora de inicio de la semana para los cálculos ---
            // Leer todas las configuraciones de tiempo y día
            var startDayOfWeekConfig = _configuration.GetValue<DayOfWeek>("EmailTasks:StartDayOfWeek", DayOfWeek.Monday);
            var reminderDayOfWeek = _configuration.GetValue<DayOfWeek>("EmailTasks:ReminderDayOfWeek", DayOfWeek.Monday);
            var reminderTime = _configuration.GetValue<TimeSpan>("EmailTasks:ReminderTime", new TimeSpan(9, 0, 0));
            var summaryDayOfWeek = _configuration.GetValue<DayOfWeek>("EmailTasks:SummaryDayOfWeek", DayOfWeek.Friday);
            var summaryTime = _configuration.GetValue<TimeSpan>("EmailTasks:SummaryTime", new TimeSpan(17, 0, 0));
            // --- END NEW ---

            // Variables para rastrear cuándo se envió el último recordatorio/resumen
            // Estas deberían ser persistentes en un sistema real (DB, Redis, etc.)
            // Para simplificar, las guardamos en memoria, pero se resetearán al reiniciar la app.
            //DateTime? lastReminderSent = null;
            //DateTime? lastSummarySent = null;

            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var dbContext = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<MenuEmailBackgroundService>>(); // NEW: Para logging

                    try
                    {
                        var now = DateTime.Now;

                        // --- Lógica para Recordatorio Semanal ---
                        // Enviamos el recordatorio, por ejemplo, el Lunes por la mañana.
                        // Si la hora actual es después de la hora de recordatorio Y es el día de inicio de semana
                        // Y no hemos enviado un recordatorio para esta semana aún.
                        // --- Cálculo de rangos de semanas ---
                        // La semana actual siempre se calcula a partir del StartDayOfWeekConfig
                        var currentWeekStart = GetStartOfWeek(now, startDayOfWeekConfig);
                        var currentWeekEnd = currentWeekStart.AddDays(6);

                        // La próxima semana es simplemente 7 días después de la semana actual
                        var nextWeekStart = currentWeekStart.AddDays(7);
                        var nextWeekEnd = nextWeekStart.AddDays(6);

                        // --- Lógica para Recordatorio Semanal Condicional ---
                        // 1. Es el día y la hora configurados para enviar recordatorios.
                        // 2. Aún no hemos enviado un recordatorio para esta *próxima semana* específica.
                        // 3. ¡Hay menús cargados para la próxima semana!
                        if (now.DayOfWeek == reminderDayOfWeek && now.TimeOfDay >= reminderTime &&
                            (_lastReminderSentForNextWeekStart == null || _lastReminderSentForNextWeekStart.Value < nextWeekStart.Date)) // Verifica si ya se envió para *esta* próxima semana
                        {
                            var nextWeekMenusExist = await dbContext.DailyMenus
                                                                    .AnyAsync(dm => dm.Date.Date >= nextWeekStart.Date && dm.Date.Date <= nextWeekEnd.Date);

                            if (nextWeekMenusExist)
                            {
                                logger.LogInformation($"Iniciando envío de recordatorios para la semana del {nextWeekStart.ToShortDateString()}.");
                                var users = await dbContext.Users.ToListAsync();

                                foreach (var user in users)
                                {
                                    // Puedes añadir lógica para filtrar usuarios que no necesitan recordatorio
                                    // o que ya tienen un menú completo para la semana.
                                    if (string.IsNullOrEmpty(user.EmailAddress)) continue;

                                    // NEW: Verificar si el usuario ya tiene selecciones para la próxima semana
                                    var userHasSelectionsForNextWeek = await dbContext.UserMenuSelections
                                        .AnyAsync(ums => ums.UserId == user.Id &&
                                                         ums.IsActive && // Solo consideramos selecciones activas
                                                         ums.DailyMenu.Date.Date >= nextWeekStart.Date &&
                                                         ums.DailyMenu.Date.Date <= nextWeekEnd.Date);

                                    if (!userHasSelectionsForNextWeek) // Solo envía si el usuario NO tiene selecciones para la próxima semana
                                    {
                                        var reminderSubject = $"¡Es hora de encargar tu menú para la semana del {nextWeekStart.ToShortDateString()} en AccuViandas!";
                                        var reminderBody = $"<p>Hola {user.Username},</p>" +
                                                           $"<p>¡El menú para la semana del <strong>{nextWeekStart.ToShortDateString()}</strong> ya está disponible!</p>" +
                                                           $"<p>Parece que aún no has hecho ninguna selección para esa semana. ¡Ingresa a la plataforma para elegir tus viandas!</p>" +
                                                           "<p>Saludos,<br/>El equipo de AccuViandas</p>";
                                        await emailService.SendEmailAsync(user.EmailAddress, reminderSubject, reminderBody);
                                        logger.LogInformation($"Recordatorio enviado a {user.EmailAddress} (semana: {nextWeekStart.ToShortDateString()}).");
                                    }
                                    else
                                    {
                                        logger.LogInformation($"Usuario {user.Username} ({user.EmailAddress}) ya tiene selecciones para la semana {nextWeekStart.ToShortDateString()}. No se envía recordatorio.");
                                    }
                                }
                                _lastReminderSentForNextWeekStart = nextWeekStart.Date; // Marcar que ya se envió para esta próxima semana
                                logger.LogInformation("Envío de recordatorios semanales completado.");
                            }
                            else
                            {
                                logger.LogInformation($"Menús para la semana del {nextWeekStart.ToShortDateString()} aún no están disponibles. No se envían recordatorios.");
                            }
                        }
                        // --- Lógica para Resumen Semanal de Pedidos ---
                        // Enviamos el resumen, por ejemplo, el Viernes al final del día.
                        // Si la hora actual es después de la hora de resumen Y es el día anterior al inicio de la semana (ej. Domingo si la semana empieza el Lunes)
                        // Y no hemos enviado un resumen para esta semana aún.
                        // --- Lógica para Resumen Semanal de Pedidos ---
                        // 1. Es el día y la hora configurados para enviar resúmenes.
                        // 2. Aún no hemos enviado un resumen para esta *semana actual* específica.
                        if (now.DayOfWeek == summaryDayOfWeek && now.TimeOfDay >= summaryTime &&
                            (_lastSummarySentForNextWeekStart == null || _lastSummarySentForNextWeekStart.Value < nextWeekStart.Date)) // MODIFICADO: Verifica contra nextWeekStart
                        {
                            logger.LogInformation($"Iniciando envío de resúmenes semanales de pedidos para la semana del {currentWeekStart.ToShortDateString()}.");
                            var users = await dbContext.Users.Include(u => u.Role).ToListAsync();

                            foreach (var user in users)
                            {
                                if (string.IsNullOrEmpty(user.EmailAddress)) continue;

                                // Obtener selecciones para la SEMANA ACTUAL (currentWeekStart a currentWeekEnd)
                                var userSelections = await dbContext.UserMenuSelections
                                   .Where(ums => ums.UserId == user.Id &&
                                               ums.IsActive &&
                                               ums.DailyMenu.Date.Date >= nextWeekStart.Date && // MODIFICADO: Usa nextWeekStart
                                               ums.DailyMenu.Date.Date <= nextWeekEnd.Date)   // MODIFICADO: Usa nextWeekEnd
                                   .Include(ums => ums.DailyMenu)
                                       .ThenInclude(dm => dm.Items)
                                   .OrderBy(ums => ums.DailyMenu.Date)
                                   .ToListAsync();

                                if (userSelections.Any())
                                {
                                    // MODIFICADO: Asunto y cuerpo del correo para la PRÓXIMA SEMANA
                                    var summarySubject = $"Confirmación de tu menú para la semana de AccuViandas ({nextWeekStart.ToShortDateString()} - {nextWeekEnd.ToShortDateString()})";
                                    var summaryBody = BuildWeeklySummaryEmailBody(user, userSelections, nextWeekStart, nextWeekEnd); // MODIFICADO: Pasa nextWeek dates
                                    await emailService.SendEmailAsync(user.EmailAddress, summarySubject, summaryBody);
                                    logger.LogInformation($"Resumen/confirmación semanal enviado a {user.EmailAddress} (semana: {nextWeekStart.ToShortDateString()}).");
                                }
                                else
                                {
                                    logger.LogInformation($"Usuario {user.Username} ({user.EmailAddress}) no tiene selecciones para la semana {currentWeekStart.ToShortDateString()}. No se envía resumen.");
                                }
                            }
                            _lastSummarySentForNextWeekStart = nextWeekStart.Date; // MODIFICADO: Marcar que se envió para *esta próxima semana*
                            logger.LogInformation("Envío de resúmenes/confirmaciones semanales completado.");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error occurred in MenuEmailBackgroundService.");
                    }
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
        

        // --- Helper Methods ---

        // Método para obtener el inicio de la semana (Lunes por defecto)
        private DateTime GetStartOfWeek(DateTime date, DayOfWeek startDay)
        {
            var diff = (7 + (date.DayOfWeek - startDay)) % 7;
            return date.AddDays(-1 * diff).Date;
        }

        // Método para construir el cuerpo del email de resumen semanal
        private string BuildWeeklySummaryEmailBody(User user, List<UserMenuSelection> selections, DateTime startDate, DateTime endDate)
        {
            var bodyBuilder = new StringBuilder();
            bodyBuilder.AppendLine($"<p>Hola {user.Username},</p>");
            bodyBuilder.AppendLine($"<p>Aquí tienes el resumen de tus selecciones de menú para la semana del <strong>{startDate.ToShortDateString()} al {endDate.ToShortDateString()}</strong>:</p>");
            bodyBuilder.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='width:100%; border-collapse: collapse;'>");
            bodyBuilder.AppendLine("<tr style='background-color:#f2f2f2;'><th>Fecha</th><th>Categoría</th><th>Plato</th><th>Observación</th></tr>");

            foreach (var selection in selections)
            {
                var menuItemName = selection.DailyMenu.Items.FirstOrDefault(item => item.Category == selection.SelectedCategory)?.Name ?? "N/A";
                var observation = string.IsNullOrEmpty(selection.Observation) ? "---" : selection.Observation;

                bodyBuilder.AppendLine($"<tr>");
                bodyBuilder.AppendLine($"<td>{selection.DailyMenu.Date.ToShortDateString()}</td>");
                bodyBuilder.AppendLine($"<td>{selection.SelectedCategory}</td>");
                bodyBuilder.AppendLine($"<td>{menuItemName}</td>");
                bodyBuilder.AppendLine($"<td>{observation}</td>");
                bodyBuilder.AppendLine($"</tr>");
            }
            bodyBuilder.AppendLine("</table>");
            bodyBuilder.AppendLine("<p>¡Que disfrutes tu menú!</p>");
            bodyBuilder.AppendLine("<p>Saludos,<br/>El equipo de AccuViandas</p>");

            return bodyBuilder.ToString();
        }
    }
}
