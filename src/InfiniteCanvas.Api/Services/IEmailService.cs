namespace InfiniteCanvas.Api.Services;

public interface IEmailService
{
    Task SendConfirmationAsync(string email, string token);
    Task SendPasswordResetAsync(string email, string token);
}
