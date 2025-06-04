// Models/User.cs
using System.Collections.Generic;

namespace AccuViandas.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; } // Aquí guardaremos el hash de la contraseña

        // --- NEW: Propiedad para la dirección de correo electrónico ---
        public string EmailAddress { get; set; }
        // --- END NEW ---

        // Propiedad de navegación para los roles del usuario (relación muchos a muchos)
        //public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

        // Clave foránea para el rol del usuario (Un usuario tiene UN rol)
        public int RoleId { get; set; }
        // Propiedad de navegación para el rol
        public Role Role { get; set; } // Un usuario tiene UN objeto Role

        // --- NEW: Propiedad de navegación para las selecciones del usuario ---
        public ICollection<UserMenuSelection> UserMenuSelections { get; set; } = new List<UserMenuSelection>();
        // --- END NEW ---
    }
}
