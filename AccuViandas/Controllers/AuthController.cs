﻿using AccuViandas.Data;
using AccuViandas.Models;
using BCrypt.Net; // Necesario para el hashing de contraseñas
                  // Controllers/AuthController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;     // NEW
using System.IdentityModel.Tokens.Jwt;   // NEW
using System.Security.Claims;            // NEW
using System.Text;                       // NEW
using Microsoft.Extensions.Configuration; // NEW: Para IConfiguration
using AccuViandas.Services; // NEW
using Microsoft.AspNetCore.Authorization; // NEW (si no lo tenías ya)
using System; // NEW (para Exception)

namespace AccuViandas.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Ruta base: /api/Auth
    public class AuthController : ControllerBase
    {
        private readonly MenuDbContext _context;
        private readonly IConfiguration _configuration; // NEW: Para acceder a la configuración de JWT
        private readonly IEmailService _emailService; // NEW: Declarar el servicio de email

        // Constructor para inyectar el contexto de la base de datos
        public AuthController(MenuDbContext context, IConfiguration configuration, IEmailService emailService) // NEW: Inyectar IEmailService) // NEW: Inyectar IConfiguration)
        {
            _context = context; 
            _configuration = configuration; // NEW: Asignar la configuración
            _emailService = emailService; // NEW: Asignar el servicio de email
        }

        /// <summary>
        /// Registra un nuevo usuario en el sistema.
        /// Ejemplo de uso: POST /api/Auth/register
        /// Cuerpo de la petición (JSON):
        /// {
        ///   "username": "nuevoUsuario",
        ///   "password": "miContraseñaSegura",
        ///   "roleName": "User" // O "Admin", "Viewer"
        /// }
        /// </summary>
        [HttpPost("register")] // Define un endpoint POST específico para el registro
        public async Task<ActionResult> Register(UserRegistrationDto request)
        {
            // Validación básica
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.RoleName) || string.IsNullOrWhiteSpace(request.EmailAddress))
            {
                return BadRequest("Username, password, and email are required.");
            }

            // Verificar si el nombre de usuario ya existe
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return Conflict("Username already exists.");
            }

            // Verificar si el mail ya existe
            if (await _context.Users.AnyAsync(u => u.EmailAddress  == request.EmailAddress))
            {
                return Conflict("Email already exists.");
            }

            // Buscar el rol por nombre
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == request.RoleName);
            if (role == null)
            {
                return BadRequest($"Role '{request.RoleName}' not found.");
            }

            // Hashear la contraseña antes de guardarla
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Crear el nuevo usuario
            var newUser = new User
            {
                Username = request.Username,
                PasswordHash = passwordHash,
                RoleId = role.Id, // Asigna el ID del rol encontrado
                EmailAddress = request.EmailAddress // <-- ¡AÑADE ESTA LÍNEA!                                                   
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            //return Ok("User registered successfully!");
            return Ok(new { message = "User registered successfully!" }); // <-- ¡Devuelve un objeto JSON!
        }

        /// <summary>
        /// Permite a un usuario iniciar sesión.
        /// Ejemplo de uso: POST /api/Auth/login
        /// Cuerpo de la petición (JSON):
        /// {
        ///   "username": "usuarioExistente",
        ///   "password": "suContraseña"
        /// }
        /// </summary>
        /// <returns>Un mensaje de éxito y el rol del usuario si las credenciales son correctas.</returns>
        [HttpPost("login")] // Define un endpoint POST específico para el login
        public async Task<ActionResult> Login(UserLoginDto request)
        {
            // Buscar el usuario por nombre de usuario
            var user = await _context.Users
                                     .Include(u => u.Role) // Incluye la información del rol
                                     .FirstOrDefaultAsync(u => u.Username == request.Username);

            // Si el usuario no existe
            if (user == null)
            {
                return Unauthorized("Invalid credentials.");
            }

            // Verificar la contraseña hasheada
            bool isPasswordCorrect = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

            // Si la contraseña es incorrecta
            if (!isPasswordCorrect)
            {
                return Unauthorized("Invalid credentials.");
            }

            // Si las credenciales son correctas, devuelve un mensaje de éxito y el rol.
            // Para una aplicación real, aquí se generaría un token JWT o una sesión.
            // --- NEW: Generar el token JWT ---
            var token = GenerateJwtToken(user);
            // --- END NEW ---

            //return Ok(new { Message = "Login successful!", Role = user.Role.Name,
            //    Token = token // Devuelve el token JWT
            //});
            //

            // Si las credenciales son correctas, devuelve el token y los datos del usuario.
            return Ok(new LoginResponseDto
            {
                Message = "Login successful!",
                Username = user.Username,
                Role = user.Role.Name, // Accede al nombre del rol de forma segura
                Token = token // Devuelve el token JWT
            });
        }

        // --- NEW: Método para generar el JWT ---
        private string GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"];
            if (string.IsNullOrEmpty(jwtKey))
            {
                // Esto no debería ocurrir si Program.cs y appsettings.json están bien configurados,
                // pero es una buena práctica tener esta validación.
                throw new InvalidOperationException("JWT Secret Key no configurada.");
            }

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            // Claims: Información que queremos incluir en el token
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), // ID único del usuario
                new Claim(ClaimTypes.Name, user.Username)                // Nombre de usuario
            };

            // Añadir el rol como un claim si existe
            if (user.Role != null && !string.IsNullOrEmpty(user.Role.Name))
            {
                claims.Add(new Claim(ClaimTypes.Role, user.Role.Name));
            }

            var token = new JwtSecurityToken(
                issuer: null,      // En este setup simple, no necesitamos un emisor/audiencia específicos
                audience: null,
                claims: claims,
                expires: DateTime.Now.AddHours(1), // Token expira en 1 hora (ajustable)
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        // --- END NEW --
        
        /// <summary>
        /// Endpoint temporal para probar el envío de correos electrónicos.
        /// Requiere autenticación como 'Admin'.
        /// </summary>
        [HttpPost("test-email")]
        [Authorize(Roles = "Admin")] // Solo los administradores pueden usar este endpoint de prueba
        public async Task<ActionResult> TestSendEmail([FromBody] TestEmailDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ToEmail) || string.IsNullOrWhiteSpace(dto.Subject) || string.IsNullOrWhiteSpace(dto.Body))
            {
                return BadRequest("ToEmail, Subject y Body son obligatorios.");
            }

            try
            {
                await _emailService.SendEmailAsync(dto.ToEmail, dto.Subject, dto.Body);
                return Ok("¡Correo de prueba enviado exitosamente!");
            }
            catch (Exception ex)
            {
                // Loguea la excepción para depuración en la consola
                Console.WriteLine($"Error al enviar correo de prueba: {ex.Message}");
                // Devuelve un error 500 con detalles para el cliente
                return StatusCode(500, $"Error al enviar correo de prueba: {ex.Message}. Detalles: {ex.InnerException?.Message}");
            }
        }
        // --- FIN NEW: Endpoint Temporal ---
    }

    // --- Data Transfer Objects (DTOs) para las peticiones ---
    // Estas clases se usan para definir la estructura de los datos que se reciben en el cuerpo de las peticiones.

    public class UserRegistrationDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string RoleName { get; set; } // Para especificar el rol al registrar
        public string EmailAddress { get; set; } // <-- ¡AÑADE ESTA LÍNEA!
    }

    public class UserLoginDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
    // NEW DTO para la respuesta del Login, para incluir el Token
    public class LoginResponseDto
    {
        public string Message { get; set; }
        public string Username { get; set; } // Opcional, pero útil
        public string Role { get; set; }
        public string Token { get; set; } // EL TOKEN JWT
    }

    // --- NEW: Endpoint Temporal para Probar el Envío de Correos ---
    public class TestEmailDto
    {
        public string ToEmail { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }

        

}
