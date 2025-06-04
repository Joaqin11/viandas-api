// Services/DataMaintenanceBackgroundService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using AccuViandas.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration; // Para leer la configuración

namespace AccuViandas.Services
{
    public class DataMaintenanceBackgroundService : BackgroundService
    {
        private readonly ILogger<DataMaintenanceBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _pollingInterval;
        private readonly int _dataRetentionDays;

        public DataMaintenanceBackgroundService(
            ILogger<DataMaintenanceBackgroundService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration) // Inyectar IConfiguration
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            // Leer configuración de appsettings.json
            _pollingInterval = TimeSpan.FromMinutes(configuration.GetValue<int>("DataRetentionSettings:PollingIntervalMinutes", 60));
            _dataRetentionDays = configuration.GetValue<int>("DataRetentionSettings:DataRetentionDays", 30);

            _logger.LogInformation("Data Maintenance Service initialized. Polling every {Interval} minutes. Retaining {Days} days of data.",
                _pollingInterval.TotalMinutes, _dataRetentionDays);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Data Maintenance Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Data Maintenance Service performing check at: {time}", DateTimeOffset.Now);

                try
                {
                    await PerformDataMaintenance(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing Data Maintenance.");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }

            _logger.LogInformation("Data Maintenance Service is stopping.");
        }

        private async Task PerformDataMaintenance(CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var mainDbContext = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
                var archiveDbContext = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();

                // Asegurarse de que la DB de archivo esté creada y su esquema aplicado
                await archiveDbContext.Database.EnsureCreatedAsync(stoppingToken);

                var cutoffDate = DateTime.Today.AddDays(-_dataRetentionDays).Date;
                _logger.LogInformation("Cutoff date for data archiving/deletion: {CutoffDate}", cutoffDate.ToShortDateString());

                // Obtener los DailyMenus que serán archivados, incluyendo sus Items
                var oldDailyMenus = await mainDbContext.DailyMenus
                    .Where(dm => dm.Date.Date < cutoffDate)
                    .Include(dm => dm.Items)
                    .OrderBy(dm => dm.Date) // Ordenar por fecha para procesar cronológicamente
                    .ToListAsync(stoppingToken);

                if (!oldDailyMenus.Any())
                {
                    _logger.LogInformation("No old daily menus found for archiving/deletion.");
                    return;
                }

                _logger.LogInformation("Found {Count} old daily menus to process.", oldDailyMenus.Count);

                // Mapeo de OldDailyMenuId -> NewDailyMenuId para mantener relaciones
                var dailyMenuIdMap = new Dictionary<int, int>();
                var selectionsToArchive = new List<Models.UserMenuSelection>(); // Para recopilar todas las selecciones a archivar
                var selectionsToDeleteFromMain = new List<Models.UserMenuSelection>(); // Para recopilar todas las selecciones a eliminar de la principal

                // Iniciar transacciones en ambas DBs para asegurar la atomicidad
                // Si algo falla, se hará rollback en ambas
                using (var mainTransaction = await mainDbContext.Database.BeginTransactionAsync(stoppingToken))
                using (var archiveTransaction = await archiveDbContext.Database.BeginTransactionAsync(stoppingToken))
                {
                    try
                    {
                        foreach (var oldDailyMenu in oldDailyMenus)
                        {
                            // A. Archivar DailyMenu y sus MenuItems asociados en la DB de archivo
                            var newDailyMenu = new Models.DailyMenu
                            {
                                Date = oldDailyMenu.Date,
                                Items = oldDailyMenu.Items.Select(item => new Models.DailyMenuItem
                                {
                                    // Id no se asigna, SQLite lo autoincrementará
                                    Category = item.Category,
                                    Name = item.Name
                                }).ToList() // Los items se añadirán junto con el DailyMenu
                            };
                            archiveDbContext.DailyMenus.Add(newDailyMenu);
                            await archiveDbContext.SaveChangesAsync(stoppingToken); // Guarda y genera el nuevo Id para newDailyMenu

                            dailyMenuIdMap[oldDailyMenu.Id] = newDailyMenu.Id; // Guardar el mapeo de IDs

                            // B. Preparar UserMenuSelections asociadas para archivar
                            // Cargar UserMenuSelections relacionadas
                            var oldSelectionsForThisMenu = await mainDbContext.UserMenuSelections
                                .Where(ums => ums.DailyMenuId == oldDailyMenu.Id)
                                .ToListAsync(stoppingToken);

                            foreach (var oldSelection in oldSelectionsForThisMenu)
                            {
                                var newSelection = new Models.UserMenuSelection
                                {
                                    // Id no se asigna, SQLite lo autoincrementará
                                    UserId = oldSelection.UserId,
                                    DailyMenuId = newDailyMenu.Id, // <-- Usa el NUEVO ID del DailyMenu archivado
                                    SelectedCategory = oldSelection.SelectedCategory,
                                    Observation = oldSelection.Observation,
                                    IsActive = oldSelection.IsActive,
                                    SelectionDateTime = oldSelection.SelectionDateTime
                                };
                                selectionsToArchive.Add(newSelection);
                                selectionsToDeleteFromMain.Add(oldSelection); // Marcar para eliminación de la principal
                            }
                        }

                        // C. Guardar todas las UserMenuSelections preparadas en la DB de archivo en un solo batch (si es posible)
                        // o después del bucle principal
                        if (selectionsToArchive.Any())
                        {
                            archiveDbContext.UserMenuSelections.AddRange(selectionsToArchive);
                            await archiveDbContext.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("Successfully archived {Count} user menu selections.", selectionsToArchive.Count);
                        }
                        _logger.LogInformation("Successfully archived {Count} daily menus and their associated data.", oldDailyMenus.Count);


                        // D. Eliminar de la base de datos principal (solo si el archivado fue exitoso)
                        // Es crucial eliminar las selecciones primero para evitar problemas de FK si DailyMenu se borra antes
                        // y no hay Cascade Delete perfecto o si no se cargaron todas las selecciones.
                        if (selectionsToDeleteFromMain.Any())
                        {
                            mainDbContext.UserMenuSelections.RemoveRange(selectionsToDeleteFromMain);
                            await mainDbContext.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("Successfully deleted {Count} user menu selections from main DB.", selectionsToDeleteFromMain.Count);
                        }

                        // Eliminar DailyMenus. Si la relación DailyMenu -> MenuItem tiene DeleteBehavior.Cascade en MenuDbContext,
                        // los MenuItems asociados a estos DailyMenus también se eliminarán automáticamente de la DB principal.
                        mainDbContext.DailyMenus.RemoveRange(oldDailyMenus);
                        await mainDbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("Successfully deleted {Count} old daily menus from main DB.", oldDailyMenus.Count);

                        // E. Confirmar ambas transacciones
                        await archiveTransaction.CommitAsync(stoppingToken);
                        await mainTransaction.CommitAsync(stoppingToken);
                        _logger.LogInformation("Data maintenance completed successfully for data older than {CutoffDate}.", cutoffDate.ToShortDateString());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during data archiving/deletion process. Rolling back transactions.");
                        await mainTransaction.RollbackAsync(stoppingToken);
                        await archiveTransaction.RollbackAsync(stoppingToken);
                        throw; // Re-lanzar para que el ExecuteAsync lo capture
                    }
                }
            }
        }
    }
}
