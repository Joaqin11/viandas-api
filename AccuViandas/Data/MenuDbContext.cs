using AccuViandas.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace AccuViandas.Data
{
    public class MenuDbContext: DbContext
    {
        // Constructor: Esto permite que ASP.NET Core inyecte las opciones de configuración de la base de datos
        // cuando se crea una instancia de MenuDbContext.
        public MenuDbContext(DbContextOptions<MenuDbContext> options) : base(options) { }

        // DbSet para tus entidades:
        // Estas propiedades le dicen a Entity Framework Core que quieres mapear
        // las clases DailyMenu y DailyMenuItem a tablas en tu base de datos.
        // La tabla se llamará 'DailyMenus' y 'DailyMenuItems' por convención.
        public DbSet<DailyMenu> DailyMenus { get; set; }
        public DbSet<DailyMenuItem> DailyMenuItems { get; set; }

        // --- NEW: DbSets for Users and Roles ---
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        // --- END NEW ---

        // --- NEW: DbSet for UserMenuSelection ---
        public DbSet<UserMenuSelection> UserMenuSelections { get; set; }
        // --- END NEW ---

        // Método OnModelCreating (Opcional, pero útil para configuraciones avanzadas)
        // Este método se usa para configurar cómo las entidades se mapean a la base de datos.
        // Aquí podemos agregar reglas como índices únicos.
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configuramos un índice único en la propiedad 'Date' de DailyMenu.
            // Esto asegura que no puedas tener dos DailyMenu para la misma fecha en tu base de datos.
            modelBuilder.Entity<DailyMenu>()
                .HasIndex(m => m.Date)
                .IsUnique();

            // --- ¡AÑADIR ESTA CONFIGURACIÓN EXPLÍCITA DE LA RELACIÓN! ---
            modelBuilder.Entity<DailyMenuItem>()
                .HasOne(mi => mi.DailyMenu) // Un DailyMenuItem tiene un DailyMenu
                .WithMany(dm => dm.Items)   // Un DailyMenu tiene muchos DailyMenuItems (a través de la propiedad 'Items')
                .HasForeignKey(mi => mi.DailyMenuId) // La clave foránea es DailyMenuId en DailyMenuItem
                .OnDelete(DeleteBehavior.Cascade); // Cuando se borra un DailyMenu, sus DailyMenuItems se borran en cascada
                                                   // Y, lo más importante para tu problema:
                                                   // .IsRequired(); // Esta es la configuración por defecto, que causa el problema.
                                                   // No la necesitas si DailyMenuId es un int y no un int?.
                                                   // EF Core ya lo hace required.
                                                   // --- FIN DE LA ADICIÓN ---

            // --- NEW: User and Role Model Configuration ---

            // Configurar el índice único para Username en la tabla Users
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // Configurar la relación One-to-Many entre Role y User
            modelBuilder.Entity<User>()
                .HasOne(u => u.Role) // Un usuario tiene un rol
                .WithMany(r => r.Users) // Un rol tiene muchos usuarios
                .HasForeignKey(u => u.RoleId) // La clave foránea en User es RoleId
                .IsRequired(); // Un usuario siempre debe tener un rol

            // Configurar el índice único para Name en la tabla Roles
            modelBuilder.Entity<Role>()
                .HasIndex(r => r.Name)
                .IsUnique();

            // --- Seed Initial Data for Roles (Optional but Recommended) ---
            // Esto precarga algunos roles en la base de datos cuando se crea por primera vez.
            modelBuilder.Entity<Role>().HasData(
                new Role { Id = 1, Name = "Admin" },
                new Role { Id = 2, Name = "User" },
                new Role { Id = 3, Name = "Viewer" }
            );
            // --- END NEW ---

            // --- NEW: UserMenuSelection Model Configuration ---

            // Configurar la relación One-to-Many entre User y UserMenuSelection
            modelBuilder.Entity<UserMenuSelection>()
                .HasOne(ums => ums.User) // Una selección tiene un usuario
                .WithMany(u => u.UserMenuSelections) // Un usuario tiene muchas selecciones
                .HasForeignKey(ums => ums.UserId) // La clave foránea en UserMenuSelection es UserId
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict); // Evita borrar usuarios si tienen selecciones

            // Configurar la relación One-to-Many entre DailyMenu y UserMenuSelection
            modelBuilder.Entity<UserMenuSelection>()
                .HasOne(ums => ums.DailyMenu) // Una selección pertenece a un menú diario
                .WithMany() // Un menú diario puede tener muchas selecciones (pero no necesitamos una colección directa en DailyMenu)
                .HasForeignKey(ums => ums.DailyMenuId) // La clave foránea en UserMenuSelection es DailyMenuId
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade); // Si se borra un DailyMenu, sus selecciones también se borran

            // Clave única compuesta para asegurar que un usuario solo pueda tener una selección activa por DailyMenu
            //modelBuilder.Entity<UserMenuSelection>()
            //    .HasIndex(ums => new { ums.UserId, ums.DailyMenuId, ums.IsActive })
            //    .IsUnique()
            //    .HasFilter("IsActive = 1"); // Solo aplica la unicidad si IsActive es TRUE

            // --- END NEW ---

            // Llama al método base para que EF Core pueda hacer sus configuraciones por defecto
            base.OnModelCreating(modelBuilder);
        }
    }
}
