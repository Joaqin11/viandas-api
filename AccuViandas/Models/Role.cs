// Models/Role.cs
using System.Collections.Generic;

namespace AccuViandas.Models
{
    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; } // Por ejemplo: "Admin", "User", "Viewer"

        // Propiedad de navegación para los usuarios que tienen este rol
        //public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

        // Propiedad de navegación para los usuarios que tienen este rol
        public ICollection<User> Users { get; set; } = new List<User>(); // Un rol puede tener MUCHOS usuarios

    }
}
