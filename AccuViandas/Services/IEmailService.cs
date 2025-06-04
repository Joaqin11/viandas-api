// Services/IEmailService.cs
using System.Threading.Tasks;

namespace AccuViandas.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string message);
    }
}