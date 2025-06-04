// Data/ArchiveDbContext.cs
using Microsoft.EntityFrameworkCore;
using AccuViandas.Models; // Necesitas los modelos existentes

namespace AccuViandas.Data
{
    public class ArchiveDbContext : DbContext
    {
        public ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : base(options)
        {
        }

        // Define los DbSets para las entidades que archivarás
        public DbSet<User> Users { get; set; }
        public DbSet<DailyMenu> DailyMenus { get; set; }
        public DbSet<DailyMenuItem> MenuItems { get; set; } // Aunque DailyMenu ya tiene Items, esto es por si accedes directamente
        public DbSet<UserMenuSelection> UserMenuSelections { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configuración de relaciones si es necesario, similar a MenuDbContext
            // Por ejemplo, para asegurar la eliminación en cascada si es lo que deseas para el archivo,
            // o para desactivarla para ciertos escenarios.
            // En este caso, simplemente usa la configuración por defecto o copia la de MenuDbContext si es especial.

            // Configurar la relación entre DailyMenu y UserMenuSelection
            modelBuilder.Entity<UserMenuSelection>()
                .HasOne(ums => ums.DailyMenu)
                .WithMany() // No es una colección en DailyMenu
                .HasForeignKey(ums => ums.DailyMenuId)
                .OnDelete(DeleteBehavior.Restrict); // O .NoAction, para evitar borrados en cascada inesperados en el archivo

            // Configurar la relación entre UserMenuSelection y User
            modelBuilder.Entity<UserMenuSelection>()
                .HasOne(ums => ums.User)
                .WithMany() // No es una colección en User
                .HasForeignKey(ums => ums.UserId)
                .OnDelete(DeleteBehavior.Restrict); // O .NoAction

            // Configurar DailyMenu y MenuItem (uno a muchos)
            modelBuilder.Entity<DailyMenu>()
                .HasMany(dm => dm.Items)
                .WithOne(mi => mi.DailyMenu)
                .HasForeignKey(mi => mi.DailyMenuId)
                .OnDelete(DeleteBehavior.Cascade); // Si quieres que los items se borren con el DailyMenu en el archivo

            base.OnModelCreating(modelBuilder);
        }
    }
}
