// Services/EmailService.cs
using Microsoft.Extensions.Configuration; // Para leer appsettings.json
using System.Net.Mail; // Para MailMessage, SmtpClient
using System.Net;     // Para NetworkCredential
using System.Threading.Tasks;
using System;         // Para InvalidOperationException

namespace AccuViandas.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string message)
        {
            // Leer la configuración desde appsettings.json
            var smtpServer = _configuration["EmailSettings:SmtpServer"];
            var smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"] ?? "587"); // Puerto por defecto si no se encuentra
            var smtpUsername = _configuration["EmailSettings:SmtpUsername"];
            var smtpPassword = _configuration["EmailSettings:SmtpPassword"];
            var senderEmail = _configuration["EmailSettings:SenderEmail"];
            var senderName = _configuration["EmailSettings:SenderName"];

            // Validación básica de que la configuración existe
            if (string.IsNullOrEmpty(smtpServer) || string.IsNullOrEmpty(smtpUsername) || string.IsNullOrEmpty(smtpPassword) || string.IsNullOrEmpty(senderEmail))
            {
                // En un entorno de producción, aquí deberías tener un logging más sofisticado
                throw new InvalidOperationException("Configuración de SMTP incompleta o faltante. Revise la sección 'EmailSettings' en appsettings.json.");
            }

            using (var client = new SmtpClient(smtpServer, smtpPort))
            {
                client.EnableSsl = true; // Habilitar SSL/TLS para una conexión segura
                client.UseDefaultCredentials = false; // No usar las credenciales del usuario logueado en el sistema
                client.Credentials = new NetworkCredential(smtpUsername, smtpPassword); // Usar las credenciales proporcionadas
                client.DeliveryMethod = SmtpDeliveryMethod.Network; // Enviar por red

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName), // Remitente
                    Subject = subject, // Asunto del correo
                    Body = message,    // Cuerpo del correo
                    IsBodyHtml = true // Asumimos que el cuerpo del correo será HTML para facilitar los reportes
                };
                mailMessage.To.Add(toEmail); // Destinatario

                await client.SendMailAsync(mailMessage); // ¡Enviar el correo!
            }
        }
    }
}