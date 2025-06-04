// Controllers/EmailController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization; // Para el atributo [Authorize]
using AccuViandas.Services;     // Para IEmailService
using AccuViandas.Data;         // Para MenuDbContext
using Microsoft.EntityFrameworkCore; // Para ToListAsync
using System.Threading.Tasks;
using System.Collections.Generic; // Para List<string>
using System.Linq;                // Para .Any(), .Where(), .Select()
using System;                     // Para Exception

namespace AccuViandas.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")] // Todo este controlador solo es accesible por usuarios con rol 'Admin'
    public class EmailController : ControllerBase
    {
        private readonly IEmailService _emailService;
        private readonly MenuDbContext _context; // Necesario para obtener las direcciones de correo de los usuarios

        public EmailController(IEmailService emailService, MenuDbContext context)
        {
            _emailService = emailService;
            _context = context;
        }

        // DTO (Data Transfer Object) para la petición de envío de correo personalizado
        public class SendCustomEmailDto
        {
            public List<string> ToEmails { get; set; } // Lista opcional de direcciones de correo específicas
            public string Subject { get; set; }        // Asunto del correo
            public string Body { get; set; }           // Cuerpo del correo (se asume HTML)
            public bool SendToAllUsers { get; set; } = false; // Flag para enviar a todos los usuarios registrados
        }

        /// <summary>
        /// Permite a un administrador enviar un correo electrónico personalizado.
        /// Puede enviarse a direcciones específicas (listadas en ToEmails) o a todos los usuarios
        /// registrados en la base de datos que tengan una dirección de correo válida (si SendToAllUsers es true).
        /// </summary>
        /// <param name="dto">Contiene la información del correo a enviar (destinatarios, asunto, cuerpo).</param>
        /// <returns>Mensaje de éxito o error.</returns>
        [HttpPost("send-custom")]
        public async Task<ActionResult> SendCustomEmail([FromBody] SendCustomEmailDto dto)
        {
            // Validación básica de los campos requeridos
            if (string.IsNullOrWhiteSpace(dto.Subject) || string.IsNullOrWhiteSpace(dto.Body))
            {
                return BadRequest("El Asunto y el Cuerpo del correo son obligatorios.");
            }

            List<string> recipientEmails = new List<string>();

            if (dto.SendToAllUsers)
            {
                // Si se desea enviar a todos los usuarios
                recipientEmails = await _context.Users
                                                .Where(u => !string.IsNullOrEmpty(u.EmailAddress)) // Solo usuarios con email válido
                                                .Select(u => u.EmailAddress)
                                                .ToListAsync();

                if (!recipientEmails.Any())
                {
                    return BadRequest("No se encontraron usuarios con direcciones de correo electrónico válidas para enviar el correo masivo.");
                }
            }
            else
            {
                // Si se desea enviar a direcciones específicas
                if (dto.ToEmails == null || !dto.ToEmails.Any())
                {
                    return BadRequest("Se debe especificar al menos una dirección de correo en 'ToEmails' o activar 'SendToAllUsers'.");
                }
                // Filtra las direcciones de correo nulas o vacías de la lista proporcionada
                recipientEmails = dto.ToEmails.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
                if (!recipientEmails.Any())
                {
                    return BadRequest("Las direcciones de correo proporcionadas en 'ToEmails' no son válidas o están vacías.");
                }
            }

            try
            {
                // Enviar un correo a cada destinatario
                foreach (var email in recipientEmails)
                {
                    await _emailService.SendEmailAsync(email, dto.Subject, dto.Body);
                }

                return Ok($"Correo(s) personalizado(s) enviado(s) exitosamente a {recipientEmails.Count} destinatario(s).");
            }
            catch (Exception ex)
            {
                // Registra el error en la consola para depuración
                Console.WriteLine($"Error al enviar correo personalizado: {ex.Message}");
                // Devuelve un error 500 al cliente con detalles
                return StatusCode(500, $"Error al enviar correo personalizado: {ex.Message}. Detalles: {ex.InnerException?.Message}");
            }
        }
    }
}
